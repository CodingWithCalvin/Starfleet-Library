using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using StarfleetLibrary.Core;
using StarfleetLibrary.Sync;

// Synthetic cross-runtime fixture for the read-only browser's Web Crypto tests.
// These all-zero test credentials are unrelated to any deployed library.
if(args.Length==2 && args[0]=="--browser-fixture")
{
    var sample=new PrivateSyncSettings("https://vault.test/",Guid.Parse("a1d3787e-8d62-42c5-896f-75df6617911a"),Convert.ToBase64String(new byte[32]),Convert.ToBase64String(new byte[32]));
    using var cipher=new VaultCrypto(sample);
    var edition=new Book {Id=Guid.Parse("7e3fce9e-e1e3-4bdf-a7f5-5bf74d0d63cd"),Title="Strange New Worlds — The High Country",Authors="Synthetic fixture",Series=TrekSeries.StrangeNewWorlds,Format=BookFormat.Ebook,Notes="<script>must remain text</script>",AddedAt=DateTimeOffset.Parse("2026-01-01T00:00:00Z")};
    var fixtureImage=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aV1cAAAAASUVORK5CYII=");
    var imageHash=Convert.ToHexString(SHA256.HashData(fixtureImage)).ToLowerInvariant();
    var imageId=cipher.CoverId(imageHash);
    var fixtureWrite=cipher.EncryptWrite(new(edition.Id,6,edition,imageHash));
    var deleted=cipher.EncryptWrite(new(edition.Id,7,null,null));
    var fixture=new {description="Synthetic .NET VaultCrypto interoperability fixture; no real credentials or catalog data.",settings=sample,book=edition,entry=new VaultEntry(edition.Id,7,fixtureWrite.Ciphertext),tombstone=new VaultEntry(edition.Id,8,deleted.Ciphertext),coverHash=imageHash,coverId=imageId,cover=Convert.ToBase64String(cipher.Encrypt(fixtureImage,"cover/"+imageId)),image=Convert.ToBase64String(fixtureImage)};
    await File.WriteAllTextAsync(args[1],System.Text.Json.JsonSerializer.Serialize(fixture,new System.Text.Json.JsonSerializerOptions {PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase,WriteIndented=true}));
    Console.WriteLine("Wrote synthetic .NET browser interoperability fixture.");
    return;
}

var settings=new PrivateSyncSettings("https://vault.example/",Guid.NewGuid(),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
var server=new VaultServer();using var desktop=settings.CreateClient(server);using var phone=settings.CreateClient(server);
var book=new Book { Title="A private Star Trek title",Notes="These notes must never reach the service in plaintext" };
var pc=await new SyncClient(desktop).SynchronizeAsync([book],[]);
var firstEntry=server.Entries[book.Id];
Check(pc.Books.Single()==book,"Encrypted upload returns the original edition");
var mobile=await new SyncClient(phone).SynchronizeAsync([],[]);
Check(mobile.Books.Single()==book,"A paired device decrypts the same edition");
Check(!server.LastBody.Contains(book.Title) && !server.LastBody.Contains(book.Notes) && !server.LastBody.Contains(settings.EncryptionKey),"Wire payload contains no catalog plaintext or encryption key");
pc=await new SyncClient(desktop).SynchronizeAsync([book with {Notes="Windows change"}],pc.Baseline);
mobile=await new SyncClient(phone).SynchronizeAsync([book with {Notes="Phone change"}],mobile.Baseline);
Check(mobile.Conflicts.Length==1 && mobile.Books.Single().Notes=="Phone change","Encrypted concurrent edits retain the local version for review");
var latestEntry=server.Entries[book.Id];server.Entries[book.Id]=firstEntry;
try{await new SyncClient(desktop).SynchronizeAsync(pc.Books,pc.Baseline);throw new Exception("Rollback accepted");}catch(InvalidDataException){Console.WriteLine("PASS Replayed older revisions cannot roll back a paired device");}
server.Entries[book.Id]=latestEntry;
var image=new byte[]{255,216,255,224,1,2,3,4};var hash=Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
await new SyncClient(desktop).UploadCoverAsync(hash,image);
Check(server.Covers.Keys.Single()!=hash && !server.Covers.Values.Single().SequenceEqual(image),"Service sees keyed cover names and encrypted image bytes");
Check((await phone.GetByteArrayAsync("api/covers/"+hash)).SequenceEqual(image),"Paired device verifies and decrypts a cover");
using var crypto=new VaultCrypto(settings);
var write=crypto.EncryptWrite(new(book.Id,0,book,hash));
Check(write.Ciphertext!=crypto.EncryptWrite(new(book.Id,0,book,hash)).Ciphertext,"Repeated encryption uses independent random nonces");
Reject(()=>crypto.DecryptEntry(new(book.Id,2,write.Ciphertext)),"Tampered revision fails authentication");
Reject(()=>crypto.DecryptEntry(new(Guid.NewGuid(),1,write.Ciphertext)),"Swapped edition ID fails authentication");
using var wrongKey=new VaultCrypto(settings with {EncryptionKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))});
Reject(()=>wrongKey.DecryptEntry(new(book.Id,1,write.Ciphertext)),"Wrong device key cannot decrypt a catalog");
var bytes=Convert.FromBase64String(write.Ciphertext);bytes[^1]^=1;
Reject(()=>crypto.DecryptEntry(new(book.Id,1,Convert.ToBase64String(bytes))),"Changed ciphertext fails authentication");
foreach(var url in new[]{"https://library.chatgpt.site/","http://public.example/","https://user:pass@example.com/","https://example.com/?token=x"})
{try{(settings with {ServerUrl=url}).Validate();throw new Exception("Unsafe endpoint accepted");}catch(InvalidDataException){}}
Check(true,"Sites, insecure endpoints and URL credentials are rejected");
Console.WriteLine("13 private sync checks passed.");
static void Check(bool valid,string message){if(!valid)throw new Exception(message);Console.WriteLine("PASS "+message);}
static void Reject(Action action,string message){try{action();throw new Exception(message+" was accepted");}catch(CryptographicException){Console.WriteLine("PASS "+message);}}

sealed class VaultServer:HttpMessageHandler
{
    public Dictionary<Guid,VaultEntry> Entries {get;}=[];
    public Dictionary<string,byte[]> Covers {get;}=[];
    public string LastBody {get;private set;}="";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(request.Headers.Authorization?.Scheme!="Bearer" || !request.Headers.Contains("X-Starfleet-Vault"))return new(HttpStatusCode.Unauthorized);
        var path=request.RequestUri!.AbsolutePath;
        if(path=="/v1/entries")
        {
            if(request.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=JsonContent.Create(Entries.Values.ToArray(),PrivateJson.Context.VaultEntryArray)};
            LastBody=await request.Content!.ReadAsStringAsync(ct);
            var write=await request.Content.ReadFromJsonAsync(PrivateJson.Context.VaultWrite,ct) ?? throw new Exception("Missing write");
            Entries.TryGetValue(write.Id,out var current);
            if((current?.Revision??0)!=write.ExpectedRevision)return new(HttpStatusCode.Conflict){Content=JsonContent.Create(current!,PrivateJson.Context.VaultEntry)};
            var next=new VaultEntry(write.Id,write.ExpectedRevision+1,write.Ciphertext);Entries[write.Id]=next;
            return new(HttpStatusCode.OK){Content=JsonContent.Create(next,PrivateJson.Context.VaultEntry)};
        }
        var key=path.Split('/')[^1];
        if(request.Method==HttpMethod.Put){Covers[key]=await request.Content!.ReadAsByteArrayAsync(ct);return new(HttpStatusCode.NoContent);}
        return new(HttpStatusCode.OK){Content=new ByteArrayContent(Covers[key])};
    }
}
