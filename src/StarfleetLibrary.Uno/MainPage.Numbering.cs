using Microsoft.UI.Xaml.Automation;
using StarfleetLibrary.Core;
using Windows.ApplicationModel.DataTransfer;

namespace StarfleetLibrary.Uno;

public sealed partial class MainPage
{
    private int gapSeries = (int)TrekSeries.NextGeneration, gapFormat, gapFirst = 1, gapLast = 63;
    private bool gapMissingOnly = true, reviewNumbers, reviewBlanksOnly = true, storyArcView, startedArcsOnly = true;
    private string numberingSearch = "";
    private StackPanel? gapResults;

    private void ShowMissingBooks()
    {
        var root = new Grid { RowSpacing = 16 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var controls = new StackPanel { Spacing = 12 };
        var view = Picker("MissingView", ["Numbered series", "Story arcs"], storyArcView ? 1 : 0, "View");
        view.SelectionChanged += (_, _) => { storyArcView = view.SelectedIndex == 1; reviewNumbers = false; Body.Children.Clear(); ShowMissingBooks(); };
        controls.Children.Add(view);
        var series = Picker("GapSeries", Enum.GetValues<TrekSeries>().Select(value => value.DisplayName()), gapSeries, "Series");
        var format = Picker("GapFormat", new[] { "Print books", "Any format" }.Concat(Enum.GetValues<BookFormat>().Select(value => value.DisplayName())), gapFormat, "Format");
        controls.Children.Add(Pair(series, format));
        var first = Number("First number", "GapFirst", gapFirst);
        var last = Number("Last number", "GapLast", gapLast);
        first.Minimum = last.Minimum = 1; first.Maximum = last.Maximum = NumberedBooks.MaximumNumber;
        if (gapLast == 0) last.Value = double.NaN;
        if (!storyArcView) controls.Children.Add(Pair(first, last));
        controls.Children.Add(Text(storyArcView ? "Complete your multi-part stories. Print books counts paperbacks and hardcovers; an ebook omnibus won't fill a print gap." : "Print books counts paperbacks and hardcovers. Classic Pocket Books ranges are suggested; adjust for your numbering scheme. Only saved book numbers count; comics are excluded.", 12));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var mode = ActionButton(reviewNumbers ? "Show missing numbers" : "Review numbering", (_, _) =>
        {
            reviewNumbers = !reviewNumbers; Body.Children.Clear(); ShowMissingBooks();
        });
        if (!storyArcView) actions.Children.Add(mode);
        actions.Children.Add(ActionButton(storyArcView ? "Copy missing titles" : "Copy missing numbers", (_, _) =>
        {
            try
            {
                if (storyArcView)
                {
                    var arcData = new DataPackage(); arcData.SetText(string.Join("\n\n", ArcReports().Select(report => report.Arc.Name + "\n" + (report.MissingTitles.Length > 0 ? report.MissingTitles : "Complete"))));
                    Clipboard.SetContent(arcData); ShowNotice("Missing titles copied."); return;
                }
                var report = NumberedBooks.Analyze(store.Books, (TrekSeries)gapSeries, GapFormat, gapFirst, gapLast, physicalOnly: gapFormat == 0);
                var text = $"{((TrekSeries)gapSeries).DisplayName()} · {(gapFormat == 0 ? "Print books" : GapFormat?.DisplayName() ?? "Any format")} · #{gapFirst}–#{gapLast}\nNot recorded as owned: {report.MissingNumbers}\nBased on saved book numbers; {report.NeedsNumber.Length} editions have blank or invalid numbers.";
                var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); ShowNotice("Missing numbers copied.");
            }
            catch (Exception error) { ShowNotice(error.Message, true); }
        }));
        controls.Children.Add(actions);
        var toggle = new CheckBox { Content = storyArcView ? "Only missing parts" : reviewNumbers ? "Only blank or invalid numbers" : "Only missing numbers", IsChecked = reviewNumbers ? reviewBlanksOnly : gapMissingOnly };
        toggle.Click += (_, _) => { if (reviewNumbers) reviewBlanksOnly = toggle.IsChecked == true; else gapMissingOnly = toggle.IsChecked == true; RefreshNumberedBooks(); };
        controls.Children.Add(toggle);
        if (storyArcView)
        {
            var started = new CheckBox { Content = "Only sets I own in this format", IsChecked = startedArcsOnly };
            started.Click += (_, _) => { startedArcsOnly = started.IsChecked == true; RefreshNumberedBooks(); }; controls.Children.Add(started);
        }
        if (reviewNumbers)
        {
            var search = new TextBox { PlaceholderText = "Find a title or ISBN to number", Text = numberingSearch };
            AutomationProperties.SetName(search, "Search books to number");
            search.TextChanged += (_, _) => { numberingSearch = search.Text; RefreshNumberedBooks(); };
            controls.Children.Add(search);
        }
        series.SelectionChanged += (_, _) =>
        {
            gapSeries = series.SelectedIndex; gapFirst = 1; gapLast = NumberedBooks.SuggestedEnd(store.Books, (TrekSeries)gapSeries);
            first.Value = gapFirst; last.Value = gapLast == 0 ? double.NaN : gapLast; RefreshNumberedBooks();
        };
        format.SelectionChanged += (_, _) => { gapFormat = format.SelectedIndex; RefreshNumberedBooks(); };
        first.ValueChanged += (_, _) => { gapFirst = double.IsFinite(first.Value) && first.Value == Math.Truncate(first.Value) ? (int)first.Value : 0; RefreshNumberedBooks(); };
        last.ValueChanged += (_, _) => { gapLast = double.IsFinite(last.Value) && last.Value == Math.Truncate(last.Value) ? (int)last.Value : 0; RefreshNumberedBooks(); };
        root.Children.Add(controls);
        gapResults = new StackPanel { Spacing = 14 };
        var scroll = new ScrollViewer { Content = gapResults, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll); Body.Children.Add(root);
        RefreshNumberedBooks();
    }

    private BookFormat? GapFormat => gapFormat <= 1 ? null : (BookFormat)(gapFormat - 2);
    private void RefreshNumberedBooks()
    {
        if (section != "Missing books" || gapResults is null || detailOpen) return;
        gapResults.Children.Clear();
        if (storyArcView) { ShowArcResults(); return; }
        if (reviewNumbers)
        {
            var books = store.Books.Where(book => book.Series == (TrekSeries)gapSeries && book.Kind == BookKind.Book &&
                book.Ownership == OwnershipStatus.Owned && (GapFormat is null || book.Format == GapFormat) &&
                (gapFormat != 0 || book.Format is BookFormat.Paperback or BookFormat.Hardcover) &&
                (!reviewBlanksOnly || NumberedBooks.ParseNumber(book.BookNumber) is null) &&
                $"{book.Title} {book.Isbn}".Contains(numberingSearch.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(book => book.Title).ToArray();
            gapResults.Children.Add(Text($"{books.Length} editions to review", 20, "#17243B", true));
            gapResults.Children.Add(Text("Leave genuinely unnumbered books blank. Set the main series number printed on the book, not a trilogy's internal number.", 12));
            foreach (var book in books)
            {
                var reference = NumberedBooks.FindByIsbn(book.Isbn);
                var row = new StackPanel { Spacing = 8 };
                row.Children.Add(Text(book.Title, 17, "#17243B", true));
                row.Children.Add(Text($"{book.Format.DisplayName()} · {book.Isbn} · Saved number: {(book.BookNumber.Length > 0 ? book.BookNumber : "blank")}", 12));
                if (reference is not null) row.Children.Add(Text($"ISBN match: {reference.Series.ShortName()} #{reference.Number}", 12, "#147D83"));
                row.Children.Add(ActionButton("Set book number", async (_, _) => await EditNumberAsync(book)));
                gapResults.Children.Add(Surface(row, 16));
            }
            return;
        }
        if (gapFirst < 1 || gapLast < gapFirst || gapLast > NumberedBooks.MaximumNumber)
        {
            gapResults.Children.Add(Text("Choose a whole-number range between 1 and 1000 to see the gaps. Some series have no standard numbered run.")); return;
        }
        var report = NumberedBooks.Analyze(store.Books, (TrekSeries)gapSeries, GapFormat, gapFirst, gapLast, physicalOnly: gapFormat == 0);
        gapResults.Children.Add(Text($"{report.MissingCount} missing · {report.OwnedCount} of {report.Slots.Length} numbers recorded as owned", 20, "#17243B", true));
        if (report.NeedsNumber.Length > 0) gapResults.Children.Add(Text($"{report.NeedsNumber.Length} owned editions have blank or invalid numbers. Review numbering before treating this as a complete shopping list.", 13, "#9B552B"));
        if (report.OutsideRange.Length > 0) gapResults.Children.Add(Text($"{report.OutsideRange.Length} numbered copies are outside this range.", 12));
        if (report.MissingCount == 0) gapResults.Children.Add(Text("Every number in this range is recorded as owned.", 16, "#147D83", true));
        var missing = Text(report.MissingNumbers.Length > 0 ? report.MissingNumbers : "No missing numbers", 15, "#17243B");
        missing.IsTextSelectionEnabled = true; gapResults.Children.Add(Surface(missing, 16));
        var tiles = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 136, ItemHeight = 82 };
        foreach (var slot in report.Slots.Where(slot => !gapMissingOnly || slot.Missing))
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(Text($"#{slot.Number}", 20, "#17243B", true));
            content.Children.Add(Text(slot.Status, 12, slot.Missing ? "#9B552B" : "#147D83"));
            var tile = new Border { Child = content, Margin = new(0, 0, 10, 10), Padding = new(12, 8, 12, 8), CornerRadius = new(8), Background = Brush(slot.Missing ? "#FFF0E5" : "#E4F0EF") };
            AutomationProperties.SetName(tile, $"Number {slot.Number}: {slot.Status}");
            ToolTipService.SetToolTip(tile, slot.Description + (slot.OtherFormats.Length > 0 ? " · Owned in another format" : ""));
            tiles.Children.Add(tile);
        }
        gapResults.Children.Add(tiles);
    }

    private StoryArcReport[] ArcReports() => StoryArcs.Reference.Where(arc => arc.Series.Contains((TrekSeries)gapSeries))
        .Select(arc => StoryArcs.Analyze(store.Books, arc, GapFormat, physicalOnly: gapFormat == 0))
        .Where(report => !startedArcsOnly || report.OwnedCount > 0 || report.Unmatched.Length > 0).ToArray();

    private void ShowArcResults()
    {
        var reports = ArcReports();
        var missing = reports.Sum(report => report.Slots.Count(slot => slot.Missing));
        gapResults!.Children.Add(Text($"{missing} missing {(missing == 1 ? "part" : "parts")} across {reports.Length} story {(reports.Length == 1 ? "arc" : "arcs")}", 20, "#17243B", true));
        gapResults.Children.Add(Text("Matches use known ISBNs or exact title variants. Reference coverage currently includes seven sets; unmatched editions are shown for review.", 12));
        if (reports.Length == 0) gapResults.Children.Add(Text("No supported sets match these filters. Turn off 'Only sets I own in this format' or choose another series or format."));
        foreach (var report in reports)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Text(report.Arc.Name, 21, "#17243B", true));
            panel.Children.Add(Text($"{report.OwnedCount} of {report.Slots.Length} parts owned · {report.Slots.Length - report.OwnedCount} missing", 14, "#147077", true));
            foreach (var slot in report.Slots.Where(slot => !gapMissingOnly || slot.Missing))
            {
                panel.Children.Add(Text($"Part {slot.Part.Number}: {slot.Part.Title}", 16, "#17243B", true));
                panel.Children.Add(Text(slot.Status + (slot.OtherFormats.Length > 0 ? " · Also owned in " + string.Join(", ", slot.OtherFormats.Select(book => book.Format.DisplayName()).Distinct()) : ""), 12, slot.Missing ? "#9B552B" : "#147077"));
            }
            if (report.OwnedCount == report.Slots.Length) panel.Children.Add(Text("Complete in this format.", 14, "#147077"));
            if (report.Unmatched.Length > 0) panel.Children.Add(Text("Review unmatched titles: " + string.Join(" · ", report.Unmatched.Select(book => book.Title)), 12, "#9B552B"));
            gapResults.Children.Add(Surface(panel, 20));
        }
    }

    private async Task EditNumberAsync(Book original)
    {
        if (store.IsReadOnly) return;
        var reference = NumberedBooks.FindByIsbn(original.Isbn);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Text(original.Title, 18, "#17243B", true));
        var series = Picker("QuickNumberSeries", Enum.GetValues<TrekSeries>().Select(value => value.DisplayName()), (int)original.Series, "Series");
        var number = Field("Book number (blank for unnumbered books)", "QuickBookNumber", original.BookNumber);
        panel.Children.Add(series); panel.Children.Add(number);
        if (reference is not null)
            panel.Children.Add(ActionButton($"Use ISBN match: {reference.Series.ShortName()} #{reference.Number}", (_, _) => { series.SelectedIndex = (int)reference.Series; number.Text = reference.Number.ToString(); }));
        var feedback = Text("", 12, "#A43F31"); panel.Children.Add(feedback);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Set book number", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            try
            {
                var parsed = NumberedBooks.ParseNumber(number.Text);
                if (!string.IsNullOrWhiteSpace(number.Text) && parsed is null) throw new InvalidOperationException("Enter a whole number from 1 to 1000, or leave blank.");
                await store.SaveAsync(original with { Series = (TrekSeries)series.SelectedIndex, BookNumber = parsed?.ToString() ?? "" }, original);
                rows.Remove(original.Id); args.Cancel = false;
            }
            catch (Exception error) { feedback.Text = error.Message; }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync(); RefreshNumberedBooks();
    }
}
