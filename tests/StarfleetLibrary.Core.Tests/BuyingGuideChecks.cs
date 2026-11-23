using StarfleetLibrary.Core;

internal static class BuyingGuideChecks
{
    public static void Combined()
    {
        var arc = StoryArcs.Reference.Single(a => a.Id == "double-helix");
        var books = arc.Parts.Take(5).Select(p => new Book { Title = p.Title, Isbn = p.Isbns[0], Series = TrekSeries.NextGeneration }).ToList();
        var wish = new Book { Title = "The First Virtue", Isbn = arc.Parts[5].Isbns[0], Series = TrekSeries.NextGeneration, Ownership = OwnershipStatus.Wishlist };
        books.Add(wish); books.Add(wish with { Id = Guid.NewGuid() });
        var before = CatalogBackup.Encode(books);
        var report = BuyingGuide.Build(books, TrekSeries.NextGeneration, null, 51, 56, true);
        Check.Equal(1, report.Items.Length);
        Check.Equal(3, report.Items[0].Reasons.Length);
        Check.True(report.Items[0].Identification.Contains("TNG #56"));
        Check.True(report.Items[0].Identification.Contains("Double Helix 6/6"));
        Check.Equal("Paperback", report.Items[0].FormatLabel);
        Check.Equal(before, CatalogBackup.Encode(books), true);
        books.Add(wish with { Id = Guid.NewGuid(), Ownership = OwnershipStatus.Owned });
        Check.Equal(0, BuyingGuide.Build(books, TrekSeries.NextGeneration, null, 51, 56, true).Items.Length);
    }
    public static void Formats()
    {
        var novel = NumberedBooks.Reference.First(n => n.Series == TrekSeries.NextGeneration);
        var paperback = new Book { Title = novel.Title, Isbn = novel.Isbn, Series = novel.Series };
        var wish = paperback with { Id = Guid.NewGuid(), Format = BookFormat.Hardcover, Ownership = OwnershipStatus.Wishlist };
        var report = BuyingGuide.Build([paperback, wish], novel.Series, null, 1, 1, true);
        Check.Equal(1, report.Items.Length); Check.True(report.Items[0].OnWishlist);
        Check.Equal("Hardcover", report.Items[0].FormatLabel);
        Check.Equal(0, BuyingGuide.Build([paperback, wish], novel.Series, BookFormat.Paperback, 1, 1).Items.Length);
        Check.Equal(1, BuyingGuide.Build([paperback with { Format = BookFormat.Ebook }], novel.Series, null, 1, 1, true).Items.Length);
        Check.Equal(0, BuyingGuide.Build([paperback with { Format = BookFormat.Ebook }], novel.Series, null, 1, 1).Items.Length);
        var omnibus = new Book { Title = "Double Helix Omnibus", Series = novel.Series, Format = BookFormat.Ebook };
        Check.Equal(0, BuyingGuide.Build([omnibus], novel.Series, null, 51, 56).Items.Length);
        Check.Equal(6, BuyingGuide.Build([omnibus], novel.Series, null, 51, 56, true).Items.Length);
        var prefixed = paperback with { Isbn = "", Title = "Star Trek: The Next Generation: " + novel.Title };
        Check.Equal(0, BuyingGuide.Build([prefixed], novel.Series, null, 1, 1, true).Items.Length);
    }
    public static void Boundaries()
    {
        var comic = new Book { Title = "My comic", Series = TrekSeries.StrangeNewWorlds, Kind = BookKind.Comic, BookNumber = "1", Ownership = OwnershipStatus.Wishlist };
        var report = BuyingGuide.Build([comic], comic.Series, null, 0, 0, true);
        Check.Equal(1, report.Items.Length); Check.True(report.Warnings.Length > 0);
        Check.Equal("Wishlist · COMIC", report.Items[0].Identification);
        Check.Equal(0, BuyingGuide.Build([comic], TrekSeries.Voyager, null, 0, 0, true).Items.Length);
        var unknown = BuyingGuide.Build([], TrekSeries.StrangeNewWorlds, null, 99, 99, true);
        Check.Equal("SNW #99 (title unknown)", unknown.Items[0].Title);
        Check.Equal("", unknown.Items[0].Isbn);
        var ownedComic = comic with { Ownership = OwnershipStatus.Owned };
        Check.Equal(1, BuyingGuide.Build([ownedComic], comic.Series, null, 1, 1, true).Items.Length);
        var tng = new Book { Title = "Unknown numbered title", Series = TrekSeries.NextGeneration, BookNumber = "56" };
        var firstHelix = new Book { Title = "Infection", Series = TrekSeries.NextGeneration };
        Check.False(BuyingGuide.Build([tng, firstHelix], tng.Series, null, 56, 56, true).Items.Any(i => i.Key == "number:1:56"));
    }
}
