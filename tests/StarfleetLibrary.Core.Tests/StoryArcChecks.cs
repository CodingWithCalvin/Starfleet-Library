using StarfleetLibrary.Core;

internal static class StoryArcChecks
{
    public static void Coverage()
    {
        Check.Equal(7, StoryArcs.Reference.Count);
        Check.Equal(30, StoryArcs.Reference.Sum(arc => arc.Parts.Length));
        foreach (var arc in StoryArcs.Reference)
        {
            Check.Equal(Enumerable.Range(1, arc.Parts.Length).ToArray(), arc.Parts.Select(part => part.Number).ToArray(), true);
            Check.True(arc.Parts.SelectMany(part => part.Isbns).Concat(arc.OmnibusIsbns).All(IsbnUtility.IsValid));
            Check.True(arc.Sources.Length > 0 && arc.Series.Length > 0);
        }
        var helix = StoryArcs.Reference.Single(arc => arc.Id == "double-helix");
        var books = helix.Parts.Take(5).Select(part => new Book { Title = $"Double Helix, Book {part.Number}: {part.Title}", Series = TrekSeries.NextGeneration, Isbn = part.Isbns[0], Format = BookFormat.Paperback }).ToList();
        books.Add(books[0] with { Id = Guid.NewGuid() });
        books.Add(new Book { Title = "Double Helix Omnibus", Series = TrekSeries.NextGeneration, Format = BookFormat.Ebook });
        var before = CatalogBackup.Encode(books);
        var physical = StoryArcs.Analyze(books, helix, null, physicalOnly: true);
        Check.Equal(5, physical.OwnedCount);
        Check.Equal("Part 6: The First Virtue", physical.MissingTitles);
        Check.Equal(1, physical.Slots[5].OtherFormats.Length);
        Check.Equal(6, StoryArcs.Analyze(books, helix, null).OwnedCount);
        Check.Equal("Owned via omnibus", StoryArcs.Analyze(books, helix, null).Slots[5].Status);
        Check.True(StoryArcs.Analyze(books, helix, BookFormat.Ebook).Slots.All(slot => slot.Status == "Owned via omnibus"));
        Check.True(before.SequenceEqual(CatalogBackup.Encode(books)), "Story-arc report changed the catalog");
        books.Add(new Book { Title = "The First Virtue", Series = TrekSeries.NextGeneration, Format = BookFormat.Paperback, Ownership = OwnershipStatus.Wishlist });
        Check.Equal("On wishlist", StoryArcs.Analyze(books, helix, null, true).Slots[5].Status);
        books.Add(new Book { Title = "The First Virtue", Series = TrekSeries.NextGeneration, Format = BookFormat.Hardcover });
        Check.Equal(6, StoryArcs.Analyze(books, helix, null, true).OwnedCount);
        Check.Equal(5, StoryArcs.Analyze(books, helix, BookFormat.Paperback).OwnedCount);
    }
    public static void Matching()
    {
        var helix = StoryArcs.Reference.Single(arc => arc.Id == "double-helix");
        var title = new Book { Title = "Star Trek: The Next Generation: Double Helix, Book One: Infection", Series = TrekSeries.NextGeneration };
        Check.True(StoryArcs.Matches(helix, helix.Parts[0], title));
        Check.False(StoryArcs.Matches(helix, helix.Parts[1], title));
        Check.False(StoryArcs.Matches(helix, helix.Parts[0], title with { Kind = BookKind.Comic }));
        Check.False(StoryArcs.Matches(helix, helix.Parts[0], title with { Title = "Double Helix Infection unrelated sequel" }));
        var conflicting = title with { Isbn = helix.Parts[1].Isbns[0], Series = TrekSeries.Voyager };
        Check.False(StoryArcs.Matches(helix, helix.Parts[0], conflicting));
        Check.True(StoryArcs.Matches(helix, helix.Parts[1], conflicting));
        Check.Equal(1, StoryArcs.Analyze([title with { Title = "Double Helix Collection" }], helix, null).Unmatched.Length);
        var destiny = StoryArcs.Reference.Single(arc => arc.Id == "destiny");
        Check.True(StoryArcs.Matches(destiny, destiny.Parts[2], new Book { Title = "Lost Souls (Destiny, Book III)", Series = TrekSeries.Other }));
        var genesis = StoryArcs.Reference.Single(arc => arc.Id == "genesis-wave");
        Check.True(StoryArcs.Matches(genesis, genesis.Parts[0], new Book { Title = "The Genesis Wave, Book One", Series = TrekSeries.NextGeneration }));
        var millennium = StoryArcs.Reference.Single(arc => arc.Id == "millennium");
        Check.Equal(3, StoryArcs.Analyze([new Book { Title = "Millennium: Fall of Terok Nor/War of the Prophets/Inferno (Star Trek: Deep Space Nine)", Series = TrekSeries.DeepSpaceNine, Format = BookFormat.Ebook }], millennium, null).OwnedCount);
    }
}
