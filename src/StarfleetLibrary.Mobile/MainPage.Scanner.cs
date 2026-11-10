using StarfleetLibrary.Core;

namespace StarfleetLibrary.Mobile;

public sealed partial class MainPage
{
    private DispatcherTimer? scanPoll;
    private async void ScanClicked(object sender,RoutedEventArgs e)
    {
#if __WASM__
        if(!await LeaveEditor())return;
        // The scanner is a browser overlay outside Uno's DOM tree. Disable the
        // underlying page so Uno's document-level pointer routing cannot also
        // open a book beneath a camera/photo button.
        IsEnabled=false;
        global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.starfleetScanner.open()");
        var poll=scanPoll=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
        poll.Tick+=async (_,_)=>
        {
            var value=global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.starfleetScanner.take()");
            if(value=="__waiting__")return;
            poll.Stop();
            scanPoll=null;
            IsEnabled=true;
            if(value.Length==0)return;
            if(value=="manual"){ShowEditor(new Book { Ownership=shelf },true);return;}
            await ShowScannedAsync(value);
        };
        poll.Start();
#else
        await Message("Camera scanning is available in the iPhone Home Screen app. You can also enter an ISBN below.");
        ShowEditor(new Book { Ownership=shelf },true);
#endif
    }
    private async Task ShowScannedAsync(string value)
    {
        var code=IsbnUtility.Normalize(value);
        if(!(code.Length==13 && (code.StartsWith("978") || code.StartsWith("979")) || code.Length==10) || !IsbnUtility.IsValid(code))
        {
            await new ContentDialog { XamlRoot=XamlRoot,Title="Retail barcode scanned",Content=Txt($"Scanned: {value}\n\nThis UPC/EAN barcode is not an ISBN. Look for the book's 978/979 ISBN barcode (sometimes inside the front cover), or search your library by title."),CloseButtonText="Search by title" }.ShowAsync();
            ShowShelf();return;
        }
        var matches=CatalogMatcher.ExactMatches(code,store.Books);
        detail=true;editing=false;Heading.Text="ISBN found";
        var panel=PagePanel("Back to library",(_,_)=>ShowShelf());
        panel.Children.Add(Txt(code,14,"#147077",true));
        if(matches.Count>0)
        {
            panel.Children.Add(Txt(matches.Any(b=>b.Ownership==OwnershipStatus.Owned) ? "Already in your collection" : "On your wishlist",24,"#17243B",true));
            foreach(var book in matches)panel.Children.Add(Btn($"{book.Title}\n{book.Format.DisplayName()} · {book.Ownership.DisplayName()}",(_,_)=>ShowDetail(book)));
        }
        else panel.Children.Add(Txt("This ISBN isn't in your library yet.",22,"#17243B",true));
        panel.Children.Add(Btn(matches.Count>0 ? "Add another edition" : "Look up and add",async (_,_)=>
        {
            var book=new Book { Isbn=code,Ownership=shelf };
            try{var found=await new StarfleetLibrary.Uno.Services.ISBNLookup(http).FindAsync(code);book=found with { Ownership=shelf };}
            catch{ /* ISBN remains usable for a manual, offline entry. */ }
            ShowEditor(book,true);
        },true));
        panel.Children.Add(Btn("Scan another",ScanClicked));
    }
}
