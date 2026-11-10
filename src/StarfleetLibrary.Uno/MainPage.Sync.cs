using StarfleetLibrary.Uno.Services;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Uno;

public sealed partial class MainPage
{
    private readonly DispatcherTimer syncTimer = new() { Interval=TimeSpan.FromSeconds(45) };
    private bool syncRunning;
    private string syncMessage = "Private sync is not connected on this PC.";
    private TextBlock? syncStatus;

    private void InitializeSync()
    {
        syncTimer.Tick+=async (_,_)=>await SyncLibraryAsync(false);
        syncTimer.Start();
        store.Changed+=async ()=>await SyncLibraryAsync(false);
        _=SyncLibraryAsync(false);
    }
    private async Task SyncLibraryAsync(bool review)
    {
        if(syncRunning || store.IsReadOnly)return;
        syncRunning=true;
        try
        {
            using var client=await SyncAccess.OpenAsync();
            if(client is null)return;
            syncMessage="Syncing Collection and Wishlist…";UpdateSyncStatus();
            await store.SynchronizeAsync(client,covers);
            syncMessage=store.SyncConflicts.Length>0 ? $"{store.SyncConflicts.Length} editions need review. Click Sync now to choose which changes to keep." : $"Synced {DateTime.Now:t} · Your phone and desktop share this catalog.";
            if(review)
            {
                foreach(var conflict in store.SyncConflicts.ToArray())
                {
                    var local=conflict.Local is {} l ? $"{l.Title} · {l.Format.DisplayName()} · {l.Ownership.DisplayName()}\n{l.Notes}" : "Removed on this PC";
                    var remote=conflict.Remote.Book is {} r ? $"{r.Title} · {r.Format.DisplayName()} · {r.Ownership.DisplayName()}\n{r.Notes}" : "Removed on another device";
                    var result=await new ContentDialog { XamlRoot=XamlRoot,Title="Both devices changed this edition",Content=Text($"THIS PC\n{local}\n\nOTHER DEVICE\n{remote}"),PrimaryButtonText="Keep PC version",SecondaryButtonText="Use other version",CloseButtonText="Later" }.ShowAsync();
                    if(result==ContentDialogResult.None)break;
                    await store.ResolveConflictAsync(conflict,result==ContentDialogResult.Primary);
                }
                await store.SynchronizeAsync(client,covers);
                syncMessage=store.SyncConflicts.Length>0 ? "Some editions still need review." : $"Synced {DateTime.Now:t} · All changes saved.";
            }
            rows.Clear();
            if(!detailOpen && section is "Collection" or "Wishlist" or "Reading now")RefreshResults();
        }
        catch(Exception)
        {
            syncMessage="Sync unavailable. Your changes are saved on this PC and will retry automatically.";
            if(review)ShowNotice(syncMessage,true);
        }
        finally{syncRunning=false;UpdateSyncStatus();}
    }
    private void UpdateSyncStatus(){if(syncStatus is not null)syncStatus.Text=syncMessage;}
    private StackPanel SyncSettings()
    {
        var panel=new StackPanel { Spacing=14 };
        panel.Children.Add(Text("Your library, wherever you explore",22,"#17243B",true));
        panel.Children.Add(Text("Your catalog and covers are encrypted on your devices before syncing to your Cloudflare account. Local copies keep your books available offline."));
        syncStatus=Text(syncMessage);panel.Children.Add(syncStatus);
        panel.Children.Add(ActionButton("Sync now",async (_,_)=>await SyncLibraryAsync(true),true));
        panel.Children.Add(Text("Pair your native iPhone app with the same private vault. Connection details stay protected on each device."));
        panel.Children.Add(ActionButton("Show iPhone connection code",async (_,_)=>
        {
            var settings=await SyncAccess.ReadAsync();
            if(settings is null){ShowNotice("Connect your private Cloudflare service first.");return;}
            var json=System.Text.Json.JsonSerializer.Serialize(settings,StarfleetLibrary.Sync.PrivateJson.Context.PrivateSyncSettings);
            var code="starfleet-v1:"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            var field=new TextBox { Text=code,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxWidth=420,Header="Keep this code private. It unlocks your library." };
            await new ContentDialog { XamlRoot=XamlRoot,Title="Pair your iPhone",Content=field,CloseButtonText="Done" }.ShowAsync();
        }));
        return panel;
    }
}
