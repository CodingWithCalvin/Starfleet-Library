using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StarfleetLibrary.Core;

// No test packages are needed: `dotnet run --project tests/StarfleetLibrary.Core.Tests`.
// This suite returns a failing process exit code when any assertion fails.
var tests = new (string Name, Func<Task> Run)[]
{
    ("ISBN checksums and formatting", () => Run(IsbnChecksums)),
    ("Malformed ISBNs never validate", () => Run(InvalidIsbns)),
    ("Equivalent ISBN-10 and ISBN-13 keys", () => Run(IsbnCanonicalization)),
    ("Book validation and progress boundaries", () => Run(BookValidation)),
    ("Backup preserves edition and reading metadata", () => Run(BackupRoundTrip)),
    ("Legacy Swift version-1 backup imports", () => Run(LegacySwiftImport)),
    ("Invalid and unsupported backups are rejected", () => Run(InvalidBackups)),
    ("Exact matches include owned and wished equivalent ISBNs", () => Run(ExactMatching)),
    ("Other-edition hints are conservative", () => Run(OtherEditionHints)),
    ("Missing repository starts empty", MissingRepository),
    ("Atomic persistence round-trips full catalog", PersistenceRoundTrip),
    ("Rejected writes preserve the active catalog", RejectedSavePreservesCatalog),
    ("Canceled writes preserve the active catalog", CanceledSavePreservesCatalog),
    ("Corrupt and unreadable stores are not treated as empty", CorruptRepository),
    ("Opt-in samples validate and remain separate editions", () => Run(SampleData)),
    ("Cover URLs prefer saved images and fall back to valid ISBNs", () => Run(CoverChecks.Sources)),
    ("Cover downloads coalesce and survive offline through disk cache", CoverChecks.Cache),
    ("Missing, invalid, oversized and offline covers fail gracefully", CoverChecks.Failures),
    ("Imported covers work offline and custom URLs override them", CoverChecks.Imported),
    ("Comic type and SNW survive backups while legacy entries default to books", () => Run(BookClassification))
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;

static Task Run(Action assertion)
{
    assertion();
    return Task.CompletedTask;
}

static void IsbnChecksums()
{
    Check.True(IsbnUtility.IsValid("0-306-40615-2"));
    Check.True(IsbnUtility.IsValid("978-0-306-40615-7"));
    Check.True(IsbnUtility.IsValid("0-8044-2957-X"));
    Check.True(IsbnUtility.IsValid("9791090636071"));
    Check.Equal("080442957X", IsbnUtility.Normalize(" 0-8044-2957-x\n"));
}

static void InvalidIsbns()
{
    foreach (var value in new[]
    {
        "", "9780306406158", "0306406153", "X804429570", "978O306406157",
        "1234567890128", "0000000000000", "９７８０３０６４０６１５７", "٠٨٠٤٤٢٩٥٧X",
        "9780306406157\u200B"
    })
    {
        Check.False(IsbnUtility.IsValid(value), $"Should reject {value}");
        Check.Equal<string?>(null, IsbnUtility.Canonical(value));
    }
}

static void IsbnCanonicalization()
{
    Check.Equal("9780306406157", IsbnUtility.Canonical("0-306-40615-2"));
    Check.Equal("9780306406157", IsbnUtility.Canonical(" 978-0-306-40615-7 "));
    Check.Equal("9780804429573", IsbnUtility.Canonical("080442957x"));
    Check.Equal("9791090636071", IsbnUtility.Canonical("9791090636071"));
    Check.False(IsbnUtility.Canonical("9791090636071") == IsbnUtility.Canonical("9781090636072"));
}

static void BookValidation()
{
    var book = CompleteBook();
    Check.Equal<string?>(null, book.ValidationError);
    Check.Equal(0.25, book.Progress);
    Check.NotNull((book with { Title = " \n" }).ValidationError);
    Check.NotNull((book with { PageCount = -1 }).ValidationError);
    Check.NotNull((book with { CurrentPage = -1 }).ValidationError);
    Check.NotNull((book with { CurrentPage = 201 }).ValidationError);
    Check.NotNull((book with { Rating = 6 }).ValidationError);
    Check.NotNull((book with { Rating = -1 }).ValidationError);
    Check.NotNull((book with { Isbn = "9780306406158" }).ValidationError);
    Check.NotNull((book with { PublicationYear = "19x4" }).ValidationError);
    Check.Equal<string?>(null, (book with { Isbn = "", PublicationYear = "", PageCount = 0 }).ValidationError);
    Check.Equal(0.0, (book with { CurrentPage = -1 }).Progress);
    Check.Equal(1.0, (book with { CurrentPage = 300 }).Progress);
    Check.Equal(0.0, (book with { PageCount = 0 }).Progress);
    Check.Equal(1.0, (book with { PageCount = 0, ReadingStatus = ReadingStatus.Finished }).Progress);
}

static void BackupRoundTrip()
{
    var book = CompleteBook();
    var data = CatalogBackup.Encode(new[] { book });
    var loaded = CatalogBackup.Decode(data);
    Check.Equal(1, loaded.Count);
    Check.Equal(book, loaded[0]);
    using var json = JsonDocument.Parse(data);
    Check.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
    Check.False(json.RootElement.GetProperty("books")[0].TryGetProperty("progress", out _));
    Check.False(json.RootElement.GetProperty("books")[0].TryGetProperty("validationError", out _));
    Check.Equal(0, CatalogBackup.Decode(CatalogBackup.Encode(Array.Empty<Book>())).Count);
}

static void LegacySwiftImport()
{
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "swift-v1.json");
    var books = CatalogBackup.Decode(File.ReadAllBytes(path));
    Check.Equal(2, books.Count);
    var book = books[0];
    Check.Equal(Guid.Parse("B62B2FD4-70FB-4C5F-8DF7-167C70FC5C42"), book.Id);
    Check.Equal("Legacy Test Edition", book.Title);
    Check.Equal("Example Author", book.Authors);
    Check.Equal(TrekSeries.DeepSpaceNine, book.Series);
    Check.Equal("12", book.BookNumber);
    Check.Equal("0-306-40615-2", book.Isbn);
    Check.Equal("Example Publisher", book.Publisher);
    Check.Equal("2000", book.PublicationYear);
    Check.Equal("Second printing", book.Edition);
    Check.Equal(BookFormat.Hardcover, book.Format);
    Check.Equal(OwnershipStatus.Owned, book.Ownership);
    Check.Equal(ReadingStatus.Finished, book.ReadingStatus);
    Check.Equal(256, book.PageCount);
    Check.Equal(256, book.CurrentPage);
    Check.Equal(4, book.Rating);
    Check.Equal("Synthetic migration fixture: café, Q’s notes.\nSecond line.", book.Notes);
    Check.Equal(DateTimeOffset.Parse("2026-01-02T03:04:05Z"), book.AddedAt);
    Check.Equal<DateTimeOffset?>(DateTimeOffset.Parse("2026-06-07T08:09:10Z"), book.FinishedAt);
    Check.Equal("https://example.invalid/legacy-cover.jpg", book.CoverUrl);
    Check.Equal(BookFormat.Ebook, books[1].Format);
    Check.Equal(OwnershipStatus.Wishlist, books[1].Ownership);
    Check.Equal(TrekSeries.NextGeneration, books[1].Series);
    Check.Equal<DateTimeOffset?>(null, books[1].FinishedAt);
    Check.Equal<string?>(null, books[1].CoverUrl);
    Check.Equal(books.ToArray(), CatalogBackup.Decode(CatalogBackup.Encode(books)).ToArray(), sequence: true);
}

static void InvalidBackups()
{
    var book = CompleteBook();
    Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes("not json")));
    Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes("null")));
    Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes("{}")));
    Check.Throws(() => CatalogBackup.Encode(new[] { book, book with { } }));
    Check.Throws(() => CatalogBackup.Encode(new[] { book with { Title = "" } }));

    var valid = JsonNode.Parse(CatalogBackup.Encode(new[] { book }))!.AsObject();
    var version = valid.DeepClone().AsObject();
    version["schemaVersion"] = 99;
    Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes(version.ToJsonString())));
    var duplicate = valid.DeepClone().AsObject();
    var list = duplicate["books"]!.AsArray();
    list.Add(list[0]!.DeepClone());
    Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes(duplicate.ToJsonString())));
    foreach (var change in new Action<JsonObject>[]
    {
        b => b["title"] = "",
        b => b["currentPage"] = 999,
        b => b["rating"] = 6,
        b => b["isbn"] = "9780306406158",
        b => b["readingStatus"] = "inventedStatus",
        b => b["format"] = "inventedFormat"
    })
    {
        var broken = valid.DeepClone().AsObject();
        change(broken["books"]![0]!.AsObject());
        Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes(broken.ToJsonString())));
    }
    // Truncated but syntactically valid JSON must not silently erase tracking data.
    foreach (var schemaVersion in new[] { 1, 2 })
    {
        foreach (var property in new[] { "notes", "currentPage", "format", "ownership", "authors", "series", "bookNumber", "isbn", "publisher", "publicationYear", "edition", "readingStatus", "pageCount", "rating" })
        {
            var truncated = valid.DeepClone().AsObject();
            truncated["schemaVersion"] = schemaVersion;
            Check.True(truncated["books"]![0]!.AsObject().Remove(property));
            Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes(truncated.ToJsonString())));
        }
    }
}

static void ExactMatching()
{
    var owned = CompleteBook();
    owned.Isbn = "0306406152";
    var wish = owned with { Id = Guid.NewGuid(), Isbn = "9780306406157", Ownership = OwnershipStatus.Wishlist };
    var other = owned with { Id = Guid.NewGuid(), Isbn = "9780804429573" };
    Check.Equal(new[] { owned.Id, wish.Id }, CatalogMatcher.ExactMatches("978-0-306-40615-7", new[] { owned, wish, other }).Select(b => b.Id).ToArray(), sequence: true);
    var invalid = owned with { Id = Guid.NewGuid(), Isbn = "bad" };
    var blank = owned with { Id = Guid.NewGuid(), Isbn = "" };
    Check.Equal(0, CatalogMatcher.ExactMatches("", new[] { invalid, blank }).Count);
    Check.Equal(0, CatalogMatcher.ExactMatches("bad", new[] { invalid, blank }).Count);
}

static void OtherEditionHints()
{
    var candidate = CompleteBook() with { Title = "Q-SQUARED", Authors = "Péter David" };
    var ebook = candidate with { Id = Guid.NewGuid(), Title = "Q squared", Authors = "Peter   David", Isbn = "9780804429573", Format = BookFormat.Ebook };
    var noIsbn = ebook with { Id = Guid.NewGuid(), Isbn = "", Format = BookFormat.Paperback };
    var exact = candidate with { Id = Guid.NewGuid(), Isbn = "0306406152" };
    var differentAuthor = ebook with { Id = Guid.NewGuid(), Authors = "Another Author" };
    var differentTitle = ebook with { Id = Guid.NewGuid(), Title = "Q Squared: Part Two" };
    var missingAuthor = ebook with { Id = Guid.NewGuid(), Authors = "" };
    var matches = CatalogMatcher.PossibleOtherEditions(candidate, new[] { candidate, ebook, noIsbn, exact, differentAuthor, differentTitle, missingAuthor });
    Check.Equal(new[] { ebook.Id, noIsbn.Id }, matches.Select(b => b.Id).ToArray(), sequence: true);
    Check.Equal(0, CatalogMatcher.PossibleOtherEditions(candidate with { Authors = "" }, new[] { ebook }).Count);
    Check.Equal(0, CatalogMatcher.PossibleOtherEditions(candidate with { Title = "" }, new[] { ebook }).Count);
}

static async Task MissingRepository()
{
    await WithDirectory(async directory =>
    {
        var path = Path.Combine(directory, "not-created", "catalog.json");
        Check.Equal(0, (await new CatalogRepository(path).LoadAsync()).Count);
    });
}

static async Task PersistenceRoundTrip()
{
    await WithDirectory(async directory =>
    {
        var repository = new CatalogRepository(Path.Combine(directory, "nested", "catalog.json"));
        var first = CompleteBook();
        var second = first with { Id = Guid.NewGuid(), Format = BookFormat.Ebook, Ownership = OwnershipStatus.Wishlist, Notes = "A separate edition" };
        await repository.SaveAsync(new[] { first, second });
        Check.Equal(new[] { first, second }, (await repository.LoadAsync()).ToArray(), sequence: true);
        await repository.SaveAsync(new[] { second });
        Check.Equal(new[] { second }, (await repository.LoadAsync()).ToArray(), sequence: true);
    });
}

static async Task RejectedSavePreservesCatalog()
{
    await WithDirectory(async directory =>
    {
        var path = Path.Combine(directory, "catalog.json");
        var repository = new CatalogRepository(path);
        var original = CompleteBook();
        await repository.SaveAsync(new[] { original });
        var bytes = await File.ReadAllBytesAsync(path);
        await Check.ThrowsAsync(() => repository.SaveAsync(new[] { original with { Title = "" } }));
        Check.Equal(bytes, await File.ReadAllBytesAsync(path), sequence: true);
        Check.Equal(original, (await repository.LoadAsync())[0]);
    });
}

static async Task CanceledSavePreservesCatalog()
{
    await WithDirectory(async directory =>
    {
        var path = Path.Combine(directory, "catalog.json");
        var repository = new CatalogRepository(path);
        var original = CompleteBook();
        await repository.SaveAsync(new[] { original });
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Check.ThrowsAsync(() => repository.SaveAsync(new[] { original with { Notes = "Do not persist" } }, cancellation.Token));
        Check.Equal(bytes, await File.ReadAllBytesAsync(path), sequence: true);
    });
}

static async Task CorruptRepository()
{
    await WithDirectory(async directory =>
    {
        var path = Path.Combine(directory, "catalog.json");
        var corrupt = Encoding.UTF8.GetBytes("{truncated");
        await File.WriteAllBytesAsync(path, corrupt);
        await Check.ThrowsAsync(() => new CatalogRepository(path).LoadAsync());
        Check.Equal(corrupt, await File.ReadAllBytesAsync(path), sequence: true);
        // A directory at the catalog path is an I/O failure, not a missing catalog.
        var directoryPath = Path.Combine(directory, "catalog-is-directory");
        Directory.CreateDirectory(directoryPath);
        await Check.ThrowsAsync(() => new CatalogRepository(directoryPath).LoadAsync());
        // A non-directory ancestor must not be silently treated as first-launch storage.
        var parentFile = Path.Combine(directory, "parent-is-file");
        await File.WriteAllTextAsync(parentFile, "existing file");
        await Check.ThrowsAsync(() => new CatalogRepository(Path.Combine(parentFile, "catalog.json")).LoadAsync());
    });
}

static void SampleData()
{
    Check.Equal(8, SampleCatalog.Books.Count);
    foreach (var book in SampleCatalog.Books) Check.Equal<string?>(null, book.ValidationError);
    Check.Equal(8, CatalogBackup.Decode(CatalogBackup.Encode(SampleCatalog.Books)).Count);
    Check.Equal(8, SampleCatalog.Books.Select(b => b.Id).Distinct().Count());
}

static void BookClassification()
{
    var comic = CompleteBook() with { Kind = BookKind.Comic, Series = TrekSeries.StrangeNewWorlds, Format = BookFormat.Ebook };
    Check.Equal(comic, CatalogBackup.Decode(CatalogBackup.Encode(new[] { comic }))[0]);
    Check.Equal("SNW", comic.Series.ShortName());
    Check.Equal("COMIC · SNW", comic.CatalogLabel());
    var legacy = JsonNode.Parse(CatalogBackup.Encode(new[] { CompleteBook() }))!.AsObject();
    Check.True(legacy["books"]![0]!.AsObject().Remove("kind"));
    Check.Equal(BookKind.Book, CatalogBackup.Decode(Encoding.UTF8.GetBytes(legacy.ToJsonString()))[0].Kind);
    legacy["books"]![0]!["kind"] = "unknownKind";
    Check.Throws(() => CatalogBackup.Decode(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
    Check.NotNull((comic with { Kind = (BookKind)999 }).ValidationError);
}

static Book CompleteBook() => new()
{
    Id = Guid.Parse("cf33324d-55ab-44be-aee2-56d2d9814967"),
    Title = "Synthetic test edition", Authors = "Example Author", Series = TrekSeries.OriginalSeries,
    BookNumber = "2", Isbn = "9780306406157", Publisher = "Example Publisher", PublicationYear = "1988",
    Edition = "First printing", Format = BookFormat.Hardcover, Ownership = OwnershipStatus.Owned,
    ReadingStatus = ReadingStatus.Reading, PageCount = 200, CurrentPage = 50, Rating = 4,
    Notes = "Signed copy — test only\nSecond line.", AddedAt = DateTimeOffset.Parse("2026-01-02T03:04:05.1234567Z"),
    FinishedAt = null, CoverUrl = "https://example.invalid/cover.jpg"
};

static async Task WithDirectory(Func<string, Task> action)
{
    var directory = Path.Combine(Path.GetTempPath(), "starfleet-tests-" + Guid.NewGuid());
    Directory.CreateDirectory(directory);
    try { await action(directory); }
    finally { Directory.Delete(directory, recursive: true); }
}

static class Check
{
    public static void True(bool value, string? message = null)
    {
        if (!value) throw new InvalidOperationException(message ?? "Expected true.");
    }
    public static void False(bool value, string? message = null) => True(!value, message ?? "Expected false.");
    public static void NotNull(object? value) => True(value is not null, "Expected a validation error.");
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
    public static void Equal<T>(T[] expected, T[] actual, bool sequence)
    {
        if (!sequence) throw new ArgumentException("Sequence equality must be explicit.", nameof(sequence));
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Sequences differ. Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }
    public static void Throws(Action action)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException("Expected an exception.");
    }
    public static async Task ThrowsAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException("Expected an exception.");
    }
}
