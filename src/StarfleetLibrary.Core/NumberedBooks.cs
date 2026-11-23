using System.Globalization;
using System.Text.RegularExpressions;

namespace StarfleetLibrary.Core;

public sealed record NumberedNovel(TrekSeries Series, int Number, string Title, string Isbn);
public sealed record NumberedSlot(int Number, Book[] Owned, Book[] Wishlist, Book[] OtherFormats)
{
    public bool Missing => Owned.Length == 0;
    public string Status => !Missing ? "Owned" : Wishlist.Length > 0 ? "On wishlist" : "Missing";
    public string Description => string.Join(" · ", Owned.Concat(Wishlist).Concat(OtherFormats).Select(book => book.Title).Distinct());
}
public sealed record NumberedReport(NumberedSlot[] Slots, Book[] NeedsNumber, Book[] OutsideRange)
{
    public int OwnedCount => Slots.Count(slot => !slot.Missing);
    public int MissingCount => Slots.Count(slot => slot.Missing);
    public string MissingNumbers => string.Join(", ", Slots.Where(slot => slot.Missing).Select(slot => $"#{slot.Number}"));
}

public static class NumberedBooks
{
    public const int MaximumNumber = 1000;
    private static readonly Lazy<NumberedNovel[]> reference = new(() =>
    {
        using var stream = typeof(NumberedBooks).Assembly.GetManifestResourceStream("StarfleetLibrary.NumberedNovels.tsv")
            ?? throw new InvalidDataException("Numbered novel reference is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var fields = line.TrimEnd('\r').Split('\t');
            return new NumberedNovel((TrekSeries)int.Parse(fields[0], CultureInfo.InvariantCulture),
                int.Parse(fields[1], CultureInfo.InvariantCulture), fields[2], IsbnUtility.Canonical(fields[3])!);
        }).ToArray();
    });
    public static IReadOnlyList<NumberedNovel> Reference => reference.Value;
    public static NumberedNovel? FindByIsbn(string isbn)
    {
        var key = IsbnUtility.Canonical(isbn);
        return key is null ? null : Reference.FirstOrDefault(book => book.Isbn == key);
    }
    public static int? ParseNumber(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.StartsWith('#')) value = value[1..].Trim();
        return value.Length > 0 && value.All(c => c is >= '0' and <= '9') &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= MaximumNumber ? number : null;
    }
    public static int SuggestedEnd(IEnumerable<Book> books, TrekSeries series) => Math.Max(
        Reference.Where(book => book.Series == series).Select(book => book.Number).DefaultIfEmpty(0).Max(),
        books.Where(book => book.Series == series && book.Kind == BookKind.Book).Select(book => ParseNumber(book.BookNumber) ?? 0).DefaultIfEmpty(0).Max());

    // Only explicit series metadata is accepted, never numbers from titles or dates.
    public static (TrekSeries Series, int Number)? FromSeriesMetadata(IEnumerable<string> values)
    {
        var names = new[] { @"star\s+trek(?:\s*[:.;-]?\s*(?:the\s+)?original\s+series)?", @"(?:star\s+trek\s*[:.;-]?\s*)?(?:the\s+)?next\s+generation", @"(?:star\s+trek\s*[:.;-]?\s*)?deep\s+space\s+(?:nine|9)", @"(?:star\s+trek\s*[:.;-]?\s*)?voyager" };
        var matches = new HashSet<(TrekSeries Series, int Number)>();
        foreach (var value in values)
            for (var i = 0; i < names.Length; i++)
            {
                var match = Regex.Match(value, @"^\s*" + names[i] + @"[\s:;,.()#\-–—]*(?:(?:no\.?|number|book|vol\.?|volume)\s*)?#?\s*(\d+)[\s.)]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success && ParseNumber(match.Groups[1].Value) is { } number) matches.Add(((TrekSeries)i, number));
            }
        return matches.Count == 1 ? matches.Single() : null;
    }

    public static NumberedReport Analyze(IEnumerable<Book> books, TrekSeries series, BookFormat? format, int first, int last, bool physicalOnly = false)
    {
        if (first < 1 || last < first || last > MaximumNumber) throw new ArgumentOutOfRangeException(nameof(last), "Use a whole-number range between 1 and 1000.");
        var selected = books.Where(book => book.Series == series && book.Kind == BookKind.Book).ToArray();
        bool InFormat(Book book) => (format is null || book.Format == format) && (!physicalOnly || book.Format is BookFormat.Paperback or BookFormat.Hardcover);
        var owned = selected.Where(book => book.Ownership == OwnershipStatus.Owned && InFormat(book)).ToArray();
        var slots = Enumerable.Range(first, last - first + 1).Select(number =>
        {
            var matches = selected.Where(book => ParseNumber(book.BookNumber) == number).ToArray();
            return new NumberedSlot(number, matches.Where(book => book.Ownership == OwnershipStatus.Owned && InFormat(book)).ToArray(),
                matches.Where(book => book.Ownership == OwnershipStatus.Wishlist && InFormat(book)).ToArray(),
                matches.Where(book => book.Ownership == OwnershipStatus.Owned && !InFormat(book)).ToArray());
        }).ToArray();
        return new(slots, owned.Where(book => ParseNumber(book.BookNumber) is null).ToArray(),
            owned.Where(book => ParseNumber(book.BookNumber) is { } number && (number < first || number > last)).ToArray());
    }
}
