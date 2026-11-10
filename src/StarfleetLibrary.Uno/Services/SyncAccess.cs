using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using System.Text.Json;
using StarfleetLibrary.Sync;

namespace StarfleetLibrary.Uno.Services;

public static class SyncAccess
{
    public static string CredentialPath => Path.Combine(UnoLibraryStore.DefaultRoot,"private-sync-access.bin");
    public static async Task<PrivateSyncSettings?> ReadAsync()
    {
        if (!File.Exists(CredentialPath)) return null;
        var encrypted=CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(CredentialPath));
        var decrypted=await new DataProtectionProvider().UnprotectAsync(encrypted);
        return JsonSerializer.Deserialize(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8,decrypted),PrivateJson.Context.PrivateSyncSettings)
            ?? throw new InvalidDataException("Invalid private connection.");
    }
    public static async Task SaveAsync(PrivateSyncSettings settings)
    {
        settings.Validate();
        var existing=await ReadAsync();
        if(existing is not null && (existing.VaultId!=settings.VaultId || existing.ServerUrl!=settings.ServerUrl || existing.EncryptionKey!=settings.EncryptionKey))
            throw new InvalidOperationException("This PC is already paired to another vault. Back up and disconnect it before changing vaults.");
        var plain=CryptographicBuffer.ConvertStringToBinary(JsonSerializer.Serialize(settings,PrivateJson.Context.PrivateSyncSettings),BinaryStringEncoding.Utf8);
        var encrypted=await new DataProtectionProvider("LOCAL=user").ProtectAsync(plain);
        CryptographicBuffer.CopyToByteArray(encrypted,out var bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath)!);
        await File.WriteAllBytesAsync(CredentialPath+".tmp",bytes);File.Move(CredentialPath+".tmp",CredentialPath,true);
    }
    public static async Task<HttpClient?> OpenAsync() => (await ReadAsync())?.CreateClient();
}
