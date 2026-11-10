using System.Text;
using System.Text.Json;
using StarfleetLibrary.Sync;
#if __IOS__
using Foundation;
using Security;
#else
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
#endif

namespace StarfleetLibrary.Mobile.Services;

public static class PrivateConnection
{
    public static PrivateSyncSettings Parse(string code)
    {
        var value=code.Trim();
        if(value.StartsWith("starfleet-v1:"))value=Encoding.UTF8.GetString(Convert.FromBase64String(value[13..]));
        var settings=JsonSerializer.Deserialize(value,PrivateJson.Context.PrivateSyncSettings) ?? throw new InvalidDataException("Invalid connection code.");
        settings.Validate();return settings;
    }
    public static async Task<PrivateSyncSettings?> ReadAsync()
    {
        string? json;
#if __IOS__
        using var query=new SecRecord(SecKind.GenericPassword) { Service="com.calvin.starfleetlibrary.sync",Account="private-vault",Synchronizable=false };
        using var record=SecKeyChain.QueryAsRecord(query,out var status);
        if(status==SecStatusCode.ItemNotFound)return null;
        if(status!=SecStatusCode.Success)throw new InvalidOperationException("Unlock your iPhone to connect to your library.");
        json=record?.ValueData is { } value ? Encoding.UTF8.GetString(value.ToArray()) : null;
        await Task.CompletedTask;
#else
        var file=Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path,"private-connection.bin");
        if(!File.Exists(file))return null;
        var clear=await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(file)));
        json=CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8,clear);
#endif
        return json is null ? null : Parse(json);
    }
    public static async Task SaveAsync(PrivateSyncSettings settings)
    {
        settings.Validate();var old=await ReadAsync();
        if(old is not null && (old.VaultId!=settings.VaultId || old.EncryptionKey!=settings.EncryptionKey || old.ServerUrl!=settings.ServerUrl))
            throw new InvalidOperationException("This app is already paired to another library. Keep its local changes safe before changing the connection.");
        var json=JsonSerializer.Serialize(settings,PrivateJson.Context.PrivateSyncSettings);
#if __IOS__
        using var query=new SecRecord(SecKind.GenericPassword) { Service="com.calvin.starfleetlibrary.sync",Account="private-vault",Synchronizable=false };
        using var data=NSData.FromString(json);
        using var record=new SecRecord(SecKind.GenericPassword) { Service=query.Service,Account=query.Account,ValueData=data,Accessible=SecAccessible.WhenUnlockedThisDeviceOnly,Synchronizable=false };
        var status=SecKeyChain.Add(record);
        if(status==SecStatusCode.DuplicateItem) { using var update=new SecRecord { ValueData=data };status=SecKeyChain.Update(query,update); }
        if(status!=SecStatusCode.Success)throw new InvalidOperationException("The private connection could not be saved in Keychain.");
#else
        var folder=Windows.Storage.ApplicationData.Current.LocalFolder.Path;Directory.CreateDirectory(folder);
        var encrypted=await new DataProtectionProvider("LOCAL=user").ProtectAsync(CryptographicBuffer.ConvertStringToBinary(json,BinaryStringEncoding.Utf8));
        CryptographicBuffer.CopyToByteArray(encrypted,out var bytes);
        var file=Path.Combine(folder,"private-connection.bin");await File.WriteAllBytesAsync(file+".tmp",bytes);File.Move(file+".tmp",file,true);
#endif
    }
}
