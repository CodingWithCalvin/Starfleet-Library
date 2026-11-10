using StarfleetLibrary.Core;

namespace StarfleetLibrary.Sync;

public static class CatalogEditGuard
{
    public static void RequireUnchanged(IEnumerable<Book> books, Book? expected)
    {
        if (expected is not null && books.FirstOrDefault(book => book.Id == expected.Id) != expected)
            throw new InvalidOperationException("This edition changed while you were viewing it. Your draft is still here. Reopen the edition to see the latest version before saving or removing it.");
    }
}
