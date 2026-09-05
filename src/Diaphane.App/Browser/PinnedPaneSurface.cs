using System.ComponentModel;
using Diaphane.Shell.Tabs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Diaphane.App.Browser;

/// <summary>One independent browsing surface for a single pinned Multiview pane. Mirrors
/// MainWindow's classic BuildBrowserRegion, but self-contained per instance — several of these
/// can coexist at once (one per pinned pane), unlike the single shared _page/BrowserImage/...
/// fields that exist for the one FollowActiveTab leaf. Built fresh on every pane-tree rebuild,
/// same as everything else PaneTreeView renders — see PaneTreeView's own notes on why nothing
/// here is ever reused across a rebuild.</summary>
internal sealed class PinnedPaneSurface
{
    private readonly TabModel _tab;
    private readonly CefSurface _surface;
    private readonly ProgressBar _loadBar;

    public Grid Region { get; }

    public PinnedPaneSurface(TabModel tab)
    {
        _tab = tab;

        var image = new Image
        {
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ManipulationMode = ManipulationModes.None,
        };

        var focus = new ScrollViewer
        {
            IsTabStop = true,
            UseSystemFocusVisuals = false,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            Content = image,
        };

        _surface = new CefSurface(image, focus);
        image.PointerMoved += (_, e) => _surface.PointerMoved(e);
        image.PointerPressed += (_, e) => _surface.PointerPressed(e);
        image.PointerReleased += (_, e) => _surface.PointerReleased(e);
        image.PointerExited += (_, e) => _surface.PointerExited(e);
        image.PointerWheelChanged += (_, e) => _surface.PointerWheel(e);
        focus.KeyDown += (_, e) => _surface.KeyDown(e);
        focus.KeyUp += (_, e) => _surface.KeyUp(e);
        focus.CharacterReceived += (_, e) => _surface.Char(e);

        _loadBar = new ProgressBar
        {
            VerticalAlignment = VerticalAlignment.Top,
            IsIndeterminate = true,
            Visibility = tab.IsLoading ? Visibility.Visible : Visibility.Collapsed,
        };
        _tab.PropertyChanged += OnTabPropertyChanged;

        Region = new Grid();
        Region.Children.Add(focus);
        Region.Children.Add(_loadBar);
        Region.SizeChanged += (_, _) => _surface.Resize();

        _surface.Attach(tab.Offscreen);
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabModel.IsLoading))
            _loadBar.Visibility = _tab.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Recomputes size (if changed) and asks for a fresh paint regardless — for a
    /// sibling panel (bookmarks, the right pane) toggling, which can otherwise leave a pinned
    /// pane stale.</summary>
    public void Refresh()
    {
        _surface.Resize();
        _surface.Invalidate();
    }

    /// <summary>Stops this surface from receiving further paint callbacks and drops its tab
    /// subscription — call before discarding, or (like the leak the classic surface used to have)
    /// it keeps doing paint work for content nothing shows anymore.</summary>
    public void Detach()
    {
        _surface.Attach(null);
        _tab.PropertyChanged -= OnTabPropertyChanged;
    }
}
