using System.Net;
using System.Text.Json;
using StarfleetLibrary.Core;
using StarfleetLibrary.Sync;

var server=new FakeServer();using var http=new HttpClient(server){BaseAddress=new Uri("https://sync.test/")};var client=new SyncClient(http);
var book=new Book{Title="Vendetta",Format=BookFormat.Paperback};
var first=await client.SynchronizeAsync([book],[]);Check(first.Baseline.Single().Revision==1,"First sync uploads an edition");
var phone=await client.SynchronizeAsync([],[]);Check(phone.Books.Single()==book,"New phone downloads the catalog");
var other=book with { Id=Guid.NewGuid(),Format=BookFormat.Ebook };
var pc=await client.SynchronizeAsync([book,other],first.Baseline);Check(pc.Books.Length==2,"Different formats retain independent IDs");
var phoneEdit=book with { Ownership=OwnershipStatus.Wishlist };
phone=await client.SynchronizeAsync([phoneEdit],phone.Baseline);Check(phone.Conflicts.Length==0 && phone.Books.Length==2,"Offline edit merges with an unrelated desktop addition");
pc=await client.SynchronizeAsync(pc.Books,pc.Baseline);Check(pc.Books.Single(b=>b.Id==book.Id).Ownership==OwnershipStatus.Wishlist,"Desktop receives the wishlist move");
var pcEdit=phoneEdit with {Notes="PC note"};var mobileEdit=phoneEdit with {Notes="Phone note"};
pc=await client.SynchronizeAsync([pcEdit,other],pc.Baseline);
phone=await client.SynchronizeAsync([mobileEdit,other],phone.Baseline);Check(phone.Conflicts.Length==1 && phone.Books.Single(b=>b.Id==book.Id).Notes=="Phone note","Concurrent edits preserve the phone version for review");
Check((await client.GetAsync()).Single(b=>b.Id==book.Id).Book!.Notes=="PC note","Conflicts never overwrite the server version");
var resolvedBaseline=phone.Baseline.Where(b=>b.Id!=book.Id).Append(phone.Conflicts[0].Remote).ToArray();
phone=await client.SynchronizeAsync(phone.Books,resolvedBaseline);Check(phone.Conflicts.Length==0 && (await client.GetAsync()).Single(b=>b.Id==book.Id).Book!.Notes=="Phone note","Explicit local choice resolves a conflict");
pc=await client.SynchronizeAsync(pc.Books,pc.Baseline);pc=await client.SynchronizeAsync([other],pc.Baseline);
phone=await client.SynchronizeAsync(phone.Books,phone.Baseline);Check(phone.Books.Length==1 && phone.Baseline.Any(b=>b.Id==book.Id && b.Book is null),"Deletion tombstones reach offline devices");
var lostAck=await client.SynchronizeAsync([other with { Notes="New note" }],phone.Baseline);
var retry=await client.SynchronizeAsync(lostAck.Books,phone.Baseline);Check(retry.Conflicts.Length==0 && retry.Baseline.Single(b=>b.Id==other.Id).Revision==lostAck.Baseline.Single(b=>b.Id==other.Id).Revision,"Retry after lost acknowledgement is idempotent");
server.Entries.Clear();try{await client.SynchronizeAsync(retry.Books,retry.Baseline);throw new Exception("Expected reset detection");}catch(InvalidDataException){Console.WriteLine("PASS server reset does not erase local books");}
Console.WriteLine("11 sync checks passed.");
static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}

sealed class FakeServer:HttpMessageHandler
{
    public Dictionary<Guid,SyncEntry> Entries {get;}=[];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(request.Method==HttpMethod.Get)return Reply(Entries.Values.ToArray(),SyncJson.Context.SyncEntryArray);
        var write=JsonSerializer.Deserialize(await request.Content!.ReadAsStringAsync(ct),SyncJson.Context.SyncWrite)!;
        Entries.TryGetValue(write.Id,out var current);
        if((current?.Revision??0)!=write.ExpectedRevision)return Reply(current!,SyncJson.Context.SyncEntry,HttpStatusCode.Conflict);
        var next=new SyncEntry(write.Id,write.ExpectedRevision+1,write.Book,write.Cover);Entries[write.Id]=next;return Reply(next,SyncJson.Context.SyncEntry);
    }
    static HttpResponseMessage Reply<T>(T value,System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(JsonSerializer.Serialize(value,type),System.Text.Encoding.UTF8,"application/json")};
}
