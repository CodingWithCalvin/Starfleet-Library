using Microsoft.UI.Xaml.Input;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Mobile;

public sealed partial class MainPage
{
    private bool numbering, onlyMissingNumbers = true, storyArcView, startedArcsOnly = true;
    private int numberedSeries = 1, numberedFormat;
    private string firstNumber = "1", lastNumber = "63";
    private StackPanel? numberedResults;

    private void ReturnToShelf() { if (numbering) ShowNumbering(); else ShowShelf(); }

    private void ShowNumbering()
    {
        numbering = true; editing = detail = false; list = null;
        Heading.Text = "Missing books";
        var panel = PagePanel("Back to " + (shelf == OwnershipStatus.Owned ? "collection" : "wishlist"), (_, _) => ShowShelf());
        var view = Pick(["Numbered series", "Story arcs"], storyArcView ? 1 : 0);
        Label(panel, "View", view);
        view.SelectionChanged += (_, _) => { storyArcView = view.SelectedIndex == 1; ShowNumbering(); };
        var series = Pick(Enum.GetValues<TrekSeries>().Select(value => value.DisplayName()).ToArray(), numberedSeries);
        Label(panel, "Series", series);
        var formatPicker = Pick(["Print books", "Any format", "Paperback", "Hardcover", "E-book", "Audiobook"], numberedFormat);
        Label(panel, "Format", formatPicker);
        var range = new Grid { ColumnSpacing = 12 };
        range.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        range.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var startPanel = new StackPanel { Spacing = 8 }; var endPanel = new StackPanel { Spacing = 8 };
        var first = Field(startPanel, "First number", firstNumber); var last = Field(endPanel, "Last number", lastNumber);
        first.InputScope = new() { Names = { new InputScopeName { NameValue = InputScopeNameValue.Number } } };
        last.InputScope = new() { Names = { new InputScopeName { NameValue = InputScopeNameValue.Number } } };
        range.Children.Add(startPanel); Grid.SetColumn(endPanel, 1); range.Children.Add(endPanel); if (!storyArcView) panel.Children.Add(range);
        panel.Children.Add(Txt(storyArcView ? "Complete your multi-part stories. Print books counts paperbacks and hardcovers; an ebook omnibus won't fill a print gap." : "Print books counts paperbacks and hardcovers. Classic Pocket Books ranges are suggested; adjust for your numbering scheme. Only saved book numbers count; comics are excluded.", 12));
        var missing = new CheckBox { Content = storyArcView ? "Only missing parts" : "Only missing numbers", IsChecked = onlyMissingNumbers };
        missing.Click += (_, _) => { onlyMissingNumbers = missing.IsChecked == true; RefreshNumbering(); }; panel.Children.Add(missing);
        if (storyArcView)
        {
            var started = new CheckBox { Content = "Only sets I own in this format", IsChecked = startedArcsOnly };
            started.Click += (_, _) => { startedArcsOnly = started.IsChecked == true; RefreshNumbering(); }; panel.Children.Add(started);
        }
        numberedResults = new StackPanel { Spacing = 14 }; panel.Children.Add(numberedResults);
        series.SelectionChanged += (_, _) =>
        {
            numberedSeries = series.SelectedIndex;
            var end = NumberedBooks.SuggestedEnd(store.Books, (TrekSeries)numberedSeries);
            first.Text = firstNumber = "1"; last.Text = lastNumber = end > 0 ? end.ToString() : ""; RefreshNumbering();
        };
        formatPicker.SelectionChanged += (_, _) => { numberedFormat = formatPicker.SelectedIndex; RefreshNumbering(); };
        first.TextChanged += (_, _) => { firstNumber = first.Text; RefreshNumbering(); };
        last.TextChanged += (_, _) => { lastNumber = last.Text; RefreshNumbering(); };
        RefreshNumbering();
    }

    private void RefreshNumbering()
    {
        if (!numbering || editing || detail || numberedResults is null) return;
        numberedResults.Children.Clear();
        if (storyArcView) { ShowStoryArcResults(); return; }
        if (NumberedBooks.ParseNumber(firstNumber) is not { } first || NumberedBooks.ParseNumber(lastNumber) is not { } last || last < first)
        { numberedResults.Children.Add(Txt("Choose a whole-number range between 1 and 1000. Some series have no standard numbered run.")); return; }
        var report = NumberedBooks.Analyze(store.Books, (TrekSeries)numberedSeries, numberedFormat <= 1 ? null : (BookFormat)(numberedFormat - 2), first, last, physicalOnly: numberedFormat == 0);
        numberedResults.Children.Add(Txt($"{report.MissingCount} missing · {report.OwnedCount} of {report.Slots.Length} numbers recorded as owned", 20, "#17243B", true));
        if (report.NeedsNumber.Length > 0)
            numberedResults.Children.Add(Txt($"{report.NeedsNumber.Length} owned editions have blank or invalid numbers. Review these below before treating this as a complete shopping list.", 13, "#9B552B"));
        if (report.OutsideRange.Length > 0) numberedResults.Children.Add(Txt($"{report.OutsideRange.Length} numbered copies are outside this range.", 12));
        var missing = Txt(report.MissingCount > 0 ? report.MissingNumbers : "Every number in this range is recorded as owned.", 16, "#17243B");
        missing.IsTextSelectionEnabled = true; numberedResults.Children.Add(missing);
        if (!onlyMissingNumbers || report.Slots.Any(slot => slot.Missing && (slot.Wishlist.Length > 0 || slot.OtherFormats.Length > 0)))
            foreach (var slot in report.Slots.Where(slot => !onlyMissingNumbers || slot.Missing && (slot.Wishlist.Length > 0 || slot.OtherFormats.Length > 0)))
                numberedResults.Children.Add(Txt($"#{slot.Number} · {slot.Status}" + (slot.OtherFormats.Length > 0 ? " · Owned in another format" : ""), 14, slot.Missing ? "#9B552B" : "#147077"));
        if (report.NeedsNumber.Length > 0)
        {
            numberedResults.Children.Add(Txt("Review numbering", 20, "#17243B", true));
            numberedResults.Children.Add(Txt("Tap an edition to fill its number, or use ISBN lookup. Leave unnumbered releases blank.", 12));
            foreach (var book in report.NeedsNumber.OrderBy(book => book.Title))
                numberedResults.Children.Add(Btn($"{book.Title}\n{book.Format.DisplayName()} · {book.Isbn}", (_, _) => ShowEditor(book, false)));
        }
    }

    private void ShowStoryArcResults()
    {
        var reports = StoryArcs.Reference.Where(arc => arc.Series.Contains((TrekSeries)numberedSeries))
            .Select(arc => StoryArcs.Analyze(store.Books, arc, numberedFormat <= 1 ? null : (BookFormat)(numberedFormat - 2), physicalOnly: numberedFormat == 0))
            .Where(report => !startedArcsOnly || report.OwnedCount > 0 || report.Unmatched.Length > 0).ToArray();
        var missing = reports.Sum(report => report.Slots.Count(slot => slot.Missing));
        numberedResults!.Children.Add(Txt($"{missing} missing {(missing == 1 ? "part" : "parts")} across {reports.Length} story {(reports.Length == 1 ? "arc" : "arcs")}", 20, "#17243B", true));
        numberedResults.Children.Add(Txt("Matches use known ISBNs or exact title variants. Reference coverage currently includes seven sets.", 12));
        if (reports.Length == 0) numberedResults.Children.Add(Txt("No supported sets match these filters. Turn off 'Only sets I own in this format' or change the series or format."));
        foreach (var report in reports)
        {
            numberedResults.Children.Add(Txt($"{report.Arc.Name} · {report.OwnedCount} of {report.Slots.Length} parts owned", 19, "#17243B", true));
            foreach (var slot in report.Slots.Where(slot => !onlyMissingNumbers || slot.Missing))
            {
                numberedResults.Children.Add(Txt($"Part {slot.Part.Number}: {slot.Part.Title}", 16, "#17243B", true));
                numberedResults.Children.Add(Txt(slot.Status + (slot.OtherFormats.Length > 0 ? " · Also owned in " + string.Join(", ", slot.OtherFormats.Select(book => book.Format.DisplayName()).Distinct()) : ""), 12, slot.Missing ? "#9B552B" : "#147077"));
            }
            if (report.OwnedCount == report.Slots.Length) numberedResults.Children.Add(Txt("Complete in this format.", 14, "#147077"));
            if (report.Unmatched.Length > 0) numberedResults.Children.Add(Txt("Review unmatched titles: " + string.Join(" · ", report.Unmatched.Select(book => book.Title)), 12, "#9B552B"));
        }
    }
}
