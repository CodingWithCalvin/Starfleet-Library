using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using StarfleetLibrary.Core;
using StarfleetLibrary.Sync;
using StarfleetLibrary.Uno.Services;

namespace StarfleetLibrary.Mobile;

public sealed partial class MainPage : Page
{
    private readonly MobileStore store;
    private readonly HttpClient http;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(45) };
    private OwnershipStatus shelf;
    private string query = "";
    private int type = 1, format;
    private bool covers, syncing, editing, detail;
    private ListViewBase? list;
    private TextBlock? count;
    private TextBlock? empty;

    public MainPage()
    {
        InitializeComponent();
        store = new();
        var origin = SyncClient.HostedUrl;
#if __WASM__
        origin = global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.location.origin + '/' ");
#endif
        http = new() { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(25) };
        Loaded += async (_,_) => { ShowShelf(); await SyncAsync(); timer.Start(); };
        timer.Tick += async (_,_) => { if (!editing && scanPoll is null) await SyncAsync(); };
    }

    private async Task SyncAsync()
    {
        if (syncing || store.LoadError is not null) { if (store.LoadError is not null) SyncStatus.Content = store.LoadError; return; }
        syncing = true;
        SyncStatus.Content = "Syncing your library…";
        try
        {
            await store.SyncAsync(http);
#if __WASM__
            var paths=store.Books.Select(b=>store.Cover(b.Id)).Where(x=>x is not null).Distinct().Select(x=>"/api/covers/"+x).ToArray();
            var serializedPaths="["+string.Join(",",paths.Select(x=>"\""+x+"\""))+"]";
            global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.starfleetOffline?.warm("+serializedPaths+")");
#endif
            SyncStatus.Content = store.Conflicts.Length > 0 ? $"{store.Conflicts.Length} changes need review · Tap here" : $"Synced {DateTime.Now:t} · Catalog saved here";
            if (!editing && !detail && scanPoll is null) Refresh();
        }
        catch { SyncStatus.Content = store.LastSync is not null ? "Offline · Changes saved here · Tap to retry" : "Connect to load your library · Tap to retry"; }
        finally { syncing = false; }
    }
    private async void SyncClicked(object sender,RoutedEventArgs e)
    {
        if (store.Conflicts.Length == 0) { await SyncAsync(); return; }
        foreach (var conflict in store.Conflicts.ToArray())
        {
            var local = conflict.Local is { } l ? $"{l.Title} · {l.Ownership.DisplayName()} · {l.Format.DisplayName()}\n{l.Notes}" : "Removed on this phone";
            var remote = conflict.Remote.Book is { } r ? $"{r.Title} · {r.Ownership.DisplayName()} · {r.Format.DisplayName()}\n{r.Notes}" : "Removed on another device";
            var dialog = new ContentDialog { XamlRoot=XamlRoot,Title="Both devices changed this edition",Content=Txt($"THIS PHONE\n{local}\n\nOTHER DEVICE\n{remote}"),PrimaryButtonText="Keep phone version",SecondaryButtonText="Use other version",CloseButtonText="Later" };
            var answer = await dialog.ShowAsync();
            if (answer == ContentDialogResult.None) return;
            await store.ResolveAsync(conflict,answer == ContentDialogResult.Primary);
        }
        await SyncAsync();
    }
    private async void CollectionClicked(object sender,RoutedEventArgs e) { if (await LeaveEditor()) { shelf=OwnershipStatus.Owned;query="";ShowShelf(); } }
    private async void WishlistClicked(object sender,RoutedEventArgs e) { if (await LeaveEditor()) { shelf=OwnershipStatus.Wishlist;query="";ShowShelf(); } }
    private async void AddClicked(object sender,RoutedEventArgs e) { if (await LeaveEditor()) ShowEditor(new Book { Ownership=shelf },true); }
    private async Task<bool> LeaveEditor() => !editing || await Confirm("Discard this draft?","Your unsaved changes will be discarded.","Discard");

    private void ShowShelf()
    {
        editing=detail=false;
        Heading.Text=shelf == OwnershipStatus.Owned ? "Your collection" : "Your wishlist";
        CollectionNav.Background=Brush(shelf == OwnershipStatus.Owned ? "#E5F1F0" : "#FFFFFF");
        WishlistNav.Background=Brush(shelf == OwnershipStatus.Wishlist ? "#E5F1F0" : "#FFFFFF");
        Body.Children.Clear();Body.RowDefinitions.Clear();
        Body.RowDefinitions.Add(new() { Height=GridLength.Auto });Body.RowDefinitions.Add(new() { Height=new(1,GridUnitType.Star) });
        var controls=new StackPanel { Spacing=12,Margin=new(0,0,0,12) };
        var search=new TextBox { PlaceholderText="Search title, author or ISBN",Text=query,MinHeight=44 };
        AutomationProperties.SetName(search,"Search your library");
        search.TextChanged+=(_,_)=>{query=search.Text;Refresh();};controls.Children.Add(search);
        controls.Children.Add(Btn("Scan ISBN / barcode",ScanClicked));
        var filters=new Grid { ColumnSpacing=8 };
        filters.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});filters.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var types=Pick(["All types","Books","Comics"],type);AutomationProperties.SetName(types,"Book or comic filter");
        types.SelectionChanged+=(_,_)=>{type=types.SelectedIndex;Refresh();};filters.Children.Add(types);
        var formats=Pick(["All formats","Paperback","Hardcover","E-book","Audiobook"],format);AutomationProperties.SetName(formats,"Format filter");
        formats.SelectionChanged+=(_,_)=>{format=formats.SelectedIndex;Refresh();};Grid.SetColumn(formats,1);filters.Children.Add(formats);
        var layout=Btn(covers ? "List" : "Covers",(_,_)=>{covers=!covers;ShowShelf();});Grid.SetColumn(layout,2);filters.Children.Add(layout);controls.Children.Add(filters);
        count=Txt("",12,"#596A7C");controls.Children.Add(count);Body.Children.Add(controls);
        var content=new Grid();Grid.SetRow(content,1);Body.Children.Add(content);
        list=covers ? new GridView() : new ListView();
        list.SelectionMode=ListViewSelectionMode.None;list.IsItemClickEnabled=true;
        list.ItemTemplate=(DataTemplate)Resources[covers ? "ShelfCover" : "ShelfCard"];
        var style=new Style(covers ? typeof(GridViewItem) : typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty,covers ? new Thickness(0,0,10,14) : new Thickness(0,0,0,4)));
        list.ItemContainerStyle=style;
        list.ItemClick+=(_,e)=>ShowDetail(((MobileRow)e.ClickedItem).Book);
        content.Children.Add(list);
        empty=Txt("",18,"#596A7C");empty.Margin=new(10,32,10,0);empty.TextAlignment=TextAlignment.Center;empty.IsHitTestVisible=false;content.Children.Add(empty);
        Refresh();
    }
    private void Refresh()
    {
        if (list is null || editing || detail) return;
        var books=store.Books.Where(b=>b.Ownership==shelf).Where(b=>type==0 || b.Kind==(type==1 ? BookKind.Book : BookKind.Comic))
            .Where(b=>format==0 || (int)b.Format==format-1)
            .Where(b=>query.Split(' ',StringSplitOptions.RemoveEmptyEntries).All(term=>$"{b.Title} {b.Authors} {b.Isbn} {b.Series.DisplayName()} {b.CatalogLabel()}".Contains(term,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(b=>b.Title,StringComparer.OrdinalIgnoreCase).ThenBy(b=>b.Format).ToArray();
        list.ItemsSource=books.Select(b=>new MobileRow(b,CoverUrl(b))).ToArray();
        count!.Text=$"{books.Length} {(books.Length==1 ? "edition" : "editions")} · {(shelf==OwnershipStatus.Owned ? "on your shelves" : "to look out for")}";
        empty!.Text=books.Length==0 ? query.Length>0 || format>0 ? "No matching editions.\nTry another search or filter." : shelf==OwnershipStatus.Wishlist ? "Your next adventure starts here.\nTap + Add to save a book for later." : store.LastSync is null ? "Your library will appear after the first sync." : "No editions in this view.\nTap + Add or change the filters." : "";
    }
    private string? CoverUrl(Book book) => store.Cover(book.Id) is { } hash ? new Uri(http.BaseAddress!,"api/covers/"+hash).AbsoluteUri : BookCover.Source(book)?.AbsoluteUri;
    private void ShowDetail(Book stale)
    {
        var book=store.Books.FirstOrDefault(x=>x.Id==stale.Id) ?? stale;
        Heading.Text="Edition details";
        detail=true;editing=false;
        var panel=PagePanel("Back to "+(shelf==OwnershipStatus.Owned ? "collection" : "wishlist"),(_,_)=>ShowShelf());
        var image=new Image { Height=240,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center };
        if (CoverUrl(book) is { } cover) image.Source=new BitmapImage(new Uri(cover));panel.Children.Add(image);
        panel.Children.Add(Txt(book.CatalogLabel(),12,"#147077",true));panel.Children.Add(Txt(book.Title,26,"#17243B",true));panel.Children.Add(Txt(book.Authors,16));
        panel.Children.Add(Txt($"{book.Format.DisplayName()} · {book.Ownership.DisplayName()}",16,"#147077",true));
        if (book.Ownership==OwnershipStatus.Wishlist) panel.Children.Add(Btn("I bought this · Move to Collection",async (_,_)=>{if(await Save(book with { Ownership=OwnershipStatus.Owned },book)){shelf=OwnershipStatus.Owned;ShowDetail(book with { Ownership=OwnershipStatus.Owned });}},true));
        panel.Children.Add(Btn("Edit edition",(_,_)=>ShowEditor(book,false)));
        panel.Children.Add(Txt(string.Join("\n",new[]{book.Isbn.Length>0 ? "ISBN "+book.Isbn : "",book.Publisher,book.PublicationYear,book.Edition}.Where(x=>x.Length>0))));
        if(book.Notes.Length>0) {panel.Children.Add(Txt("Notes",16,"#17243B",true));panel.Children.Add(Txt(book.Notes));}
        panel.Children.Add(Btn("Remove this edition",async (_,_)=>{if(await Confirm("Remove this edition?",book.Title,"Remove")){try{await store.DeleteAsync(book.Id,book);ShowShelf();await SyncAsync();}catch(Exception ex){await Message(ex.Message);}}}));
    }
    private void ShowEditor(Book original,bool isNew)
    {
        var expected = isNew ? null : original with { };
        editing=true;detail=false;Heading.Text=isNew ? "Add an edition" : "Edit edition";
        var panel=PagePanel("Cancel",async (_,_)=>{if(await LeaveEditor())ShowShelf();});
        var isbn=Field(panel,"ISBN (optional)",original.Isbn);var title=Field(panel,"Title",original.Title);var authors=Field(panel,"Author(s)",original.Authors);
        var series=Pick(Enum.GetValues<TrekSeries>().Select(x=>x.DisplayName()).ToArray(),(int)original.Series);Label(panel,"Series",series);
        var formatPicker=Pick(Enum.GetValues<BookFormat>().Select(x=>x.DisplayName()).ToArray(),(int)original.Format);Label(panel,"Format",formatPicker);
        var kind=Pick(["Book","COMIC"],(int)original.Kind);Label(panel,"Type",kind);
        var ownership=Pick(["Collection","Wishlist"],(int)original.Ownership);Label(panel,"Save to",ownership);
        var notes=Field(panel,"Notes",original.Notes);notes.AcceptsReturn=true;notes.TextWrapping=TextWrapping.Wrap;notes.MinHeight=90;
        var status=Txt("",13,"#9B452C");panel.Children.Add(status);
        var lookup=Btn("Look up ISBN",async (_,_)=>
        {
            try
            {
                var matches=CatalogMatcher.ExactMatches(isbn.Text,store.Books);
                if(matches.Count>0)status.Text="Already in your library: "+string.Join(", ",matches.Select(b=>$"{b.Title} ({b.Format.DisplayName()}, {b.Ownership.DisplayName()})"));
                var found=await new ISBNLookup(http).FindAsync(isbn.Text);
                title.Text=found.Title;authors.Text=found.Authors;series.SelectedIndex=(int)found.Series;
                original=original with { Publisher=found.Publisher,PublicationYear=found.PublicationYear,PageCount=found.PageCount,CoverUrl=found.CoverUrl };
                if(matches.Count==0)status.Text="Details found. Check the edition and format before saving.";
            }
            catch(Exception ex){status.Text=ex.Message;}
        });panel.Children.Insert(1,lookup);
        panel.Children.Add(Btn("Save edition",async (_,_)=>
        {
            var book=original with { Title=title.Text.Trim(),Authors=authors.Text.Trim(),Isbn=IsbnUtility.Normalize(isbn.Text),Series=(TrekSeries)series.SelectedIndex,Format=(BookFormat)formatPicker.SelectedIndex,Kind=(BookKind)kind.SelectedIndex,Ownership=(OwnershipStatus)ownership.SelectedIndex,Notes=notes.Text.Trim() };
            if(book.ValidationError is { } error){status.Text=error;return;}
            var duplicates=CatalogMatcher.ExactMatches(book.Isbn,store.Books).Where(b=>b.Id!=book.Id).ToArray();
            if(duplicates.Length>0 && !await Confirm("Another copy is already saved",string.Join("\n",duplicates.Select(b=>$"{b.Title} · {b.Format.DisplayName()} · {b.Ownership.DisplayName()}")),"Save separate copy"))return;
            try{await store.SaveAsync(book,expected);editing=false;shelf=book.Ownership;ShowDetail(book);await SyncAsync();}catch(Exception ex){status.Text=ex.Message;}
        },true));
    }
    private async Task<bool> Save(Book book,Book expected){try{await store.SaveAsync(book,expected);await SyncAsync();return true;}catch(Exception ex){await Message(ex.Message);return false;}}
    private StackPanel PagePanel(string back,RoutedEventHandler action)
    {
        Body.Children.Clear();Body.RowDefinitions.Clear();var panel=new StackPanel { Spacing=14,Margin=new(0,0,0,24) };panel.Children.Add(Btn("‹  "+back,action));Body.Children.Add(new ScrollViewer { Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });return panel;
    }
    private async Task<bool> Confirm(string title,string text,string action)=>await new ContentDialog { XamlRoot=XamlRoot,Title=title,Content=Txt(text),PrimaryButtonText=action,CloseButtonText="Cancel" }.ShowAsync()==ContentDialogResult.Primary;
    private async Task Message(string text)=>await new ContentDialog { XamlRoot=XamlRoot,Title="Could not save",Content=Txt(text),CloseButtonText="OK" }.ShowAsync();
    private static TextBox Field(StackPanel panel,string name,string value){var box=new TextBox { Text=value,MinHeight=44 };AutomationProperties.SetName(box,name);Label(panel,name,box);return box;}
    private static void Label(StackPanel panel,string name,UIElement control){panel.Children.Add(Txt(name,13,"#526376",true));panel.Children.Add(control);}
    private static ComboBox Pick(string[] values,int selected)=>new(){ItemsSource=values,SelectedIndex=selected,MinHeight=44,MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private static Button Btn(string text,RoutedEventHandler click,bool primary=false){var button=new Button{Content=Txt(text,14,primary ? "#FFFFFF" : "#17243B",true),MinHeight=46,Padding=new(14,10,14,10),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,Background=Brush(primary ? "#147077" : "#FFFFFF"),BorderBrush=Brush("#DEE6EC"),CornerRadius=new(8)};button.Click+=click;return button;}
    private static TextBlock Txt(string value,double size=14,string color="#596A7C",bool bold=false)=>new(){Text=value,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,FontWeight=bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal};
    internal static SolidColorBrush Brush(string color)=>new(Windows.UI.Color.FromArgb(255,Convert.ToByte(color.Substring(1,2),16),Convert.ToByte(color.Substring(3,2),16),Convert.ToByte(color.Substring(5,2),16)));
}

public sealed class MobileRow(Book book,string? cover)
{
    public Book Book=>book;
    public string Title=>book.Title;
    public string Authors=>book.Authors;
    public string Label=>book.CatalogLabel();
    public string Format=>book.Format.DisplayName();
    public string FormatCaps=>Format.ToUpperInvariant();
    public string AccessibleName=>$"{Title}, {Format}, {Label}";
    public ImageSource? Cover {get;}=cover is null ? null : new BitmapImage(new Uri(cover));
    public SolidColorBrush Ribbon=>MainPage.Brush(book.Format switch {BookFormat.Paperback=>"#8C4E22",BookFormat.Hardcover=>"#344B76",BookFormat.Audiobook=>"#6D488F",_=>"#147077"});
}
