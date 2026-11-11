using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace StarfleetLibrary.Uno;

public sealed partial class MainPage
{
    private string detailOriginTitle = "", detailOriginSubtitle = "";
    private (double Horizontal, double Vertical)[] detailOriginScroll = [];

    private void OpenDetails()
    {
        if (!detailOpen)
        {
            detailOriginTitle = PageTitle.Text;
            detailOriginSubtitle = PageSubtitle.Text;
            detailOriginScroll = CaptureBodyScroll();
        }
        detailOpen = true;
        // Retain the original controls, including lookup results and the catalog's view state.
        Body.Visibility = Visibility.Collapsed;
        DetailBody.Children.Clear();
        DetailBody.Visibility = DetailBackButton.Visibility = Visibility.Visible;
        DetailBackButton.Content = "←  Back to " + section;
    }

    private void CloseDetails()
    {
        detailOpen = false;
        DetailBody.Children.Clear();
        DetailBody.Visibility = DetailBackButton.Visibility = Visibility.Collapsed;
        Body.Visibility = Visibility.Visible;
    }

    private void DetailBackClicked(object sender, RoutedEventArgs e) => ReturnFromDetails();

    private void DetailBackInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        if (!detailOpen) return;
        ReturnFromDetails();
        e.Handled = true;
    }

    private void ReturnFromDetails()
    {
        if (!detailOpen) return;
        var offsets = detailOriginScroll;
        CloseDetails();
        PageTitle.Text = detailOriginTitle;
        PageSubtitle.Text = detailOriginSubtitle;
        Notice.IsOpen = false;
        if (section == "Insights")
        {
            Body.Children.Clear();
            ShowInsights();
        }
        else RefreshResults();
        Responsive();
        RestoreBodyScroll(offsets);
        detailOriginScroll = [];
    }

    private (double Horizontal, double Vertical)[] CaptureBodyScroll() => ScrollViewers(Body)
        .Select(view => (view.HorizontalOffset, view.VerticalOffset)).ToArray();

    private void RestoreBodyScroll((double Horizontal, double Vertical)[] offsets)
    {
        var content = Body.Children.FirstOrDefault();
        var source = itemView?.ItemsSource;
        void RestoreScroll()
        {
            if (detailOpen || !ReferenceEquals(content, Body.Children.FirstOrDefault()) || !ReferenceEquals(source, itemView?.ItemsSource)) return;
            Body.UpdateLayout();
            var viewers = ScrollViewers(Body).ToArray();
            for (var i = 0; i < Math.Min(viewers.Length, offsets.Length); i++)
                viewers[i].ChangeView(offsets[i].Horizontal, offsets[i].Vertical, null, true);
        }
        RestoreScroll();
        // Virtualized items finish measuring after the retained view becomes visible again.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RestoreScroll);
    }

    private static IEnumerable<ScrollViewer> ScrollViewers(DependencyObject root)
    {
        if (root is ScrollViewer viewer) yield return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var descendant in ScrollViewers(VisualTreeHelper.GetChild(root, i)))
                yield return descendant;
    }
}
