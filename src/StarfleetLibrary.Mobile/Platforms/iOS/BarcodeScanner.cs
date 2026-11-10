using AVFoundation;
using CoreFoundation;
using Foundation;
using UIKit;

namespace StarfleetLibrary.Mobile.Platforms.iOS;

public static class BarcodeScanner
{
    public static async Task<string?> ScanAsync()
    {
        if (!await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video))
            throw new InvalidOperationException("Camera access is off. Enable it in iPhone Settings, or enter the ISBN manually.");
        var window = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .Where(scene => scene.ActivationState == UISceneActivationState.ForegroundActive)
            .SelectMany(scene => scene.Windows).FirstOrDefault(window => window.IsKeyWindow);
        var presenter = window?.RootViewController ?? throw new InvalidOperationException("The scanner could not open.");
        while (presenter.PresentedViewController is { } presented) presenter = presented;
        using var scanner = new ScannerController();
        presenter.PresentViewController(scanner, true, null);
        return await scanner.Result;
    }
    private sealed class ScannerController : UIViewController
    {
        private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AVCaptureSession session = new();
        private readonly DispatchQueue cameraQueue = new("starfleet.camera");
        private AVCaptureVideoPreviewLayer? preview;
        private Reader? reader;
        private NSObject? backgroundObserver;
        private bool finished;
        public Task<string?> Result => completion.Task;
        public ScannerController() { ModalPresentationStyle = UIModalPresentationStyle.FullScreen; }
        public override void ViewDidLoad()
        {
            base.ViewDidLoad(); View!.BackgroundColor = UIColor.Black;
            var hint = new UILabel { Text = "Fit the whole ISBN / UPC barcode inside the camera view", TextColor = UIColor.White, Lines = 2, TextAlignment = UITextAlignment.Center, TranslatesAutoresizingMaskIntoConstraints = false };
            View.AddSubview(hint);
            var manual = Button("Enter ISBN", () => Finish("manual")); var cancel = Button("Cancel", () => Finish(null));
            View.AddSubview(manual); View.AddSubview(cancel);
            NSLayoutConstraint.ActivateConstraints([
                hint.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor, 22), hint.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor, 20), hint.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor, -20),
                manual.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor, -24), manual.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor, 24), manual.HeightAnchor.ConstraintEqualTo(48),
                cancel.BottomAnchor.ConstraintEqualTo(manual.BottomAnchor), cancel.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor, -24), cancel.HeightAnchor.ConstraintEqualTo(48)]);
            backgroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification, _ => Finish(null));
            try
            {
                var camera = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) ?? throw new InvalidOperationException("No camera is available.");
                var input = AVCaptureDeviceInput.FromDevice(camera, out var error);
                if (input is null) throw new InvalidOperationException(error?.LocalizedDescription ?? "Camera unavailable.");
                var output = new AVCaptureMetadataOutput(); session.BeginConfiguration();
                if (!session.CanAddInput(input) || !session.CanAddOutput(output)) throw new InvalidOperationException("Barcode scanning is unavailable.");
                session.AddInput(input); session.AddOutput(output);
                reader = new Reader(value => Finish(value)); output.SetDelegate(reader, DispatchQueue.MainQueue);
                output.MetadataObjectTypes = (AVMetadataObjectType.EAN13Code | AVMetadataObjectType.EAN8Code | AVMetadataObjectType.UPCECode | AVMetadataObjectType.Code128Code | AVMetadataObjectType.QRCode) & output.AvailableMetadataObjectTypes;
                session.CommitConfiguration();
                preview = new AVCaptureVideoPreviewLayer(session) { VideoGravity = AVLayerVideoGravity.ResizeAspectFill };
                View.Layer.InsertSublayer(preview, 0);
                cameraQueue.DispatchAsync(() => { if (!finished) session.StartRunning(); });
            }
            catch (Exception ex) { hint.Text = ex.Message + " Use Enter ISBN below."; }
        }
        private static UIButton Button(string title, Action action)
        {
            var button = UIButton.FromType(UIButtonType.System); button.SetTitle(title, UIControlState.Normal);
            button.SetTitleColor(UIColor.White, UIControlState.Normal); button.BackgroundColor = UIColor.FromRGB(20,112,119);
            button.Layer.CornerRadius = 10; button.TranslatesAutoresizingMaskIntoConstraints = false;
            button.WidthAnchor.ConstraintGreaterThanOrEqualTo(120).Active = true; button.TouchUpInside += (_,_) => action(); return button;
        }
        public override void ViewDidLayoutSubviews() { base.ViewDidLayoutSubviews(); if(preview is not null)preview.Frame=View!.Bounds; }
        private void Finish(string? value)
        {
            if(finished)return;finished=true; cameraQueue.DispatchAsync(() => session.StopRunning());
            DismissViewController(true, () => completion.TrySetResult(value));
        }
        public override void ViewDidDisappear(bool animated) { base.ViewDidDisappear(animated); if(!finished)Finish(null); }
        protected override void Dispose(bool disposing)
        {
            if(disposing) { backgroundObserver?.Dispose(); cameraQueue.DispatchSync(() => session.StopRunning()); preview?.Dispose(); reader?.Dispose(); session.Dispose(); cameraQueue.Dispose(); }
            base.Dispose(disposing);
        }
    }
    private sealed class Reader(Action<string> found) : AVCaptureMetadataOutputObjectsDelegate
    {
        public override void DidOutputMetadataObjects(AVCaptureMetadataOutput output, AVMetadataObject[] objects, AVCaptureConnection connection)
        {
            var code=objects.OfType<AVMetadataMachineReadableCodeObject>().Select(x=>x.StringValue).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
            if(code is not null)found(code);
        }
    }
}
