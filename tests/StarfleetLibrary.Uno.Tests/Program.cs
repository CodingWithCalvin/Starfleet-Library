using StarfleetLibrary.Core;
using StarfleetLibrary.Uno.Services;

var tests = new (string Name, Func<string, Task> Run)[]
{
    ("First launch copies metadata and artwork without modifying MAUI", async root =>
    {
        var source = Path.Combine(root, "maui");
        var sourceFile = Path.Combine(source, "Data", "StarfleetLibrary", "catalog.json");
        var book = new Book { Title = "SNW comic", Kind = BookKind.Comic, Series = TrekSeries.StrangeNewWorlds, Format = BookFormat.Ebook, ReadingStatus = ReadingStatus.Finished, FinishedAt = DateTimeOffset.Now, Notes = "Import provenance", Rating = 4 };
        var repo = new CatalogRepository(sourceFile);
        await repo.SaveAsync([book]);
        var before = await File.ReadAllBytesAsync(sourceFile);
        var sourceCovers = Path.Combine(source, "Data", "StarfleetLibrary", "covers");
        Directory.CreateDirectory(sourceCovers);
        await File.WriteAllBytesAsync(Path.Combine(sourceCovers, $"{book.Id:N}.jpg"), [255, 216, 255, 224]);
        var store = new UnoLibraryStore(Path.Combine(root, "uno"));
        await store.InitializeAsync(source);
        Ensure(!store.IsReadOnly && store.Books.Single() == book, "Metadata changed during copy");
        Ensure(File.Exists(Path.Combine(store.CoverDirectory, $"{book.Id:N}.jpg")), "Imported cover missing");
        await store.SaveAsync(book with { Title = "Changed only in Uno" });
        var sourceAfter = await File.ReadAllBytesAsync(sourceFile);
        Ensure(before.SequenceEqual(sourceAfter), "Source catalog changed");
        var restart = new UnoLibraryStore(store.Root);
        await restart.InitializeAsync(source);
        Ensure(restart.Books.Single().Title == "Changed only in Uno", "Restart imported over an existing catalog");
        await restart.ReplaceAsync([]);
        var emptyRestart = new UnoLibraryStore(store.Root);
        await emptyRestart.InitializeAsync(source);
        Ensure(emptyRestart.Books.Count == 0, "An intentionally empty catalog was reseeded");
    }),
    ("Corrupt catalogs remain untouched and read only", async root =>
    {
        var store = new UnoLibraryStore(root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.CatalogPath)!);
        await File.WriteAllTextAsync(store.CatalogPath, "broken catalog");
        await store.InitializeAsync(Path.Combine(root, "no-source"));
        Ensure(store.IsReadOnly, "Corrupt catalog was writable");
        try { await store.SaveAsync(new Book { Title = "Must not save" }); throw new Exception("Save accepted"); }
        catch (InvalidOperationException) { }
        Ensure(await File.ReadAllTextAsync(store.CatalogPath) == "broken catalog", "Corrupt original overwritten");
    }),
    ("Failed imports do not create an empty destination", async root =>
    {
        var source = Path.Combine(root, "source");
        var sourceFile = Path.Combine(source, "Data", "StarfleetLibrary", "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "not json");
        var store = new UnoLibraryStore(Path.Combine(root, "uno"));
        await store.InitializeAsync(source);
        Ensure(store.IsReadOnly && !File.Exists(store.CatalogPath), "Bad source created a writable empty destination");
    }),
    ("Concurrent changes, defensive copies and restore backups preserve data", async root =>
    {
        var store = new UnoLibraryStore(root);
        await store.InitializeAsync(Path.Combine(root, "no-source"));
        await Task.WhenAll(Enumerable.Range(1, 12).Select(i => store.SaveAsync(new Book { Title = $"Edition {i}" })));
        Ensure(store.Books.Count == 12, "Concurrent additions were lost");
        store.Books[0].Title = "Mutation outside store";
        Ensure(store.Books.All(b => b.Title != "Mutation outside store"), "External mutation escaped snapshot");
        var before = await File.ReadAllBytesAsync(store.CatalogPath);
        try { await store.ReplaceAsync([new Book { Title = "" }]); throw new Exception("Invalid restore accepted"); }
        catch (InvalidDataException) { }
        var afterRejectedRestore = await File.ReadAllBytesAsync(store.CatalogPath);
        Ensure(before.SequenceEqual(afterRejectedRestore), "Failed restore changed catalog");
        await store.ReplaceAsync([new Book { Title = "Restored edition" }]);
        var backup = Directory.GetFiles(Path.Combine(root, "Backups")).Single();
        Ensure((await new CatalogRepository(backup).LoadAsync()).Count == 12, "Restore backup missing prior entries");
        Ensure(store.Books.Single().Title == "Restored edition", "Restore failed");
        await store.DeleteAsync(store.Books.Single().Id);
        Ensure((await new CatalogRepository(store.CatalogPath).LoadAsync()).Count == 0, "Delete did not persist");
    })
};
var failures = 0;
foreach (var (name, run) in tests)
{
    var root = Path.Combine(Path.GetTempPath(), "StarfleetUnoTests", Guid.NewGuid().ToString("N"));
    try { Directory.CreateDirectory(root); await run(root); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
    finally
    {
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "StarfleetUnoTests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup path");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} Uno storage checks passed.");
return failures == 0 ? 0 : 1;
static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
