namespace StarfleetLibrary.Mobile.Services;
public static class NativeScanner
{
    public static Task<string?> ScanAsync()
    {
#if __IOS__
        return Platforms.iOS.BarcodeScanner.ScanAsync();
#else
        return Task.FromResult<string?>("manual");
#endif
    }
}
