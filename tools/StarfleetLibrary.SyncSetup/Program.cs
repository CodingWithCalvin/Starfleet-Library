using StarfleetLibrary.Core;
using StarfleetLibrary.Sync;
using StarfleetLibrary.Uno.Services;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

if(args.FirstOrDefault()=="connect")
{
    var token=(await Console.In.ReadLineAsync())?.Trim() ?? throw new InvalidOperationException("Missing service credential.");
    using var test=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(SyncClient.HostedUrl)};
    test.DefaultRequestHeaders.Add("OAI-Sites-Authorization","Bearer "+token);
    _=await new SyncClient(test).GetAsync();
    var buffer=CryptographicBuffer.ConvertStringToBinary(token,BinaryStringEncoding.Utf8);
    var protectedBuffer=await new DataProtectionProvider("LOCAL=user").ProtectAsync(buffer);
    CryptographicBuffer.CopyToByteArray(protectedBuffer,out var bytes);
    Directory.CreateDirectory(Path.GetDirectoryName(SyncAccess.CredentialPath)!);
    await File.WriteAllBytesAsync(SyncAccess.CredentialPath,bytes);
    Console.WriteLine("Private sync credential verified and encrypted for this Windows user.");
    return;
}
var root=args.Length>1 ? args[1] : UnoLibraryStore.DefaultRoot;
using var http=args.Length>2 ? new HttpClient{BaseAddress=new Uri(args[2]),Timeout=TimeSpan.FromSeconds(40)} : await SyncAccess.OpenAsync() ?? throw new InvalidOperationException("Connect the private service first.");
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
