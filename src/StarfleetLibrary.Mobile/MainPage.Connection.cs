namespace StarfleetLibrary.Mobile;

public sealed partial class MainPage
{
    private async Task PairLibraryAsync()
    {
        var input=new PasswordBox { Header="Private connection code",PlaceholderText="Paste the code from your Windows app",MinWidth=260 };
        var status=Txt("Connects directly to your private Cloudflare library.",14);
        var panel=new StackPanel { Spacing=16 };panel.Children.Add(status);panel.Children.Add(input);
        var scroll=new ScrollViewer { Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        global::Uno.Toolkit.UI.SafeArea.SetInsets(scroll,global::Uno.Toolkit.UI.SafeArea.InsetMask.SoftInput);
        var dialog=new ContentDialog { XamlRoot=XamlRoot,Title="Pair your library",Content=scroll,PrimaryButtonText="Connect",CloseButtonText="Cancel" };
        dialog.PrimaryButtonClick+=async (_,args)=>
        {
            args.Cancel=true;var deferral=args.GetDeferral();
            try
            {
                var settings=Services.PrivateConnection.Parse(input.Password);
                using var client=settings.CreateClient();
                await new StarfleetLibrary.Sync.SyncClient(client).GetAsync();
                await Services.PrivateConnection.SaveAsync(settings);args.Cancel=false;
            }
            catch(Exception){status.Text="Couldn't connect. Check the code and internet connection, then try again.";}
            finally{deferral.Complete();}
        };
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)await SyncAsync();
    }
}
