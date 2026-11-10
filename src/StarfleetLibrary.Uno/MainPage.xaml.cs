using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using StarfleetLibrary.Core;
using StarfleetLibrary.Uno.Services;
using Microsoft.UI.Text;

namespace StarfleetLibrary.Uno;

public sealed partial class MainPage : Page
{
    private readonly UnoLibraryStore store = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly CoverImageCache covers;
    private readonly ISBNLookup lookup;
    private readonly List<(Button Button, TextBlock Label, string Key)> navigation = [];
    private readonly Dictionary<Guid, BookRow> rows = [];
    private string section = "Collection";
    private string search = "";
    private int seriesFilter, formatFilter, statusFilter, sort;
    private bool ready, detailOpen;
    private Grid? results;
    private TextBlock? resultCount;
    private TextBox? searchBox;
    private Button? filterButton;
    private ComboBox? layoutPicker;
    private readonly List<ToggleButton> typeButtons = [];
    private ListViewBase? itemView;
    private Grid? tableGrid;
    private readonly SemaphoreSlim preferenceGate = new(1, 1);

    public MainPage()
    {
        InitializeComponent();
        covers = new CoverImageCache(http, store.CacheDirectory, store.CoverDirectory);
        lookup = new ISBNLookup(http);
        AddNavigation("Collection", "\uE8F1");
        AddNavigation("Reading now", "\uE736");
        AddNavigation("Wishlist", "\uEB51");
        NavItems.Children.Add(new Border { Height = 1, Background = Brush("#2A3A50"), Margin = new(10, 20, 10, 20) });
        AddNavigation("Find by ISBN", "\uE721");
        AddNavigation("Insights", "\uE9D9");
        SizeChanged += (_, _) => Responsive();
        Loaded += async (_, _) =>
        {
            if (ready) return;
            await store.InitializeAsync();
            ready = true;
            AddBookButton.IsEnabled = !store.IsReadOnly;
            Navigate("Collection");
            if (store.LoadError is not null) ShowNotice(store.LoadError, true);
            else if (store.ImportMessage is not null) ShowNotice(store.ImportMessage);
            InitializeSync();
        };
    }

    private void AddNavigation(string name, string glyph)
    {
        var label = Text(name, 14, "#E7EDF7");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18 });
        content.Children.Add(label);
        var button = new Button { Content = content, Style = (Style)Resources["NavButton"] };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => Navigate(name);
        navigation.Add((button, label, name));
        NavItems.Children.Add(button);
    }

    private void Responsive()
    {
        var compact = ActualWidth < 1000;
        SidebarColumn.Width = new(compact ? 80 : 224);
        BrandLogo.Width = BrandLogo.Height = compact ? 48 : 72;
        BrandWords.Visibility = SidebarFooter.Visibility = SettingsWords.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var nav in navigation) nav.Label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Workspace.Margin = new(ActualWidth < 760 ? 16 : 32, 24, ActualWidth < 760 ? 16 : 32, 20);
        PageTitle.FontSize = ActualWidth < 760 ? 26 : 32;
        if (tableGrid is not null && results is not null) tableGrid.Width = Math.Max(1080, results.ActualWidth - 4);
    }

    private void Navigate(string destination)
    {
        if (!ready) return;
        section = destination;
        detailOpen = false;
        Notice.IsOpen = false;
        foreach (var nav in navigation) nav.Button.Background = Brush(nav.Key == section ? "#293C55" : "#00101D32");
        SettingsNav.Background = Brush(section == "Settings" ? "#293C55" : "#00101D32");
        PageTitle.Text = section switch { "Collection" => "Your collection", "Reading now" => "On your nightstand", "Wishlist" => "The next adventure", "Find by ISBN" => "Find your next story", "Insights" => "Your reading universe", _ => "Make yourself at home" };
        PageSubtitle.Text = section switch { "Collection" => "Every edition. Every adventure.", "Reading now" => "Pick up where you left off.", "Wishlist" => "A little space for the stories still to come.", "Find by ISBN" => "Check your shelves before adding another edition.", "Insights" => "A closer look at the stories on your shelves.", _ => "Your library, stored locally and under your control." };
        Body.Children.Clear();
        itemView = null;
        tableGrid = null;
        switch (section)
        {
            case "Settings": ShowSettings(); break;
            case "Insights": ShowInsights(); break;
            case "Find by ISBN": ShowLookup(); break;
            default: ShowCatalog(); break;
        }
        Responsive();
        if (store.LoadError is not null) ShowNotice(store.LoadError, true);
    }

    private void SettingsClicked(object sender, RoutedEventArgs e) => Navigate("Settings");
    private async void AddClicked(object sender, RoutedEventArgs e) => await EditBookAsync(new Book { Ownership = section == "Wishlist" ? OwnershipStatus.Wishlist : OwnershipStatus.Owned }, true);
    private async void CoverLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image { DataContext: BookRow row })
        {
            try { await row.LoadCoverAsync(); }
            catch (Exception ex) when (ex is IOException or UriFormatException or ArgumentException) { /* Artwork is optional. */ }
        }
    }

    private async void CoverContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        // Virtualized tiles keep their Image control when assigned to another book.
        // Loaded only runs when the control enters the tree, not for each new book.
        if (args.NewValue is BookRow row)
        {
            try { await row.LoadCoverAsync(); }
            catch (Exception ex) when (ex is IOException or UriFormatException or ArgumentException) { /* Artwork is optional. */ }
        }
    }

    private void ShowCatalog()
    {
        var grid = new Grid { RowSpacing = 16 };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) }) grid.RowDefinitions.Add(new() { Height = height });
        var searchRow = new Grid { ColumnSpacing = 12 };
        searchRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchBox = new TextBox { PlaceholderText = "Search titles, authors or ISBNs", Text = search, CornerRadius = new(8), Padding = new(14, 10, 14, 10) };
        AutomationProperties.SetAutomationId(searchBox, "CatalogSearch");
        AutomationProperties.SetName(searchBox, "Search library");
        searchBox.TextChanged += (_, _) => { search = searchBox.Text; RefreshResults(); };
        searchRow.Children.Add(searchBox);
        filterButton = ActionButton("Filters", (_, _) => { });
        filterButton.Flyout = BuildFilters();
        Grid.SetColumn(filterButton, 1);
        searchRow.Children.Add(filterButton);
        grid.Children.Add(searchRow);

        var options = new Grid { ColumnSpacing = 8 };
        options.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        options.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var types = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        typeButtons.Clear();
        foreach (var name in new[] { "All", "Books", "Comics" })
        {
            var index = typeButtons.Count;
            var toggle = new ToggleButton { Content = name, IsChecked = store.Preferences.Type == index, Padding = new(12, 8, 12, 8), CornerRadius = new(6) };
            AutomationProperties.SetName(toggle, name == "All" ? "All types" : name + " only");
            toggle.Click += async (_, _) =>
            {
                store.Preferences.Type = index;
                for (int i = 0; i < typeButtons.Count; i++) typeButtons[i].IsChecked = i == index;
                RefreshResults();
                await SavePreferencesAsync();
            };
            typeButtons.Add(toggle);
            types.Children.Add(toggle);
        }
        options.Children.Add(types);
        layoutPicker = Picker("LayoutPicker", ["Table", "Wide cards", "Covers"], store.Preferences.Layout);
        layoutPicker.Width = 130;
        AutomationProperties.SetName(layoutPicker, "Layout");
        layoutPicker.SelectionChanged += async (_, _) => { store.Preferences.Layout = layoutPicker.SelectedIndex; CreateItemView(); RefreshResults(); await SavePreferencesAsync(); };
        Grid.SetColumn(layoutPicker, 1);
        options.Children.Add(layoutPicker);
        Grid.SetRow(options, 1);
        grid.Children.Add(options);
        resultCount = Text("", 12);
        Grid.SetRow(resultCount, 2);
        grid.Children.Add(resultCount);
        results = new Grid();
        Grid.SetRow(results, 3);
        grid.Children.Add(results);
        Body.Children.Add(grid);
        CreateItemView();
        RefreshResults();
    }

    private Flyout BuildFilters()
    {
        var content = new StackPanel { Spacing = 14, Width = 260 };
        content.Children.Add(Text("Refine your shelf", 20, "#17243B", true));
        var series = Picker("SeriesFilter", new[] { "All series" }.Concat(Enum.GetValues<TrekSeries>().Select(v => v.DisplayName())), seriesFilter, "Series");
        var format = Picker("FormatFilter", new[] { "All formats" }.Concat(Enum.GetValues<BookFormat>().Select(v => v.DisplayName())), formatFilter, "Format");
        var status = Picker("StatusFilter", new[] { "All reading statuses" }.Concat(Enum.GetValues<ReadingStatus>().Select(v => v.DisplayName())), statusFilter, "Reading status");
        var order = Picker("SortPicker", ["Title A–Z", "Author A–Z", "Newest added", "Publication year"], sort, "Sort by");
        foreach (var picker in new[] { series, format, status, order }) content.Children.Add(picker);
        series.SelectionChanged += (_, _) => { seriesFilter = series.SelectedIndex; RefreshResults(); };
        format.SelectionChanged += (_, _) => { formatFilter = format.SelectedIndex; RefreshResults(); };
        status.SelectionChanged += (_, _) => { statusFilter = status.SelectedIndex; RefreshResults(); };
        order.SelectionChanged += (_, _) => { sort = order.SelectedIndex; RefreshResults(); };
        content.Children.Add(ActionButton("Reset filters", (_, _) => { series.SelectedIndex = format.SelectedIndex = status.SelectedIndex = order.SelectedIndex = 0; }));
        return new Flyout { Content = content, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
    }

    private void CreateItemView()
    {
        if (results is null) return;
        results.Children.Clear();
        tableGrid = null;
        var layout = store.Preferences.Layout;
        itemView = layout == 2 ? new GridView() : new ListView();
        itemView.IsItemClickEnabled = true;
        itemView.SelectionMode = ListViewSelectionMode.None;
        itemView.ItemTemplate = (DataTemplate)Resources[layout switch { 0 => "TableTemplate", 1 => "CardTemplate", _ => "CoverTemplate" }];
        var style = new Style(layout == 2 ? typeof(GridViewItem) : typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, layout == 2 ? new Thickness(0, 0, 18, 22) : layout == 1 ? new Thickness(0, 0, 0, 10) : new Thickness(0)));
        itemView.ItemContainerStyle = style;
        itemView.ItemClick += (_, e) => ShowDetail(((BookRow)e.ClickedItem).Book);
        AutomationProperties.SetAutomationId(itemView, "BookResults");
        if (layout == 0)
        {
            tableGrid = new Grid { Background = Brush("#FFFFFF"), CornerRadius = new(10), Width = Math.Max(1080, results.ActualWidth - 4) };
            tableGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            tableGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            var header = new Grid { Padding = new(16, 14, 16, 14), ColumnSpacing = 16, Background = Brush("#E9EEF4") };
            var widths = new double[] { 0, 190, 80, 65, 90, 50, 90 };
            var names = new[] { "TITLE", "AUTHOR", "SERIES", "TYPE", "FORMAT", "YEAR", "READING" };
            for (int i = 0; i < names.Length; i++)
            {
                header.ColumnDefinitions.Add(new() { Width = i == 0 ? new(1, GridUnitType.Star) : new(widths[i]) });
                var text = Text(names[i], 10, "#617087", true);
                Grid.SetColumn(text, i); header.Children.Add(text);
            }
            tableGrid.Children.Add(header);
            Grid.SetRow(itemView, 1); tableGrid.Children.Add(itemView);
            results.Children.Add(new ScrollViewer { Content = tableGrid, HorizontalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
            results.SizeChanged += (_, _) => Responsive();
        }
        else results.Children.Add(itemView);
    }

    private void RefreshResults()
    {
        if (itemView is null || detailOpen) return;
        IEnumerable<Book> selection = store.Books.Where(b => section == "Wishlist" ? b.Ownership == OwnershipStatus.Wishlist : b.Ownership == OwnershipStatus.Owned);
        if (section == "Reading now") selection = selection.Where(b => b.ReadingStatus == ReadingStatus.Reading);
        var total = selection.Count();
        if (store.Preferences.Type != 0) selection = selection.Where(b => b.Kind == (store.Preferences.Type == 1 ? BookKind.Book : BookKind.Comic));
        if (seriesFilter > 0) selection = selection.Where(b => b.Series == Enum.GetValues<TrekSeries>()[seriesFilter - 1]);
        if (formatFilter > 0) selection = selection.Where(b => b.Format == Enum.GetValues<BookFormat>()[formatFilter - 1]);
        if (statusFilter > 0) selection = selection.Where(b => b.ReadingStatus == Enum.GetValues<ReadingStatus>()[statusFilter - 1]);
        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length > 0) selection = selection.Where(b => terms.All(term => $"{b.Title} {b.Authors} {b.Isbn} {b.Series.DisplayName()} {b.CatalogLabel()}".Contains(term, StringComparison.OrdinalIgnoreCase)));
        selection = sort switch { 1 => selection.OrderBy(b => b.Authors).ThenBy(b => b.Title), 2 => selection.OrderByDescending(b => b.AddedAt), 3 => selection.OrderByDescending(b => b.PublicationYear).ThenBy(b => b.Title), _ => selection.OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase) };
        var visible = selection.Select(Row).ToArray();
        itemView.ItemsSource = visible;
        resultCount!.Text = $"{visible.Length:N0} shown  /  {total:N0} titles";
        var active = new[] { seriesFilter, formatFilter, statusFilter }.Count(i => i > 0);
        filterButton!.Content = active > 0 ? $"Filters · {active}" : "Filters";
        if (results!.Children.LastOrDefault() is StackPanel empty) results.Children.Remove(empty);
        if (visible.Length == 0)
        {
            var emptyState = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320 };
            emptyState.Children.Add(new FontIcon { Glyph = "\uE8F1", FontSize = 40, Foreground = Brush("#92A5B7"), Margin = new(0, 0, 0, 8) });
            emptyState.Children.Add(Text(total == 0 ? "A new chapter starts here" : "No titles on this shelf", 20, "#17243B", true));
            emptyState.Children.Add(Text(total == 0 ? "Add a title, or change its reading status to see it here." : "Try a different search or clear your filters.", 14));
            emptyState.Children.Add(ActionButton("Clear search and filters", (_, _) => { search = ""; seriesFilter = formatFilter = statusFilter = 0; store.Preferences.Type = 0; Navigate(section); }));
            results.Children.Add(emptyState);
        }
    }

    private BookRow Row(Book book)
    {
        if (!rows.TryGetValue(book.Id, out var row) || row.Book != book) rows[book.Id] = row = new(book, covers);
        return row;
    }

    private async Task SavePreferencesAsync()
    {
        await preferenceGate.WaitAsync();
        try { await store.SavePreferencesAsync(); }
        catch (Exception ex) { ShowNotice("Your view preference could not be saved. " + ex.Message, true); }
        finally { preferenceGate.Release(); }
    }

    private void ShowNotice(string message, bool error = false)
    {
        Notice.Message = message;
        Notice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        Notice.IsOpen = true;
    }

    private static SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#');
        var alpha = hex.Length == 8 ? Convert.ToByte(hex[..2], 16) : (byte)255;
        if (hex.Length == 8) hex = hex[2..];
        return new(Windows.UI.Color.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
    }
    private static TextBlock Text(string text, double size = 14, string color = "#617087", bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    private static Button ActionButton(string label, RoutedEventHandler handler, bool accent = false)
    {
        var button = new Button { Content = label, Padding = new(16, 10, 16, 10), CornerRadius = new(7) };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += handler;
        return button;
    }
    private static ComboBox Picker(string id, IEnumerable<string> options, int selected, string? header = null)
    {
        var picker = new ComboBox { ItemsSource = options.ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        if (header is not null) picker.Header = header;
        AutomationProperties.SetAutomationId(picker, id);
        return picker;
    }
    private static Border Surface(UIElement content, double padding = 24) => new() { Child = content, Background = Brush("#FFFFFF"), Padding = new(padding), CornerRadius = new(12), BorderBrush = Brush("#E3E8EF"), BorderThickness = new(1) };
    private void ScrollBody(UIElement content) => Body.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch });
}
