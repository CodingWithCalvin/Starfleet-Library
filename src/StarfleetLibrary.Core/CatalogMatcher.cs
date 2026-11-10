using System.Globalization;
using System.Text;

namespace StarfleetLibrary.Core;

public static class CatalogMatcher
{
    public static IReadOnlyList<Book> ExactMatches(string isbn, IEnumerable<Book> books)
    {
        var key = IsbnUtility.Canonical(isbn);
        return key is null ? Array.Empty<Book>() : books.Where(book => IsbnUtility.Canonical(book.Isbn) == key).ToArray();
    }

    /// <summary>Conservative hints, never proof two records are the same work.</summary>
    public static IReadOnlyList<Book> PossibleOtherEditions(Book candidate, IEnumerable<Book> books)
    {
        var title = ComparisonText(candidate.Title);
        var authors = ComparisonText(candidate.Authors);
        if (title.Length == 0 || authors.Length == 0) return Array.Empty<Book>();
        var candidateIsbn = IsbnUtility.Canonical(candidate.Isbn);
        return books.Where(book => book.Id != candidate.Id &&
            (candidateIsbn is null || IsbnUtility.Canonical(book.Isbn) != candidateIsbn) &&
            ComparisonText(book.Title) == title && ComparisonText(book.Authors) == authors).ToArray();
    }

    private static string ComparisonText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new StringBuilder();
        var pendingSpace = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && result.Length > 0) result.Append(' ');
                result.Append(char.ToUpperInvariant(character));
                pendingSpace = false;
            }
            else pendingSpace = true;
        }
        return result.ToString();
    }
}
