using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Uno;

public sealed class BookRow(Book book, CoverImageCache cache) : INotifyPropertyChanged
{
    public Book Book { get; } = book;
    public string Title => Book.Title;
    public string Authors => string.IsNullOrWhiteSpace(Book.Authors) ? "Unknown author" : Book.Authors;
    public string Label => Book.CatalogLabel();
    public string Series => Book.Series.ShortName();
    public string Kind => Book.Kind == BookKind.Comic ? "COMIC" : "BOOK";
    public string Format => Book.Format.DisplayName();
    public string FormatRibbon => Format.ToUpperInvariant();
    public SolidColorBrush FormatRibbonBackground { get; } = new(book.Format switch
    {
        BookFormat.Ebook => Windows.UI.Color.FromArgb(255, 20, 112, 119),
        BookFormat.Paperback => Windows.UI.Color.FromArgb(255, 140, 78, 34),
        BookFormat.Hardcover => Windows.UI.Color.FromArgb(255, 52, 75, 118),
        BookFormat.Audiobook => Windows.UI.Color.FromArgb(255, 109, 72, 143),
        _ => Windows.UI.Color.FromArgb(255, 65, 79, 99)
    });
    public string Year => Book.PublicationYear;
    public string Status => Book.ReadingStatus.DisplayName();
    public double Progress => Book.Progress * 100;
    public string EditionLine => string.Join("  ·  ", new[] { Format, Year, Book.Edition }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string AccessibleName => $"Open {Title}, {Authors}, {Label}, {Format}, {Status}";
    public ImageSource? Cover { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool loading;
    private DateTimeOffset retryAfter;
    public async Task LoadCoverAsync()
    {
        if (Cover is not null || loading || DateTimeOffset.UtcNow < retryAfter) return;
        loading = true;
        try
        {
            var path = await cache.GetAsync(Book);
            if (path is not null)
            {
                Cover = new BitmapImage(new Uri(path));
                PropertyChanged?.Invoke(this, new(nameof(Cover)));
            }
            else retryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
        }
        finally { loading = false; }
    }
}
