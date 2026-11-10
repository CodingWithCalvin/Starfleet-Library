using StarfleetLibrary.Core;

namespace StarfleetLibrary.Mobile;

public sealed partial class MainPage
{
    private bool scanning;
    private async void ScanClicked(object sender,RoutedEventArgs e)
    {
        if(!await LeaveEditor())return;
        if(scanning)return;
        scanning=true;
        try
        {
            var value=await Services.NativeScanner.ScanAsync();
            if(string.IsNullOrWhiteSpace(value))return;
            if(value=="manual"){ShowEditor(new Book { Ownership=shelf },true);return;}
            await ShowScannedAsync(value);
        }
        catch(Exception ex){await Message(ex.Message);ShowEditor(new Book { Ownership=shelf },true);}
        finally{scanning=false;}
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
