using StarfleetLibrary.Core;
using StarfleetLibrary.Sync;
using StarfleetLibrary.Uno.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if(args.FirstOrDefault()=="initialize")
{
    if(args.Length!=2)throw new ArgumentException("Pass the private service HTTPS origin.");
    var settings=await SyncAccess.ReadAsync() ?? new PrivateSyncSettings(args[1],Guid.NewGuid(),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    if(settings.ServerUrl!=args[1])throw new InvalidOperationException("A different vault is already configured.");
    await SyncAccess.SaveAsync(settings);
    // These server-side values do not disclose the device token or encryption key.
    Console.WriteLine(JsonSerializer.Serialize(new { VAULT_ID=settings.VaultId, DEVICE_TOKEN_HASHES=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings.DeviceToken))).ToLowerInvariant() }));
    return;
}
if(args.FirstOrDefault()=="connect")
{
    var json=(await Console.In.ReadLineAsync())?.Trim() ?? throw new InvalidOperationException("Missing private connection.");
    var settings=JsonSerializer.Deserialize(json,PrivateJson.Context.PrivateSyncSettings) ?? throw new InvalidDataException("Invalid connection.");
    using var test=settings.CreateClient();await new SyncClient(test).GetAsync();
    await SyncAccess.SaveAsync(settings);
    Console.WriteLine("Private connection verified and protected for this Windows user.");
    return;
}
var root=args.Length>1 ? args[1] : UnoLibraryStore.DefaultRoot;
using var http=await SyncAccess.OpenAsync() ?? throw new InvalidOperationException("Connect the private service first.");
if (args.FirstOrDefault()=="verify")
{
    var entries=await new SyncClient(http).GetAsync();
    var active=entries.Where(entry=>entry.Book is not null).ToArray();
    var hashes=active.Where(entry=>entry.Cover is not null).Select(entry=>entry.Cover!).Distinct().ToArray();
    await Parallel.ForEachAsync(hashes,new ParallelOptions { MaxDegreeOfParallelism=4 },async (hash,ct)=>
    {
        using var response=await http.GetAsync("api/covers/"+hash,ct);
        response.EnsureSuccessStatusCode();
        var bytes=await response.Content.ReadAsByteArrayAsync(ct);
        if(!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(hash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A hosted cover failed its checksum.");
    });
    Console.WriteLine($"Verified {active.Length} hosted editions, {active.Count(entry=>entry.Cover is not null)} editions with covers, and {hashes.Length} cover checksums.");
    return;
}
using var coverHttp=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
var store=new UnoLibraryStore(root);await store.InitializeAsync();
if(store.IsReadOnly)throw new InvalidOperationException(store.LoadError);
Console.WriteLine($"Syncing {store.Books.Count} editions and their covers…");
await store.SynchronizeAsync(http,new CoverImageCache(coverHttp,store.CacheDirectory,store.CoverDirectory));
Console.WriteLine($"Saved {store.Books.Count} editions; {store.SyncConflicts.Length} conflicts require review.");
