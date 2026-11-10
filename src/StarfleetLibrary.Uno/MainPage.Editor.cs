using Microsoft.UI.Xaml.Automation;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Uno;

public sealed partial class MainPage
{
    private async Task EditBookAsync(Book original, bool isNew)
    {
        if (store.IsReadOnly) { ShowNotice("This library is read only. " + store.LoadError, true); return; }
        var form = new StackPanel { Spacing = 16, Width = Math.Max(280, Math.Min(640, ActualWidth - 160)) };
        var title = Field("Title", "BookTitle", original.Title);
        var authors = Field("Authors", "BookAuthors", original.Authors);
        var isbn = Field("ISBN", "BookIsbn", original.Isbn);
        var year = Field("Publication year", "BookYear", original.PublicationYear);
        var publisher = Field("Publisher", "BookPublisher", original.Publisher);
        var edition = Field("Edition", "BookEdition", original.Edition);
        var number = Field("Book number", "BookNumber", original.BookNumber);
        var cover = Field("Cover image URL (optional)", "BookCoverUrl", original.CoverUrl ?? "");
        var notes = Field("Notes", "BookNotes", original.Notes); notes.AcceptsReturn = true; notes.TextWrapping = TextWrapping.Wrap; notes.Height = 120;
        var seriesValues = Enum.GetValues<TrekSeries>();
        var series = Picker("BookSeries", seriesValues.Select(v => v.DisplayName()), Array.IndexOf(seriesValues, original.Series), "Series");
        var kind = Picker("BookKind", ["Book", "COMIC"], (int)original.Kind, "Type");
        var format = Picker("BookFormat", Enum.GetValues<BookFormat>().Select(v => v.DisplayName()), (int)original.Format, "Format");
        var ownership = Picker("BookOwnership", ["Owned", "Wishlist"], (int)original.Ownership, "Collection");
        var status = Picker("BookStatus", Enum.GetValues<ReadingStatus>().Select(v => v.DisplayName()), (int)original.ReadingStatus, "Reading status");
        var rating = Picker("BookRating", ["Not rated", "★", "★★", "★★★", "★★★★", "★★★★★"], original.Rating, "Rating");
        var pages = Number("Total pages (0 if unknown)", "BookPages", original.PageCount);
        var current = Number("Current page", "BookCurrentPage", original.CurrentPage);
        var finished = new CalendarDatePicker { Header = "Date finished (optional)", Date = original.FinishedAt, PlaceholderText = "No date recorded", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(finished, "BookFinishedDate");
        var feedback = new InfoBar { IsOpen = false, IsClosable = false };
        form.Children.Add(Text(isNew ? "Make room for a new adventure." : "Update this edition and its reading log.", 14));
        form.Children.Add(feedback);
        form.Children.Add(title); form.Children.Add(authors);
        var lookupButton = ActionButton("Fill details from ISBN", (_, _) => { });
        lookupButton.Click += async (_, _) =>
        {
            lookupButton.IsEnabled = false; feedback.IsOpen = false;
            try
            {
                var found = await lookup.FindAsync(isbn.Text);
                title.Text = found.Title; authors.Text = found.Authors; publisher.Text = found.Publisher;
                year.Text = found.PublicationYear; pages.Value = found.PageCount; cover.Text = found.CoverUrl ?? "";
                isbn.Text = found.Isbn; series.SelectedIndex = Array.IndexOf(seriesValues, found.Series);
                feedback.Severity = InfoBarSeverity.Informational; feedback.Message = "Metadata filled from Open Library. Review the title, series and format before saving."; feedback.IsOpen = true;
            }
            catch (Exception ex) { feedback.Severity = InfoBarSeverity.Error; feedback.Message = ex.Message; feedback.IsOpen = true; }
            finally { lookupButton.IsEnabled = true; }
        };
        form.Children.Add(Pair(isbn, year)); form.Children.Add(lookupButton);
        form.Children.Add(Pair(series, kind)); form.Children.Add(Pair(format, ownership));
        form.Children.Add(Pair(publisher, edition)); form.Children.Add(number);
        form.Children.Add(Text("Reading log", 20, "#17243B", true));
        form.Children.Add(Pair(status, rating)); form.Children.Add(Pair(pages, current)); form.Children.Add(finished);
        var clearDate = ActionButton("Clear finished date", (_, _) => finished.Date = null); form.Children.Add(clearDate);
        form.Children.Add(Text("Artwork & notes", 20, "#17243B", true));
        form.Children.Add(cover);
        var preview = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Left };
        preview.Content = CoverImage(original, 90, 134);
        form.Children.Add(ActionButton("Preview cover", (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(cover.Text) && !BookCover.IsWebUrl(cover.Text))
            { feedback.Message = "Use an http or https image URL, or leave it blank for imported artwork / ISBN lookup."; feedback.Severity = InfoBarSeverity.Error; feedback.IsOpen = true; return; }
            preview.Content = CoverImage(original with { Isbn = isbn.Text.Trim(), CoverUrl = string.IsNullOrWhiteSpace(cover.Text) ? null : cover.Text.Trim() }, 90, 134);
        }));
        form.Children.Add(preview); form.Children.Add(notes);

        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(240, ActualHeight - 240) };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = isNew ? "Add an edition" : "Edit edition", Content = scroll,
            PrimaryButtonText = isNew ? "Add to library" : "Save changes", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None, RequestedTheme = ElementTheme.Light
        };
        dialog.Resources["ContentDialogMaxWidth"] = 740d;
        string? acknowledgedDuplicate = null;
        Book? saved = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                feedback.IsOpen = false;
                if (new[] { pages.Value, current.Value }.Any(v => double.IsNaN(v) || v != Math.Truncate(v))) throw new InvalidOperationException("Page counts must be whole numbers.");
                if (!string.IsNullOrWhiteSpace(cover.Text) && !BookCover.IsWebUrl(cover.Text)) throw new InvalidOperationException("Use an http or https cover URL, or leave it empty.");
                var readingStatus = (ReadingStatus)status.SelectedIndex;
                var book = original with
                {
                    Title = title.Text.Trim(), Authors = authors.Text.Trim(), Isbn = IsbnUtility.Normalize(isbn.Text),
                    PublicationYear = year.Text.Trim(), Publisher = publisher.Text.Trim(), Edition = edition.Text.Trim(), BookNumber = number.Text.Trim(),
                    Series = seriesValues[series.SelectedIndex], Kind = (BookKind)kind.SelectedIndex, Format = (BookFormat)format.SelectedIndex,
                    Ownership = (OwnershipStatus)ownership.SelectedIndex, ReadingStatus = readingStatus,
                    PageCount = (int)pages.Value, CurrentPage = (int)current.Value, Rating = rating.SelectedIndex,
                    FinishedAt = readingStatus == ReadingStatus.Finished ? finished.Date : null,
                    CoverUrl = string.IsNullOrWhiteSpace(cover.Text) ? null : cover.Text.Trim(), Notes = notes.Text
                };
                if (book.ValidationError is { } error) throw new InvalidOperationException(error);
                var duplicates = CatalogMatcher.ExactMatches(book.Isbn, store.Books).Where(b => b.Id != book.Id).ToArray();
                var key = IsbnUtility.Canonical(book.Isbn);
                if (duplicates.Length > 0 && acknowledgedDuplicate != key)
                {
                    acknowledgedDuplicate = key;
                    feedback.Severity = InfoBarSeverity.Warning;
                    feedback.Message = $"You already have {duplicates.Length} edition(s) with this ISBN. Save again to keep a separate copy.";
                    feedback.IsOpen = true;
                    dialog.PrimaryButtonText = "Save separate copy";
                    scroll.ChangeView(null, 0, null);
                    return;
                }
                await store.SaveAsync(book);
                saved = book;
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                feedback.Message = ex.Message; feedback.Severity = InfoBarSeverity.Error; feedback.IsOpen = true;
                scroll.ChangeView(null, 0, null);
            }
            finally { deferral.Complete(); }
        };
        SizeChangedEventHandler resized = (_, _) =>
        {
            form.Width = Math.Max(280, Math.Min(640, ActualWidth - 160));
            scroll.MaxHeight = Math.Max(240, ActualHeight - 240);
        };
        SizeChanged += resized;
        try { await dialog.ShowAsync(); }
        finally { SizeChanged -= resized; }
        if (saved is not null) { rows.Remove(saved.Id); ShowDetail(saved); ShowNotice(isNew ? "Edition added to your library." : "Edition updated."); }
    }

    private static TextBox Field(string label, string id, string value)
    {
        var field = new TextBox { Header = label, Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(field, id); return field;
    }
    private static NumberBox Number(string label, string id, int value)
    {
        var field = new NumberBox { Header = label, Value = value, Minimum = 0, Maximum = int.MaxValue, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        AutomationProperties.SetAutomationId(field, id); return field;
    }
    private static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
}
