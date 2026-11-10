namespace StarfleetLibrary.Core;

public static class BookCover
{
    public static bool IsWebUrl(string? value) => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrWhiteSpace(uri.Host);

    public static Uri? Source(Book book)
    {
        if (IsWebUrl(book.CoverUrl)) return new Uri(book.CoverUrl!.Trim());
        var isbn = IsbnUtility.Canonical(book.Isbn);
        return isbn is null ? null : new Uri($"https://covers.openlibrary.org/b/isbn/{isbn}-M.jpg?default=false");
    }
}
