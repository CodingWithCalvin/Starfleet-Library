using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StarfleetLibrary.Core;

public sealed record BuyingGuideItem(string Key, string Title, string Isbn, string[] Reasons, string[] Formats, bool OnWishlist)
{
    public string Identification => string.Join(" · ", Reasons);
    public string FormatLabel => string.Join(", ", Formats);
}
public sealed record BuyingGuideReport(BuyingGuideItem[] Items, string[] Warnings)
{
    public string ShoppingList => string.Join("\n", Items.Select(item => $"☐ {item.Title} — {item.Identification} · {item.FormatLabel}" + (item.Isbn.Length > 0 ? $" · ISBN {item.Isbn}" : "")));
}

/// <summary>A read-only shopping projection. Inferred numbers never alter catalog records.</summary>
public static class BuyingGuide
{
    private static string TitleKey(string value)
    {
        var key = Regex.Replace(new string(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        key = Regex.Replace(key, @"^(?:star trek\s+)?(?:(?:the\s+)?(?:next generation|original series)|deep space (?:nine|9)|voyager|enterprise)\s+", "");
        key = Regex.Replace(key, @"^star trek\s+", "");
        return Regex.Replace(key, @"^the\s+", "");
    }

    private static NumberedNovel? IdentifyNumber(Book book)
    {
        if (book.Kind != BookKind.Book) return null;
        if (NumberedBooks.FindByIsbn(book.Isbn) is { } isbnMatch) return isbnMatch;
        if (NumberedBooks.ParseNumber(book.BookNumber) is { } number)
            return NumberedBooks.Reference.FirstOrDefault(n => n.Series == book.Series && n.Number == number)
                ?? new(book.Series, number, book.Title, IsbnUtility.Canonical(book.Isbn) ?? "");
        var exact = NumberedBooks.Reference.Where(n => n.Series == book.Series && TitleKey(n.Title) == TitleKey(book.Title)).ToArray();
        if (exact.Length == 1) return exact[0];
        foreach (var arc in StoryArcs.Reference)
        {
            if (StoryArcs.IsOmnibus(arc, book)) continue;
            foreach (var part in arc.Parts.Where(part => StoryArcs.Matches(arc, part, book)))
                foreach (var isbn in part.Isbns)
                    if (NumberedBooks.FindByIsbn(isbn) is { } match) return match;
        }
        return null;
    }
    private static string Identity(Book book)
    {
        if (IdentifyNumber(book) is { } n) return $"number:{(int)n.Series}:{n.Number}";
        foreach (var arc in StoryArcs.Reference)
        {
            if (StoryArcs.IsOmnibus(arc, book)) continue;
            foreach (var part in arc.Parts)
                if (StoryArcs.Matches(arc, part, book)) return $"arc:{arc.Id}:{part.Number}";
        }
        return $"title:{(int)book.Kind}:{(int)book.Series}:{TitleKey(book.Title)}";
    }

    public static BuyingGuideReport Build(IEnumerable<Book> catalog, TrekSeries series, BookFormat? format,
        int first, int last, bool physicalOnly = false, bool startedArcsOnly = true)
    {
        var books = catalog.ToArray();
        bool InFormat(Book b) => (format is null || b.Format == format) && (!physicalOnly || b.Format is BookFormat.Paperback or BookFormat.Hardcover);
        var owned = books.Where(b => b.Ownership == OwnershipStatus.Owned).ToArray();
        var identities = books.ToDictionary(b => b.Id, Identity);
        string Key(Book b) => identities.TryGetValue(b.Id, out var key) ? key : Identity(b);
        var covered = owned.Where(InFormat).Select(Key).ToHashSet();
        foreach (var arc in StoryArcs.Reference)
            foreach (var part in arc.Parts.Where(p => owned.Any(b => InFormat(b) && StoryArcs.Matches(arc, p, b))))
                foreach (var isbn in part.Isbns)
                    if (NumberedBooks.FindByIsbn(isbn) is { } n) covered.Add($"number:{(int)n.Series}:{n.Number}");
        var items = new Dictionary<string, BuyingGuideItem>();
        var warnings = new List<string>();
        var selectedFormat = physicalOnly ? "Print" : format?.DisplayName() ?? "Any format";
        void Add(string key, string title, string isbn, string reason, string formatLabel, bool wishlist = false)
        {
            if (items.TryGetValue(key, out var old))
                items[key] = old with { Title = old.OnWishlist ? old.Title : title, Reasons = old.Reasons.Append(reason).Distinct().ToArray(),
                    Formats = old.Formats.Append(formatLabel).Distinct().ToArray(), OnWishlist = old.OnWishlist || wishlist,
                    Isbn = old.Isbn.Length > 0 ? old.Isbn : isbn };
            else items[key] = new(key, title, isbn, [reason], [formatLabel], wishlist);
        }
        // A wish for a hardcover remains useful even when a paperback is owned.
        foreach (var book in books.Where(b => b.Series == series && b.Ownership == OwnershipStatus.Wishlist && InFormat(b)))
        {
            var key = Key(book);
            if (owned.Any(b => b.Format == book.Format && Key(b) == key)) continue;
            var number = IdentifyNumber(book);
            var reason = book.Kind == BookKind.Comic ? "Wishlist · COMIC" : "Wishlist";
            Add(key, book.Title, IsbnUtility.Canonical(book.Isbn) ?? "", reason, book.Format.DisplayName(), true);
            if (number is not null) Add(key, book.Title, number.Isbn, $"{number.Series.ShortName()} #{number.Number}", book.Format.DisplayName());
        }
        if (first >= 1 && last >= first && last <= NumberedBooks.MaximumNumber)
        {
            for (var number = first; number <= last; number++)
            {
                var key = $"number:{(int)series}:{number}";
                if (covered.Contains(key)) continue;
                var novel = NumberedBooks.Reference.FirstOrDefault(n => n.Series == series && n.Number == number);
                Add(key, novel?.Title ?? $"{series.ShortName()} #{number} (title unknown)", novel?.Isbn ?? "", $"{series.ShortName()} #{number}", selectedFormat);
            }
        }
        else warnings.Add("Numbered gaps omitted: choose a valid first and last number. Some series have no standard numbered run.");
        foreach (var arc in StoryArcs.Reference.Where(a => a.Series.Contains(series)))
        {
            var report = StoryArcs.Analyze(books, arc, format, physicalOnly);
            if (startedArcsOnly && report.OwnedCount == 0 && report.Unmatched.Length == 0) continue;
            foreach (var slot in report.Slots.Where(s => s.Missing))
            {
                var isbn = slot.Part.Isbns.Select(IsbnUtility.Canonical).FirstOrDefault(s => s is not null) ?? "";
                var candidate = new Book { Title = slot.Part.Title, Isbn = isbn, Series = series };
                if (owned.Any(b => InFormat(b) && Key(b) == Identity(candidate))) continue;
                Add(Identity(candidate), slot.Part.Title, isbn, $"{arc.Name} {slot.Part.Number}/{arc.Parts.Length}", selectedFormat);
            }
            if (report.Unmatched.Length > 0) warnings.Add($"{arc.Name}: review {report.Unmatched.Length} unmatched owned titles.");
        }
        var uncertain = owned.Count(b => b.Series == series && b.Kind == BookKind.Book && InFormat(b) && IdentifyNumber(b) is null);
        if (uncertain > 0) warnings.Add($"{uncertain} owned editions have no recognized series number. Review numbering; some may be unnumbered releases.");
        var result = items.Values.Select(item => item with { Formats = item.Formats.Length > 1 && item.OnWishlist
            ? item.Formats.Where(f => f != selectedFormat || selectedFormat is not ("Print" or "Any format")).ToArray() : item.Formats })
            .OrderByDescending(i => i.OnWishlist).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(result, warnings.ToArray());
    }
}
