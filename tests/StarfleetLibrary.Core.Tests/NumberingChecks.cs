using StarfleetLibrary.Core;

internal static class NumberingChecks
{
    public static void Reference()
    {
        Check.Equal(208, NumberedBooks.Reference.Count);
        Check.Equal(208, NumberedBooks.Reference.Select(b => b.Isbn).Distinct().Count());
        var ends = new[] { 97, 63, 27, 21 };
        for (var series = 0; series < ends.Length; series++)
            Check.Equal(Enumerable.Range(1, ends[series]).ToArray(), NumberedBooks.Reference.Where(b => (int)b.Series == series).Select(b => b.Number).ToArray(), true);
        Check.True(NumberedBooks.Reference.All(b => IsbnUtility.IsValid(b.Isbn) && b.Title.Length > 0));
        var escape = NumberedBooks.FindByIsbn("0-671-52096-2")!;
        Check.Equal(TrekSeries.Voyager, escape.Series);
        Check.Equal(2, escape.Number);
        Check.Equal(escape, NumberedBooks.FindByIsbn("9780671520960"));
        Check.Equal(22, NumberedBooks.FindByIsbn("9780671775711")!.Number);
        Check.Equal<NumberedNovel?>(null, NumberedBooks.FindByIsbn("9780306406157"));
        Check.Equal(0, NumberedBooks.SuggestedEnd([], TrekSeries.Enterprise));
        Check.Equal(63, NumberedBooks.SuggestedEnd([], TrekSeries.NextGeneration));
    }

    public static void Gaps()
    {
        var first = new Book { Title = "First", Series = TrekSeries.NextGeneration, BookNumber = " #01 ", Format = BookFormat.Paperback };
        Book[] books = [first, first with { Id = Guid.NewGuid() }, first with { Id = Guid.NewGuid(), BookNumber = "2", Format = BookFormat.Ebook },
            first with { Id = Guid.NewGuid(), BookNumber = "3", Ownership = OwnershipStatus.Wishlist },
            first with { Id = Guid.NewGuid(), BookNumber = "4", Kind = BookKind.Comic },
            first with { Id = Guid.NewGuid(), BookNumber = "" }, first with { Id = Guid.NewGuid(), BookNumber = "Book 5" },
            first with { Id = Guid.NewGuid(), BookNumber = "9" }, first with { Id = Guid.NewGuid(), BookNumber = "4", Series = TrekSeries.Voyager }];
        var before = CatalogBackup.Encode(books);
        var paper = NumberedBooks.Analyze(books, TrekSeries.NextGeneration, BookFormat.Paperback, 1, 4);
        Check.Equal(1, paper.OwnedCount);
        Check.Equal(3, paper.MissingCount);
        Check.Equal("#2, #3, #4", paper.MissingNumbers);
        Check.Equal(2, paper.Slots[0].Owned.Length);
        Check.Equal(1, paper.Slots[1].OtherFormats.Length);
        Check.Equal("On wishlist", paper.Slots[2].Status);
        Check.True(paper.Slots[2].Missing);
        Check.Equal(2, paper.NeedsNumber.Length);
        Check.Equal(1, paper.OutsideRange.Length);
        Check.Equal(2, NumberedBooks.Analyze(books, TrekSeries.NextGeneration, null, 1, 4).OwnedCount);
        var physical = NumberedBooks.Analyze(books.Concat([first with { Id = Guid.NewGuid(), BookNumber = "4", Format = BookFormat.Hardcover }, first with { Id = Guid.NewGuid(), BookNumber = "", Format = BookFormat.Ebook }]), TrekSeries.NextGeneration, null, 1, 4, physicalOnly: true);
        Check.Equal(2, physical.OwnedCount);
        Check.Equal(2, physical.NeedsNumber.Length);
        Check.True(physical.Slots[1].Missing && !physical.Slots[3].Missing, "Print filter must include hardcovers and exclude ebooks");
        Check.True(before.SequenceEqual(CatalogBackup.Encode(books)), "Gap analysis modified the catalog");
        Check.Equal(4, NumberedBooks.Analyze([], TrekSeries.Voyager, null, 1, 4).MissingCount);
        Check.Throws(() => NumberedBooks.Analyze([], TrekSeries.Voyager, null, 0, 5));
        Check.Throws(() => NumberedBooks.Analyze([], TrekSeries.Voyager, null, 5, 4));
        Check.Throws(() => NumberedBooks.Analyze([], TrekSeries.Voyager, null, 1, 1001));
        foreach (var value in new[] { "", "1.5", "-1", "0", "1001", "1-3", "##1", "Book 1", "２", "99999999999999999999" })
            Check.Equal<int?>(null, NumberedBooks.ParseNumber(value));
        Check.Equal<int?>(1, NumberedBooks.ParseNumber(" #001 "));
        Check.Equal<int?>(1000, NumberedBooks.ParseNumber("1000"));
    }

    public static void Metadata()
    {
        Check.Equal<(TrekSeries, int)?>((TrekSeries.NextGeneration, 21), NumberedBooks.FromSeriesMetadata(["Star Trek: The Next Generation #21"]));
        Check.Equal<(TrekSeries, int)?>((TrekSeries.DeepSpaceNine, 27), NumberedBooks.FromSeriesMetadata(["Star Trek. Deep Space Nine -- no. 27"]));
        Check.Equal<(TrekSeries, int)?>((TrekSeries.OriginalSeries, 2), NumberedBooks.FromSeriesMetadata(["Star Trek ; 2"]));
        Check.Equal<(TrekSeries, int)?>((TrekSeries.Voyager, 6), NumberedBooks.FromSeriesMetadata(["Star Trek: Voyager, book 6", "Voyager #6"]));
        foreach (var values in new[] { new[] { "Star Trek 1995" }, new[] { "Homecoming Book 1" }, new[] { "Next Generation #2", "Next Generation #3" }, new[] { "Star Trek Next Generation Destiny 1" }, new[] { "Voyager" } })
            Check.Equal<(TrekSeries, int)?>(null, NumberedBooks.FromSeriesMetadata(values));
    }
}
