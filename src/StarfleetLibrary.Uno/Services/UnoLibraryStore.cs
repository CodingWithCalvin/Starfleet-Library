using System.Text.Json;
using StarfleetLibrary.Core;
using StarfleetLibrary.Sync;
using System.Security.Cryptography;

namespace StarfleetLibrary.Uno.Services;

/// <summary>A separate catalog. The MAUI library is read only, and is copied only on first use.</summary>
public sealed class UnoLibraryStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CatalogRepository repository;
    private IReadOnlyList<Book> books = [];
    public string Root { get; }
    public string CatalogPath => repository.FilePath;
    public string CoverDirectory => Path.Combine(Root, "Data", "covers");
    public string CacheDirectory => Path.Combine(Root, "Cache", "covers");
    public IReadOnlyList<Book> Books => books.Select(b => b with { }).ToArray();
    public bool IsReadOnly { get; private set; }
    public string? LoadError { get; private set; }
    public string? ImportMessage { get; private set; }
    public LibraryPreferences Preferences { get; private set; } = new();
    public event Action? Changed;
    public SyncConflict[] SyncConflicts { get; private set; } = [];
    public DateTimeOffset? LastSync { get; private set; }
    private string SyncStatePath => Path.Combine(Root,"sync-baseline.json");
    public static string DefaultRoot => Environment.GetEnvironmentVariable("STARFLEET_UNO_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starfleet Library Uno");

    public UnoLibraryStore(string? root = null)
    {
        Root = Path.GetFullPath(root ?? DefaultRoot);
        repository = new CatalogRepository(Path.Combine(Root, "Data", "catalog.json"));
    }

    public async Task InitializeAsync(string? sourceRoot = null)
    {
        await gate.WaitAsync();
        try
        {
            var source = sourceRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Starfleet Library", "com.example.starfleetlibrary");
            var sourceCatalog = Path.Combine(source, "Data", "StarfleetLibrary", "catalog.json");
            if (!File.Exists(CatalogPath) && File.Exists(sourceCatalog))
            {
                var imported = await new CatalogRepository(sourceCatalog).LoadAsync();
                CopyImages(Path.Combine(source, "Data", "StarfleetLibrary", "covers"), CoverDirectory);
                CopyImages(Path.Combine(source, "Cache", "covers"), CacheDirectory);
                await repository.SaveAsync(imported);
                ImportMessage = $"Copied {imported.Count} titles and their covers from your MAUI library. This library is now independent.";
            }
            books = await repository.LoadAsync();
            if (!File.Exists(CatalogPath)) await repository.SaveAsync(books);
            var preferencesPath = Path.Combine(Root, "preferences.json");
            if (File.Exists(preferencesPath))
            {
                try { Preferences = JsonSerializer.Deserialize<LibraryPreferences>(await File.ReadAllTextAsync(preferencesPath)) ?? new(); }
                catch (JsonException) { Preferences = new(); }
            }
            Preferences.Layout = Math.Clamp(Preferences.Layout, 0, 2);
            Preferences.Type = Math.Clamp(Preferences.Type, 0, 2);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            IsReadOnly = true;
            LoadError = $"The library could not be loaded. Your files have been preserved. {ex.Message}";
        }
        finally { gate.Release(); }
    }

    private static void CopyImages(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            if (Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".png" or ".gif")
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    public Task SaveAsync(Book book) => MutateAsync(current => current.Where(b => b.Id != book.Id).Append(book with { }).ToArray());
    public Task DeleteAsync(Guid id) => MutateAsync(current => current.Where(b => b.Id != id).ToArray());
    public Task ReplaceAsync(IReadOnlyList<Book> replacement) => MutateAsync(_ => replacement.Select(b => b with { }).ToArray(), backup: true);

    private async Task MutateAsync(Func<IReadOnlyList<Book>, IReadOnlyList<Book>> change, bool backup = false)
    {
        await gate.WaitAsync();
        try
        {
            if (IsReadOnly) throw new InvalidOperationException("The library is read only because it could not be loaded. Repair the saved file and reopen the app.");
            var next = change(books);
            _ = CatalogBackup.Encode(next);
            if (backup) await new CatalogRepository(Path.Combine(Root, "Backups", $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json")).SaveAsync(books);
            await repository.SaveAsync(next);
            books = next;
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    public async Task SavePreferencesAsync()
    {
        Directory.CreateDirectory(Root);
        var file = Path.Combine(Root, "preferences.json");
        var staged = file + ".tmp";
        await File.WriteAllTextAsync(staged, JsonSerializer.Serialize(Preferences));
        File.Move(staged, file, overwrite: true);
    }

    public async Task SynchronizeAsync(HttpClient http, CoverImageCache covers)
    {
        await gate.WaitAsync();
        try
        {
            if (IsReadOnly) throw new InvalidOperationException(LoadError);
            var baseline = File.Exists(SyncStatePath) ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(SyncStatePath),SyncJson.Context.SyncEntryArray)
                ?? throw new InvalidDataException("Invalid sync history.") : [];
            if (!File.Exists(SyncStatePath))
                await new CatalogRepository(Path.Combine(Root,"Backups",$"before-cloud-sync-{DateTime.Now:yyyyMMdd-HHmmss}.json")).SaveAsync(books);
            var hashes = new Dictionary<Guid,string>();
            var uploaded = baseline.Where(x=>x.Cover is not null).Select(x=>x.Cover!).ToHashSet();
            var client = new SyncClient(http);
            var uploadGate = new SemaphoreSlim(4,4);
            var uploads = new List<Task>();
            foreach (var book in books)
            {
                var path = await covers.GetAsync(book);
                if (path is null) continue;
                var bytes = await File.ReadAllBytesAsync(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                hashes[book.Id] = hash;
                if (!uploaded.Add(hash)) continue;
                uploads.Add(Upload(hash,bytes));
            }
            await Task.WhenAll(uploads);
            var result = await client.SynchronizeAsync(books,baseline,hashes);
            // Catalog first: after an interrupted commit the older baseline can
            // produce a reviewable conflict, never silently discard an edit.
            await repository.SaveAsync(result.Books);
            books = result.Books;
            var staged = SyncStatePath + ".tmp";
            await File.WriteAllTextAsync(staged,JsonSerializer.Serialize(result.Baseline,SyncJson.Context.SyncEntryArray));
            File.Move(staged,SyncStatePath,true);
            SyncConflicts = result.Conflicts;
            LastSync = DateTimeOffset.UtcNow;
            foreach (var row in result.Baseline.Where(x=>x.Book is not null && x.Cover is not null))
            {
                if (hashes.GetValueOrDefault(row.Id) == row.Cover) continue;
                var bytes = await http.GetByteArrayAsync("api/covers/"+row.Cover);
                if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != row.Cover) throw new InvalidDataException("Cover checksum mismatch.");
                Directory.CreateDirectory(CoverDirectory);
                var file = Path.Combine(CoverDirectory,row.Id.ToString("N")+".jpg");
                await File.WriteAllBytesAsync(file+".tmp",bytes);File.Move(file+".tmp",file,true);
            }
            async Task Upload(string hash,byte[] bytes)
            {
                await uploadGate.WaitAsync();
                try { await client.UploadCoverAsync(hash,bytes); }
                finally { uploadGate.Release(); }
            }
        }
        finally { gate.Release(); }
    }

    public async Task ResolveConflictAsync(SyncConflict conflict,bool keepLocal)
    {
        await gate.WaitAsync();
        try
        {
            if (IsReadOnly) throw new InvalidOperationException(LoadError);
            var baseline = JsonSerializer.Deserialize(await File.ReadAllTextAsync(SyncStatePath),SyncJson.Context.SyncEntryArray)!;
            var book = keepLocal ? conflict.Local : conflict.Remote.Book;
            var next = books.Where(b=>b.Id!=conflict.Id);
            var resolved = (book is null ? next : next.Append(book)).ToArray();
            await repository.SaveAsync(resolved);
            books = resolved;
            await File.WriteAllTextAsync(SyncStatePath+".tmp",JsonSerializer.Serialize(baseline.Where(x=>x.Id!=conflict.Id).Append(conflict.Remote).ToArray(),SyncJson.Context.SyncEntryArray));
            File.Move(SyncStatePath+".tmp",SyncStatePath,true);
            SyncConflicts = SyncConflicts.Where(x=>x.Id!=conflict.Id).ToArray();
        }
        finally { gate.Release(); }
    }
}

public sealed class LibraryPreferences
{
    public int Layout { get; set; } = 2;
    public int Type { get; set; } = 1;
}
