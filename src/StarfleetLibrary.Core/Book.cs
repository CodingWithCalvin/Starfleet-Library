using System.Text.Json.Serialization;

namespace StarfleetLibrary.Core;

public enum TrekSeries { OriginalSeries, NextGeneration, DeepSpaceNine, Voyager, Enterprise, Discovery, Picard, Other, StrangeNewWorlds }
public enum BookKind { Book, Comic }
public enum BookFormat { Paperback, Hardcover, Ebook, Audiobook }
public enum OwnershipStatus { Owned, Wishlist }
public enum ReadingStatus { Unread, Reading, Finished }

/// <summary>An individual edition/copy; different formats can have separate reading logs.</summary>
public sealed record Book
{
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonRequired] public string Title { get; set; } = string.Empty;
    [JsonRequired] public string Authors { get; set; } = string.Empty;
    [JsonRequired] public TrekSeries Series { get; set; } = TrekSeries.Other;
    [JsonRequired] public string BookNumber { get; set; } = string.Empty;
    [JsonRequired] public string Isbn { get; set; } = string.Empty;
    [JsonRequired] public string Publisher { get; set; } = string.Empty;
    [JsonRequired] public string PublicationYear { get; set; } = string.Empty;
    [JsonRequired] public string Edition { get; set; } = string.Empty;
    [JsonRequired] public BookFormat Format { get; set; } = BookFormat.Paperback;
    // Optional for compatibility with existing Swift and MAUI backups.
    public BookKind Kind { get; set; } = BookKind.Book;
    [JsonRequired] public OwnershipStatus Ownership { get; set; } = OwnershipStatus.Owned;
    [JsonRequired] public ReadingStatus ReadingStatus { get; set; } = ReadingStatus.Unread;
    [JsonRequired] public int PageCount { get; set; }
    [JsonRequired] public int CurrentPage { get; set; }
    [JsonRequired] public int Rating { get; set; }
    [JsonRequired] public string Notes { get; set; } = string.Empty;
    [JsonRequired] public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string? CoverUrl { get; set; }

    [JsonIgnore]
    public double Progress => ReadingStatus == global::StarfleetLibrary.Core.ReadingStatus.Finished ? 1 :
        PageCount > 0 ? Math.Clamp((double)CurrentPage / PageCount, 0, 1) : 0;

    [JsonIgnore]
    public string? ValidationError
    {
        get
        {
            if (Id == Guid.Empty) return "The book identifier cannot be empty.";
            if (Title is null || Authors is null || BookNumber is null || Isbn is null || Publisher is null ||
                PublicationYear is null || Edition is null || Notes is null)
                return "Text fields cannot be null.";
            if (string.IsNullOrWhiteSpace(Title)) return "Enter a book title.";
            if (!Enum.IsDefined(Series) || !Enum.IsDefined(Format) || !Enum.IsDefined(Kind) || !Enum.IsDefined(Ownership) || !Enum.IsDefined(ReadingStatus))
                return "Choose a recognized series, format, ownership, and reading status.";
            if (PageCount < 0 || CurrentPage < 0) return "Page numbers cannot be negative.";
            if (PageCount > 0 && CurrentPage > PageCount) return "Current page cannot exceed the total pages.";
            if (Rating is < 0 or > 5) return "Choose a rating from zero to five stars.";
            if (!string.IsNullOrWhiteSpace(Isbn) && !IsbnUtility.IsValid(Isbn))
                return "Enter a valid ISBN-10 or ISBN-13, or leave ISBN empty.";
            var year = PublicationYear.Trim();
            if (year.Length != 0 && (year.Length != 4 || year.Any(c => c is < '0' or > '9')))
                return "Enter a four-digit publication year, or leave it empty.";
            return null;
        }
    }
}

public static class BookLabels
{
    public static string DisplayName(this TrekSeries value) => value switch
    {
        TrekSeries.OriginalSeries => "The Original Series",
        TrekSeries.NextGeneration => "The Next Generation",
        TrekSeries.DeepSpaceNine => "Deep Space Nine",
        TrekSeries.Voyager => "Voyager",
        TrekSeries.Enterprise => "Enterprise",
        TrekSeries.Discovery => "Discovery",
        TrekSeries.Picard => "Picard",
        TrekSeries.StrangeNewWorlds => "Strange New Worlds",
        _ => "Other / Crossover"
    };

    public static string ShortName(this TrekSeries value) => value switch
    {
        TrekSeries.OriginalSeries => "TOS", TrekSeries.NextGeneration => "TNG",
        TrekSeries.DeepSpaceNine => "DS9", TrekSeries.Voyager => "VOY",
        TrekSeries.Enterprise => "ENT", TrekSeries.Discovery => "DIS",
        TrekSeries.Picard => "PIC", TrekSeries.StrangeNewWorlds => "SNW", _ => "OTHER"
    };

    public static string DisplayName(this BookKind value) => value == BookKind.Comic ? "Comic" : "Book";

    public static string CatalogLabel(this Book book) => book.Kind == BookKind.Comic
        ? book.Series == TrekSeries.Other ? "COMIC" : $"COMIC · {book.Series.ShortName()}"
        : book.Series.ShortName();

    public static string DisplayName(this BookFormat value) => value switch
    {
        BookFormat.Paperback => "Paperback", BookFormat.Hardcover => "Hardcover",
        BookFormat.Ebook => "E-book", BookFormat.Audiobook => "Audiobook", _ => "Unknown"
    };

    public static string DisplayName(this OwnershipStatus value) => value == OwnershipStatus.Owned ? "Owned" : "Wish list";

    public static string DisplayName(this ReadingStatus value) => value switch
    {
        ReadingStatus.Unread => "Unread", ReadingStatus.Reading => "Reading",
        ReadingStatus.Finished => "Finished", _ => "Unknown"
    };
}
