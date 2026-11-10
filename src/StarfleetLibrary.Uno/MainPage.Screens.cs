using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using StarfleetLibrary.Core;
using Windows.Storage.Pickers;

namespace StarfleetLibrary.Uno;

public sealed partial class MainPage
{
    private void ShowDetail(Book book)
    {
        detailOpen = true;
        itemView = null;
        Body.Children.Clear();
        PageTitle.Text = "Between the covers";
        PageSubtitle.Text = "Edition details and your reading journey.";
        var stack = new StackPanel { Spacing = 20 };
        stack.Children.Add(ActionButton("←  Back to " + section.ToLowerInvariant(), (_, _) => Navigate(section)));
        var hero = new Grid { ColumnSpacing = 26 };
        hero.ColumnDefinitions.Add(new() { Width = new(148) });
        hero.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        hero.Children.Add(CoverImage(book, 148, 220));
        var title = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Text(book.CatalogLabel(), 12, "#147D83", true));
        title.Children.Add(Text(book.Title, 26, "#17243B", true));
        title.Children.Add(Text(book.Authors, 16));
        title.Children.Add(Text(Row(book).EditionLine, 13));
        title.Children.Add(Text($"{book.Ownership.DisplayName()}  ·  {book.ReadingStatus.DisplayName()}", 13, "#147D83"));
        var edit = ActionButton("Edit details", async (_, _) => await EditBookAsync(book, false), true);
        edit.IsEnabled = !store.IsReadOnly;
        title.Children.Add(edit);
        Grid.SetColumn(title, 1); hero.Children.Add(title);
        stack.Children.Add(Surface(hero));

        var reading = new StackPanel { Spacing = 14 };
        reading.Children.Add(Text("Your reading journey", 20, "#17243B", true));
        reading.Children.Add(Text(book.FinishedAt is { } date ? $"Finished {date.LocalDateTime:MMMM d, yyyy}" : book.ReadingStatus == ReadingStatus.Finished ? "Finished · date not recorded" : book.PageCount > 0 ? $"Page {book.CurrentPage:N0} of {book.PageCount:N0} · {book.Progress:P0}" : "Total page count is not recorded."));
        reading.Children.Add(new ProgressBar { Value = book.Progress * 100, Maximum = 100, Height = 5, Foreground = Brush("#147D83") });
        var progress = new NumberBox { Header = "Current page", Value = book.CurrentPage, Minimum = 0, Maximum = book.PageCount > 0 ? book.PageCount : int.MaxValue, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(progress, "ProgressPage");
        reading.Children.Add(progress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var save = ActionButton("Save progress", async (_, _) =>
        {
            if (double.IsNaN(progress.Value) || progress.Value != Math.Truncate(progress.Value)) { ShowNotice("Enter a whole page number.", true); return; }
            await SaveAndShowAsync(book with { CurrentPage = (int)progress.Value, ReadingStatus = progress.Value > 0 ? ReadingStatus.Reading : ReadingStatus.Unread, FinishedAt = null }, book);
        });
        var finish = ActionButton(book.ReadingStatus == ReadingStatus.Finished ? "Mark unread" : "Mark finished", async (_, _) =>
            await SaveAndShowAsync(book.ReadingStatus == ReadingStatus.Finished
                ? book with { ReadingStatus = ReadingStatus.Unread, CurrentPage = 0, FinishedAt = null }
                : book with { ReadingStatus = ReadingStatus.Finished, CurrentPage = book.PageCount > 0 ? book.PageCount : book.CurrentPage, FinishedAt = DateTimeOffset.Now }, book));
        save.IsEnabled = finish.IsEnabled = !store.IsReadOnly;
        actions.Children.Add(save); actions.Children.Add(finish); reading.Children.Add(actions);
        if (book.Rating > 0) reading.Children.Add(Text(new string('★', book.Rating) + new string('☆', 5 - book.Rating), 22, "#B97A18"));
        stack.Children.Add(Surface(reading));

        var edition = new StackPanel { Spacing = 12 };
        edition.Children.Add(Text("About this edition", 20, "#17243B", true));
        foreach (var pair in new[] { ("Series", book.Series.DisplayName()), ("Type", book.Kind == BookKind.Comic ? "COMIC" : "Book"), ("Book number", book.BookNumber), ("ISBN", book.Isbn), ("Publisher", book.Publisher), ("Year", book.PublicationYear), ("Edition", book.Edition), ("Added", book.AddedAt.LocalDateTime.ToString("MMMM d, yyyy")) })
            if (!string.IsNullOrWhiteSpace(pair.Item2))
            {
                var line = new Grid { ColumnSpacing = 16 };
                line.ColumnDefinitions.Add(new() { Width = new(110) }); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                line.Children.Add(Text(pair.Item1, 13));
                var value = Text(pair.Item2, 14, "#17243B"); value.IsTextSelectionEnabled = true;
                Grid.SetColumn(value, 1); line.Children.Add(value); edition.Children.Add(line);
            }
        stack.Children.Add(Surface(edition));
        if (!string.IsNullOrWhiteSpace(book.Notes))
        {
            var notes = new StackPanel { Spacing = 12 };
            notes.Children.Add(Text("Notes", 20, "#17243B", true));
            var noteText = Text(book.Notes); noteText.IsTextSelectionEnabled = true;
            notes.Children.Add(noteText); stack.Children.Add(Surface(notes));
        }
        var delete = ActionButton("Remove from library", async (_, _) =>
        {
            if (!await ConfirmAsync("Remove this edition?", $"Remove “{book.Title}” and its reading log from this Uno library?", "Remove")) return;
            try { await store.DeleteAsync(book.Id, book); rows.Remove(book.Id); Navigate(section); ShowNotice("Edition removed."); }
            catch (Exception ex) { ShowNotice(ex.Message, true); }
        });
        delete.IsEnabled = !store.IsReadOnly;
        stack.Children.Add(delete);
        ScrollBody(stack);
    }

    private UIElement CoverImage(Book book, double width, double height)
    {
        var panel = new Grid { Width = width, Height = height, CornerRadius = new(6), Background = Brush("#E4E9EF"), VerticalAlignment = VerticalAlignment.Top };
        panel.Children.Add(new FontIcon { Glyph = "\uE8F1", FontSize = 36, Foreground = Brush("#8B99AA") });
        var row = Row(book);
        var image = new Image { DataContext = row, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        image.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new("Cover") });
        image.Loaded += CoverLoaded;
        panel.Children.Add(image);
        return panel;
    }

    private async Task SaveAndShowAsync(Book book, Book expected)
    {
        try { await store.SaveAsync(book, expected); ShowDetail(book); ShowNotice("Reading log updated."); }
        catch (Exception ex) { ShowNotice(ex.Message, true); }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = Text(message, 14, "#17243B"), PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, RequestedTheme = ElementTheme.Light };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowLookup()
    {
        var stack = new StackPanel { Spacing = 20 };
        var form = new StackPanel { Spacing = 16 };
        form.Children.Add(Text("An ISBN is all you need", 22, "#17243B", true));
        form.Children.Add(Text("Find edition details from Open Library, or see whether you already own a copy."));
        var isbn = new TextBox { Header = "ISBN-10 or ISBN-13", PlaceholderText = "978…", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(isbn, "LookupIsbn");
        form.Children.Add(isbn);
        var feedback = Text("");
        var matchesPanel = new StackPanel { Spacing = 12 };
        Button? find = null;
        find = ActionButton("Look up edition", async (_, _) =>
        {
            matchesPanel.Children.Clear();
            if (!IsbnUtility.IsValid(isbn.Text)) { feedback.Text = "Enter a valid ISBN-10 or ISBN-13."; return; }
            var matches = CatalogMatcher.ExactMatches(isbn.Text, store.Books);
            if (matches.Count > 0)
            {
                feedback.Text = $"{matches.Count} matching edition(s) already in your library.";
                foreach (var match in matches)
                    matchesPanel.Children.Add(ActionButton($"{match.Title} · {match.Format.DisplayName()} · {match.Ownership.DisplayName()}", (_, _) => ShowDetail(match)));
                matchesPanel.Children.Add(ActionButton("Add a separate copy", async (_, _) => await EditBookAsync(matches[0] with { Id = Guid.NewGuid(), AddedAt = DateTimeOffset.Now, CurrentPage = 0, ReadingStatus = ReadingStatus.Unread, FinishedAt = null, Rating = 0 }, true)));
                return;
            }
            find!.IsEnabled = false;
            feedback.Text = "Searching Open Library…";
            try
            {
                var found = await lookup.FindAsync(isbn.Text);
                feedback.Text = $"Found: {found.Title} · {found.Authors}. Review the metadata before saving.";
                var editions = CatalogMatcher.PossibleOtherEditions(found, store.Books);
                if (editions.Count > 0) feedback.Text += $" You also have {editions.Count} possible other edition(s).";
                var add = ActionButton("Review and add", async (_, _) => await EditBookAsync(found, true), true);
                add.IsEnabled = !store.IsReadOnly; matchesPanel.Children.Add(add);
            }
            catch (Exception ex)
            {
                feedback.Text = ex.Message;
                matchesPanel.Children.Add(ActionButton("Enter details manually", async (_, _) => await EditBookAsync(new Book { Isbn = IsbnUtility.Normalize(isbn.Text) }, true)));
            }
            finally { find.IsEnabled = true; }
        }, true);
        form.Children.Add(find); form.Children.Add(feedback); form.Children.Add(matchesPanel);
        stack.Children.Add(Surface(form));
        stack.Children.Add(Text("Open Library is a general book database. Review the title, series, format and cover before adding an edition. ISBNs are sent only when you request a lookup."));
        ScrollBody(stack);
    }

    private void ShowInsights()
    {
        var all = store.Books;
        var owned = all.Where(b => b.Ownership == OwnershipStatus.Owned).ToArray();
        var stack = new StackPanel { Spacing = 20 };
        var stats = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        stats.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); stats.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        stats.RowDefinitions.Add(new()); stats.RowDefinitions.Add(new());
        var values = new[] { ("ON YOUR SHELVES", owned.Length, "Books and comics, all editions"), ("STORIES FINISHED", owned.Count(b => b.ReadingStatus == ReadingStatus.Finished), "Every finished edition counts"), ("READING NOW", owned.Count(b => b.ReadingStatus == ReadingStatus.Reading), "Your current adventures"), ("ON THE WISHLIST", all.Count(b => b.Ownership == OwnershipStatus.Wishlist), "Something to look forward to") };
        for (int i = 0; i < values.Length; i++)
        {
            var stat = new StackPanel { Spacing = 8 };
            stat.Children.Add(Text(values[i].Item1, 10, "#147D83", true)); stat.Children.Add(Text(values[i].Item2.ToString("N0"), 36, "#17243B", true)); stat.Children.Add(Text(values[i].Item3, 12));
            var card = Surface(stat); Grid.SetColumn(card, i % 2); Grid.SetRow(card, i / 2); stats.Children.Add(card);
        }
        stack.Children.Add(stats);
        var series = new StackPanel { Spacing = 16 };
        series.Children.Add(Text("Explore your universe", 22, "#17243B", true));
        series.Children.Add(Text($"{owned.Count(b => b.Kind == BookKind.Book)} books  ·  {owned.Count(b => b.Kind == BookKind.Comic)} comics  ·  {owned.Count(b => b.Format == BookFormat.Ebook)} e-books"));
        foreach (var group in owned.GroupBy(b => b.Series).OrderByDescending(g => g.Count()))
        {
            var row = new StackPanel { Spacing = 7 };
            row.Children.Add(Text($"{group.Key.DisplayName()}  ·  {group.Count()} editions / {group.Count(b => b.ReadingStatus == ReadingStatus.Finished)} finished", 13, "#17243B"));
            row.Children.Add(new ProgressBar { Value = group.Count(), Maximum = Math.Max(1, owned.Length), Height = 5, Foreground = Brush("#147D83") });
            series.Children.Add(row);
        }
        stack.Children.Add(Surface(series));
        var finished = new StackPanel { Spacing = 12 };
        finished.Children.Add(Text("Finished stories", 22, "#17243B", true));
        foreach (var book in owned.Where(b => b.ReadingStatus == ReadingStatus.Finished).OrderByDescending(b => b.FinishedAt))
        {
            var button = ActionButton($"{book.Title}  ·  {(book.FinishedAt?.LocalDateTime.ToString("MMM d, yyyy") ?? "Date not recorded")}", (_, _) => ShowDetail(book));
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Content = Text((string)button.Content, 14, "#17243B"); finished.Children.Add(button);
        }
        if (!owned.Any(b => b.ReadingStatus == ReadingStatus.Finished)) finished.Children.Add(Text("Your completed books will appear here."));
        stack.Children.Add(Surface(finished));
        ScrollBody(stack);
    }

    private void ShowSettings()
    {
        var stack = new StackPanel { Spacing = 20 };
        var backup = new StackPanel { Spacing = 14 };
        backup.Children.Add(Text("Keep your library safe", 22, "#17243B", true));
        backup.Children.Add(Text("Export a JSON backup of every edition, note and reading log. Backups are compatible with the MAUI app. Image files are stored separately; JSON includes cover URLs, but does not embed local artwork."));
        var export = ActionButton("Export catalog backup", async (_, _) => await ExportAsync(), true);
        export.IsEnabled = !store.IsReadOnly; backup.Children.Add(export);
        var restore = ActionButton("Restore from backup", async (_, _) => await RestoreAsync()); restore.IsEnabled = !store.IsReadOnly; backup.Children.Add(restore);
        backup.Children.Add(Text("Restoring replaces this Uno catalog after confirmation. A copy of the current catalog is saved in the Backups folder first.", 12));
        stack.Children.Add(Surface(backup));
        stack.Children.Add(Surface(SyncSettings()));
        var local = new StackPanel { Spacing = 14 };
        local.Children.Add(Text("A library of your own", 22, "#17243B", true));
        local.Children.Add(Text($"{store.Books.Count} editions stored on this PC. Private sync connects this catalog to the Uno mobile companion. Your MAUI library and Calibre files remain independent."));
        var path = Text(store.Root, 12); path.IsTextSelectionEnabled = true; local.Children.Add(path);
        local.Children.Add(ActionButton("Open library folder", async (_, _) =>
        {
            try { await Windows.System.Launcher.LaunchFolderPathAsync(store.Root); }
            catch (Exception ex) { ShowNotice(ex.Message, true); }
        }));
        stack.Children.Add(Surface(local));
        var about = new StackPanel { Spacing = 12 };
        about.Children.Add(Text("Starfleet Library", 22, "#17243B", true));
        about.Children.Add(Text("Windows edition · Built with Uno Platform and WinUI"));
        about.Children.Add(Text("A local copy stays on this device. When private sync is connected, editions and cover images are shared with your Uno mobile companion."));
        stack.Children.Add(Surface(about));
        var clear = ActionButton("Clear this Uno catalog", async (_, _) =>
        {
            if (!await ConfirmAsync("Clear this library?", "Remove every edition from the Uno catalog? Your MAUI and Calibre libraries remain untouched. A catalog backup will be saved first.", "Clear catalog")) return;
            try { await store.ReplaceAsync([]); rows.Clear(); Navigate("Collection"); ShowNotice("Catalog cleared. The previous catalog is in your Backups folder."); }
            catch (Exception ex) { ShowNotice(ex.Message, true); }
        });
        clear.IsEnabled = !store.IsReadOnly; stack.Children.Add(clear);
        ScrollBody(stack);
    }

    private async Task ExportAsync()
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"Starfleet-Library-{DateTime.Now:yyyy-MM-dd}", SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("Library backup", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await File.WriteAllBytesAsync(file.Path, CatalogBackup.Encode(store.Books));
            ShowNotice($"Exported {store.Books.Count} editions to {file.Name}.");
        }
        catch (Exception ex) { ShowNotice("Backup failed. " + ex.Message, true); }
    }

    private async Task RestoreAsync()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var restored = await new CatalogRepository(file.Path).LoadAsync();
            if (!await ConfirmAsync("Restore this catalog?", $"Replace {store.Books.Count} editions with {restored.Count} editions from {file.Name}? A backup of this catalog will be saved first.", "Restore")) return;
            await store.ReplaceAsync(restored);
            rows.Clear(); Navigate("Collection"); ShowNotice($"Restored {restored.Count} editions.");
        }
        catch (Exception ex) { ShowNotice("Restore failed. " + ex.Message, true); }
    }
}
