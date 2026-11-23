using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StarfleetLibrary.Core;

public sealed record StoryArcPart(int Number, string Title, string[] Isbns, string[] Aliases);
public sealed record StoryArc(string Id, string Name, TrekSeries[] Series, StoryArcPart[] Parts, string[] OmnibusTitles, string[] OmnibusIsbns, string[] Sources);
public sealed record StoryArcSlot(StoryArcPart Part, Book[] Owned, Book[] Wishlist, Book[] OtherFormats, bool ViaOmnibus)
{
    public bool Missing => Owned.Length == 0;
    public string Status => Missing ? Wishlist.Length > 0 ? "On wishlist" : "Missing" : ViaOmnibus ? "Owned via omnibus" : "Owned";
}
public sealed record StoryArcReport(StoryArc Arc, StoryArcSlot[] Slots, Book[] Unmatched)
{
    public int OwnedCount => Slots.Count(slot => !slot.Missing);
    public string MissingTitles => string.Join("\n", Slots.Where(slot => slot.Missing).Select(slot => $"Part {slot.Part.Number}: {slot.Part.Title}"));
}

public static class StoryArcs
{
    private static readonly Lazy<StoryArc[]> reference = new(() =>
    {
        using var stream = typeof(StoryArcs).Assembly.GetManifestResourceStream("StarfleetLibrary.StoryArcs.json")!;
        return JsonSerializer.Deserialize(stream, StoryArcJson.Default.StoryArcArray)!;
    });
    public static IReadOnlyList<StoryArc> Reference => reference.Value;
    private static readonly string[] words = ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
    private static readonly string[] roman = ["", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix"];
    private static readonly Lazy<Dictionary<string, (string Arc, int Part)>> isbnIndex = new(() => Reference
        .SelectMany(arc => arc.Parts.SelectMany(part => part.Isbns.Select(isbn => (Isbn: IsbnUtility.Canonical(isbn)!, Arc: arc.Id, Part: part.Number))))
        .ToDictionary(entry => entry.Isbn, entry => (entry.Arc, entry.Part)));
    private static readonly Lazy<Dictionary<(string Arc, int Part), HashSet<string>>> titleIndex = new(() => Reference
        .SelectMany(arc => arc.Parts.Select(part => (arc, part))).ToDictionary(pair => (pair.arc.Id, pair.part.Number), pair => CandidateTitles(pair.arc, pair.part).Select(Key).ToHashSet(StringComparer.Ordinal)));
    private static IEnumerable<string> CandidateTitles(StoryArc arc, StoryArcPart part)
    {
        yield return part.Title; yield return $"{arc.Name} {part.Title}";
        foreach (var alias in part.Aliases) yield return alias;
        foreach (var number in new[] { part.Number.ToString(CultureInfo.InvariantCulture), words.ElementAtOrDefault(part.Number), roman.ElementAtOrDefault(part.Number) }.Where(value => value is not null))
            foreach (var label in new[] { "", "book ", "part " })
            {
                yield return $"{arc.Name} {label}{number} {part.Title}";
                yield return $"{arc.Name} {part.Title} {label}{number}";
                yield return $"{part.Title} {arc.Name} {label}{number}";
            }
    }
    private static string Key(string value)
    {
        var normalized = new string(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        normalized = Regex.Replace(normalized, "[^a-z0-9]+", " ").Trim();
        normalized = Regex.Replace(normalized, @"^(?:star trek\s+)?(?:(?:the\s+)?(?:next generation|original series)|deep space (?:nine|9)|voyager|enterprise)\s+", "");
        normalized = Regex.Replace(normalized, @"^star trek\s+", "");
        return Regex.Replace(normalized, @"^the\s+", "");
    }
    private static bool SameIsbn(IEnumerable<string> values, string? isbn) => isbn is not null && values.Any(value => IsbnUtility.Canonical(value) == isbn);
    public static bool IsOmnibus(StoryArc arc, Book book) => book.Kind == BookKind.Book &&
        (SameIsbn(arc.OmnibusIsbns, IsbnUtility.Canonical(book.Isbn)) || arc.Series.Contains(book.Series) && arc.OmnibusTitles.Any(title => Key(title) == Key(book.Title)));
    public static bool Matches(StoryArc arc, StoryArcPart part, Book book)
    {
        if (book.Kind != BookKind.Book) return false;
        var isbn = IsbnUtility.Canonical(book.Isbn);
        // A known ISBN takes precedence over conflicting title text or an incorrect saved franchise.
        if (isbn is not null)
        {
            if (isbnIndex.Value.TryGetValue(isbn, out var identified)) return identified.Arc == arc.Id && identified.Part == part.Number;
            var omnibus = Reference.FirstOrDefault(set => SameIsbn(set.OmnibusIsbns, isbn));
            if (omnibus is not null) return omnibus.Id == arc.Id;
        }
        if (!arc.Series.Contains(book.Series)) return false;
        if (IsOmnibus(arc, book)) return true;
        return titleIndex.Value[(arc.Id, part.Number)].Contains(Key(book.Title));
    }
    public static StoryArcReport Analyze(IEnumerable<Book> books, StoryArc arc, BookFormat? format, bool physicalOnly = false)
    {
        var all = books.Where(book => book.Kind == BookKind.Book).ToArray();
        bool InFormat(Book book) => (format is null || book.Format == format) && (!physicalOnly || book.Format is BookFormat.Paperback or BookFormat.Hardcover);
        var slots = arc.Parts.Select(part =>
        {
            var matching = all.Where(book => Matches(arc, part, book)).ToArray();
            var owned = matching.Where(book => book.Ownership == OwnershipStatus.Owned && InFormat(book)).ToArray();
            return new StoryArcSlot(part, owned, matching.Where(book => book.Ownership == OwnershipStatus.Wishlist && InFormat(book)).ToArray(),
                matching.Where(book => book.Ownership == OwnershipStatus.Owned && !InFormat(book)).ToArray(), owned.Length > 0 && owned.All(book => IsOmnibus(arc, book)));
        }).ToArray();
        var matched = slots.SelectMany(slot => slot.Owned).Select(book => book.Id).ToHashSet();
        var unmatched = all.Where(book => book.Ownership == OwnershipStatus.Owned && InFormat(book) && arc.Series.Contains(book.Series) &&
            (" " + Key(book.Title) + " ").Contains(" " + Key(arc.Name) + " ", StringComparison.Ordinal) && !matched.Contains(book.Id)).ToArray();
        return new(arc, slots, unmatched);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StoryArc[]))]
internal partial class StoryArcJson : JsonSerializerContext;
