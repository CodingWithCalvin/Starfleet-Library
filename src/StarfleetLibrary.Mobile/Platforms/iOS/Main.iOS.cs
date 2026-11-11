using UIKit;
using Uno.UI.Hosting;

namespace StarfleetLibrary.Mobile.iOS;

public class EntryPoint
{
    // This is the main entry point of the application.
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(CreateApplication)
            .UseAppleUIKit()
            .Build();

        host.Run();
    }

    // Uno's native UIKit host reads the factory method's return type to select
    // the UIApplicationDelegate. A lambda inferred as Func<Application> loses
    // the concrete App type and starts the base delegate without our UI.
    private static App CreateApplication() => new();
}
