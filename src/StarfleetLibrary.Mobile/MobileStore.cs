using System.Text.Json;
using StarfleetLibrary.Core;
using StarfleetLibrary.Sync;
using Windows.Storage;

namespace StarfleetLibrary.Mobile;

public sealed class MobileStore
{
    private static string Root => Path.Combine(ApplicationData.Current.LocalFolder.Path,"NativeLibrary");
    private static string StatePath => Path.Combine(Root,"catalog-and-sync.json");
    private DeviceState state = new([], [], null);
    private readonly SemaphoreSlim gate = new(1,1);
    public Book[] Books => state.Books.Select(b => b with { }).ToArray();
    public SyncConflict[] Conflicts { get; private set; } = [];
    public DateTimeOffset? LastSync => state.LastSync;
    public string? LoadError { get; private set; }
    public string? Cover(Guid id) => state.Baseline.FirstOrDefault(x => x.Id == id)?.Cover;

    public MobileStore()
    {
        try
        {
            if (File.Exists(StatePath))
                state = JsonSerializer.Deserialize(File.ReadAllText(StatePath),SyncJson.Context.DeviceState) ?? throw new InvalidDataException("Empty saved catalog.");
            _ = CatalogBackup.Encode(state.Books);
        }
        catch (Exception ex) { LoadError = "The saved catalog could not be opened. It has been preserved. " + ex.Message; }
    }
    private void Persist(DeviceState next)
    {
        if (LoadError is not null) throw new InvalidOperationException(LoadError);
        _ = CatalogBackup.Encode(next.Books);
        Directory.CreateDirectory(Root);
        File.WriteAllText(StatePath+".tmp",JsonSerializer.Serialize(next,SyncJson.Context.DeviceState));
        File.Move(StatePath+".tmp",StatePath,true);
        state = next;
    }
    public async Task SaveAsync(Book book, Book? expected = null)
    {
        await gate.WaitAsync();
        try
        {
            CatalogEditGuard.RequireUnchanged(state.Books, expected);
            Persist(state with { Books = state.Books.Where(b=>b.Id != book.Id).Append(book with { }).ToArray() });
        }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(Guid id, Book? expected = null)
    {
        await gate.WaitAsync();
        try
        {
            CatalogEditGuard.RequireUnchanged(state.Books, expected);
            Persist(state with { Books = state.Books.Where(b=>b.Id != id).ToArray() });
        }
        finally { gate.Release(); }
    }
    public async Task SyncAsync(HttpClient http)
    {
        await gate.WaitAsync();
        try
        {
            var result = await new SyncClient(http).SynchronizeAsync(state.Books,state.Baseline);
            Persist(new(result.Books,result.Baseline,DateTimeOffset.UtcNow));
            Conflicts = result.Conflicts;
            Directory.CreateDirectory(Path.Combine(Root,"covers"));
            foreach(var entry in result.Baseline.Where(x=>x.Book is not null && x.Cover is not null))
            {
                var file=Path.Combine(Root,"covers",entry.Cover+".jpg");
                if(File.Exists(file))continue;
                var bytes=await http.GetByteArrayAsync("api/covers/"+entry.Cover);
                await File.WriteAllBytesAsync(file+".tmp",bytes);File.Move(file+".tmp",file,true);
            }
        }
        finally { gate.Release(); }
    }
    public async Task ResolveAsync(SyncConflict conflict,bool keepLocal)
    {
        await gate.WaitAsync();
        try
        {
            var selected = keepLocal ? conflict.Local : conflict.Remote.Book;
            var books = state.Books.Where(b=>b.Id != conflict.Id);
            Persist(state with { Books = (selected is null ? books : books.Append(selected)).ToArray(), Baseline = state.Baseline.Where(x=>x.Id != conflict.Id).Append(conflict.Remote).ToArray() });
            Conflicts = Conflicts.Where(c=>c.Id != conflict.Id).ToArray();
        }
        finally { gate.Release(); }
    }
    public string? CoverFile(Guid id)
    {
        if(Cover(id) is not { } hash)return null;
        var file=Path.Combine(Root,"covers",hash+".jpg");
        return File.Exists(file) ? new Uri(file).AbsoluteUri : null;
    }
}
