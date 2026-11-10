using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;
using StarfleetLibrary.Sync;

namespace StarfleetLibrary.Uno.Services;

public static class SyncAccess
{
    public static string CredentialPath => Path.Combine(UnoLibraryStore.DefaultRoot,"sync-access.bin");
    public static async Task<HttpClient?> OpenAsync()
    {
        if (!File.Exists(CredentialPath)) return null;
        var encrypted=CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(CredentialPath));
        var decrypted=await new DataProtectionProvider().UnprotectAsync(encrypted);
        var token=CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8,decrypted);
        var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri(SyncClient.HostedUrl),Timeout=TimeSpan.FromSeconds(40) };
        client.DefaultRequestHeaders.Add("OAI-Sites-Authorization","Bearer "+token);
        return client;
    }
}
