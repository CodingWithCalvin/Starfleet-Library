using Uno.Resizetizer;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;
using StarfleetLibrary.Uno.Services;
using System.Security.Cryptography;
using System.Text;

namespace StarfleetLibrary.Uno;

public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;
    private AppInstance? registeredInstance;
    public App() => InitializeComponent();
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One window per catalog prevents two stale snapshots from overwriting each other.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(UnoLibraryStore.DefaultRoot).ToUpperInvariant())));
        var instance = registeredInstance = AppInstance.FindOrRegisterForKey("StarfleetLibrary-" + key);
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();
            return;
        }
        instance.Activated += (_, _) => MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (MainWindow.AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
            MainWindow.Activate();
        });
        MainWindow = new Window { Title = "Starfleet Library", Content = new MainPage() };
        MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 900));
        MainWindow.AppWindow.TitleBar.BackgroundColor = ColorHelper.FromArgb(255, 16, 29, 50);
        MainWindow.AppWindow.TitleBar.ForegroundColor = Colors.White;
        MainWindow.AppWindow.TitleBar.ButtonBackgroundColor = ColorHelper.FromArgb(255, 16, 29, 50);
        MainWindow.AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        MainWindow.SetWindowIcon();
        MainWindow.Activate();
    }
}
