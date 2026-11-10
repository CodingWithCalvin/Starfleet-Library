namespace StarfleetLibrary.Core;

public static class SampleCatalog
{
    /// <summary>Opt-in demo entries; edition metadata and reading activity are illustrative.</summary>
    public static IReadOnlyList<Book> Books => new Book[]
    {
        new() { Title = "Spock’s World", Authors = "Diane Duane", Series = TrekSeries.OriginalSeries,
            Publisher = "Pocket Books", PublicationYear = "1988", Format = BookFormat.Hardcover,
            ReadingStatus = ReadingStatus.Reading, PageCount = 336, CurrentPage = 128,
            Notes = "Sample book. Edition details and reading progress are illustrative." },
        new() { Title = "Q-Squared", Authors = "Peter David", Series = TrekSeries.NextGeneration,
            Publisher = "Pocket Books", PublicationYear = "1994", Format = BookFormat.Hardcover,
            ReadingStatus = ReadingStatus.Finished, PageCount = 448, CurrentPage = 448, Rating = 5,
            FinishedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            Notes = "Sample book. Edition details and rating are illustrative." },
        new() { Title = "A Stitch in Time", Authors = "Andrew J. Robinson", Series = TrekSeries.DeepSpaceNine,
            Publisher = "Pocket Books", PublicationYear = "2000", ReadingStatus = ReadingStatus.Reading,
            PageCount = 432, CurrentPage = 86, Notes = "Sample book. Edition details and reading progress are illustrative." },
        new() { Title = "Imzadi", Authors = "Peter David", Series = TrekSeries.NextGeneration,
            Publisher = "Pocket Books", PublicationYear = "1992", Format = BookFormat.Hardcover,
            ReadingStatus = ReadingStatus.Finished, PageCount = 352, CurrentPage = 352, Rating = 4,
            FinishedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Notes = "Sample book. Edition details and rating are illustrative." },
        new() { Title = "The Entropy Effect", Authors = "Vonda N. McIntyre", Series = TrekSeries.OriginalSeries,
            BookNumber = "2", Publisher = "Pocket Books", PublicationYear = "1981", PageCount = 224,
            Notes = "Sample book. Edition details are illustrative." },
        new() { Title = "Homecoming", Authors = "Christie Golden", Series = TrekSeries.Voyager,
            BookNumber = "1", Publisher = "Pocket Books", PublicationYear = "2003", PageCount = 304,
            Notes = "Sample book. Edition details are illustrative." },
        new() { Title = "The Lives of Dax", Authors = "Marco Palmieri (editor)", Series = TrekSeries.DeepSpaceNine,
            Publisher = "Pocket Books", PublicationYear = "1999", Ownership = OwnershipStatus.Wishlist, PageCount = 384,
            Notes = "Sample wish-list entry. Edition details are illustrative." },
        new() { Title = "Federation", Authors = "Judith and Garfield Reeves-Stevens", Series = TrekSeries.Other,
            Publisher = "Pocket Books", PublicationYear = "1994", Format = BookFormat.Hardcover,
            Ownership = OwnershipStatus.Wishlist, PageCount = 448,
            Notes = "Sample wish-list entry. Edition details are illustrative." }
    };
}
