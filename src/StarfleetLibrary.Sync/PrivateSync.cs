using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Sync;

public sealed record PrivateSyncSettings(string ServerUrl, Guid VaultId, string DeviceToken, string EncryptionKey)
{
    public Uri Validate()
    {
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            throw new InvalidDataException("Use the HTTPS address of your private sync service, without a path.");
        if (uri.Host.EndsWith("chatgpt.site", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ChatGPT Sites connections are no longer supported.");
        if (VaultId == Guid.Empty || Convert.FromBase64String(EncryptionKey).Length != 32 ||
            Convert.FromBase64String(DeviceToken).Length != 32)
            throw new InvalidDataException("The connection needs a vault ID, a 256-bit device token and a 256-bit encryption key.");
        return uri;
    }

    public HttpClient CreateClient(HttpMessageHandler? transport = null)
    {
        var origin = Validate();
        return new HttpClient(new EncryptedSyncHandler(this, transport ?? new HttpClientHandler { AllowAutoRedirect = false }))
        { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(40) };
    }
}

public sealed record VaultEntry(Guid Id, long Revision, string Ciphertext);
public sealed record VaultWrite(Guid Id, long ExpectedRevision, string Ciphertext);
public sealed record VaultPayload(Book? Book, string? Cover);

// Only this device-side adapter sees plaintext. The Worker receives AES-GCM
// envelopes and keyed cover IDs, and never receives the encryption key.
public sealed class VaultCrypto : IDisposable
{
    private readonly byte[] key;
    private readonly Guid vault;
    public VaultCrypto(PrivateSyncSettings settings) { settings.Validate(); key = Convert.FromBase64String(settings.EncryptionKey); vault = settings.VaultId; }
    private byte[] Aad(string purpose) => Encoding.UTF8.GetBytes($"starfleet-v1/{vault:D}/{purpose}");
    public byte[] Encrypt(byte[] plaintext, string purpose)
    {
        var result = new byte[29 + plaintext.Length]; result[0] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(1, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(1, 12), plaintext, result.AsSpan(29), result.AsSpan(13, 16), Aad(purpose));
        return result;
    }
    public byte[] Decrypt(byte[] envelope, string purpose)
    {
        if (envelope.Length < 29 || envelope[0] != 1) throw new InvalidDataException("Invalid encrypted data.");
        var result = new byte[envelope.Length - 29];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(envelope.AsSpan(1, 12), envelope.AsSpan(29), envelope.AsSpan(13, 16), result, Aad(purpose));
        return result;
    }
    public string CoverId(string hash) => Convert.ToHexString(HMACSHA256.HashData(key, Aad("cover-id/" + hash))).ToLowerInvariant();
    public VaultWrite EncryptWrite(SyncWrite value) => new(value.Id, value.ExpectedRevision,
        Convert.ToBase64String(Encrypt(JsonSerializer.SerializeToUtf8Bytes(new VaultPayload(value.Book, value.Cover), PrivateJson.Context.VaultPayload), $"entry/{value.Id:D}/{value.ExpectedRevision + 1}")));
    public SyncEntry DecryptEntry(VaultEntry entry)
    {
        if (entry.Id == Guid.Empty || entry.Revision < 1) throw new InvalidDataException("Invalid encrypted edition revision.");
        var payload = JsonSerializer.Deserialize(Decrypt(Convert.FromBase64String(entry.Ciphertext), $"entry/{entry.Id:D}/{entry.Revision}"), PrivateJson.Context.VaultPayload)
            ?? throw new InvalidDataException("Empty encrypted edition.");
        if (payload.Book is { } book && (book.Id != entry.Id || book.ValidationError is not null) ||
            payload.Cover is { } cover && (cover.Length != 64 || !cover.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid decrypted edition.");
        return new(entry.Id, entry.Revision, payload.Book, payload.Cover);
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(key);
}

public sealed class EncryptedSyncHandler : HttpMessageHandler
{
    private readonly HttpClient transport;
    private readonly VaultCrypto crypto;
    public EncryptedSyncHandler(PrivateSyncSettings settings, HttpMessageHandler handler)
    {
        transport = new(handler) { BaseAddress = settings.Validate(), Timeout = TimeSpan.FromSeconds(40) };
        transport.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);
        transport.DefaultRequestHeaders.Add("X-Starfleet-Vault", settings.VaultId.ToString("D"));
        crypto = new(settings);
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/entries")
        {
            using var wire = new HttpRequestMessage(request.Method, "v1/entries");
            if (request.Method == HttpMethod.Post)
            {
                var write = await request.Content!.ReadFromJsonAsync(SyncJson.Context.SyncWrite, ct) ?? throw new InvalidDataException("Missing edition.");
                wire.Content = JsonContent.Create(crypto.EncryptWrite(write), PrivateJson.Context.VaultWrite);
            }
            using var response = await transport.SendAsync(wire, ct);
            if (response.StatusCode != HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
            if (request.Method == HttpMethod.Get)
            {
                var entries = await response.Content.ReadFromJsonAsync(PrivateJson.Context.VaultEntryArray, ct) ?? throw new InvalidDataException("Missing encrypted catalog.");
                return new(response.StatusCode) { Content = JsonContent.Create(entries.Select(crypto.DecryptEntry).ToArray(), SyncJson.Context.SyncEntryArray) };
            }
            var entry = await response.Content.ReadFromJsonAsync(PrivateJson.Context.VaultEntry, ct) ?? throw new InvalidDataException("Missing encrypted acknowledgement.");
            return new(response.StatusCode) { Content = JsonContent.Create(crypto.DecryptEntry(entry), SyncJson.Context.SyncEntry) };
        }
        const string prefix = "/api/covers/";
        if (!path.StartsWith(prefix)) throw new InvalidOperationException("Unsupported private sync request.");
        var hash = path[prefix.Length..];
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid cover hash.");
        var id = crypto.CoverId(hash);
        using var coverRequest = new HttpRequestMessage(request.Method, "v1/covers/" + id);
        if (request.Method == HttpMethod.Put)
            coverRequest.Content = new ByteArrayContent(crypto.Encrypt(await request.Content!.ReadAsByteArrayAsync(ct), "cover/" + id));
        using var coverResponse = await transport.SendAsync(coverRequest, ct);
        coverResponse.EnsureSuccessStatusCode();
        if (request.Method == HttpMethod.Put) return new(HttpStatusCode.NoContent);
        var bytes = crypto.Decrypt(await coverResponse.Content.ReadAsByteArrayAsync(ct), "cover/" + id);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Cover checksum mismatch.");
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }
    protected override void Dispose(bool disposing) { if (disposing) { transport.Dispose(); crypto.Dispose(); } base.Dispose(disposing); }
}

public static class PrivateJson { public static PrivateJsonContext Context { get; } = new(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true }); }
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PrivateSyncSettings))]
[JsonSerializable(typeof(VaultEntry[]))]
[JsonSerializable(typeof(VaultWrite))]
[JsonSerializable(typeof(VaultPayload))]
public partial class PrivateJsonContext : JsonSerializerContext { }
