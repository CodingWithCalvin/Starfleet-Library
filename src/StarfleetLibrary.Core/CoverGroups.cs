using System.Text;

namespace StarfleetLibrary.Core;

/// <summary>Display-only groups; every copy keeps its own identity and metadata.</summary>
public static class CoverGroups
{
    public static string TitleKey(string title) => string.Join(' ',
        title.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public static bool SameGroup(Book first, Book second) => first.Format == second.Format &&
        first.Ownership == second.Ownership && TitleKey(first.Title) == TitleKey(second.Title);

    // Call after filtering and sorting: counts describe matching copies, and the
    // first matching copy determines each tile's place in the current sort order.
    public static Book[][] Group(IEnumerable<Book> books) => books
        .GroupBy(book => (TitleKey(book.Title), book.Format, book.Ownership))
        .Select(group => group.ToArray()).ToArray();

    public static string CopyLabel(Book book, int index) => string.Join(" · ",
        new[] { $"Copy {index + 1}", book.Edition, book.Isbn, book.ReadingStatus.DisplayName() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}
