using System.ComponentModel;
using System.Runtime.InteropServices;
using Diaphane.App.Browser;
using Diaphane.Shell.Tabs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Diaphane.App;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);

    private readonly CefHost _cef;
    public ShellViewModel Vm { get; }

    private nint FrameHwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>The WinUI content island child window — parenting the CEF child here
    /// keeps it clipped to and z-ordered within the XAML content region.</summary>
    private nint ContentHostHwnd
    {
        get
        {
            var island = FindWindowExW(FrameHwnd, 0, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
            return island != 0 ? island : FrameHwnd;
        }
    }

    public MainWindow()
    {
        InitializeComponent();

        Title = "diaphane";
        ExtendsContentIntoTitleBar = false;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diaphane");

        _cef = new CefHost(DispatcherQueue, CefHost.ResolveNativeBinDir());
        Vm = new ShellViewModel(_cef.Engine, dataDir);
        Vm.PropertyChanged += OnVmPropertyChanged;

        Root.Loaded += (_, _) =>
        {
            Vm.Start(ContentHostHwnd);   // island HWND exists now
            SelectActiveInStrip();
            UpdateBrowserBounds();
        };
        Activated += (_, _) => UpdateBrowserBounds();
        Closed += (_, _) => { Vm.Dispose(); _cef.Dispose(); };

        // The CEF child window can materialise a beat after CreateBrowserSync;
        // re-assert its bounds for the first few seconds.
        var settle = DispatcherQueue.CreateTimer();
        settle.Interval = TimeSpan.FromMilliseconds(250);
        int ticks = 0;
        settle.Tick += (t, _) => { UpdateBrowserBounds(); if (++ticks > 16) t.Stop(); };
        settle.Start();
    }

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.ActiveTab):
                SelectActiveInStrip();
                UpdateBrowserBounds();
                break;
            case nameof(ShellViewModel.IsLoading):
                LoadBar.Visibility = Vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                break;
        }
    }

    // ---- tab strip ----
    private void OnAddTab(TabView sender, object args) => Vm.NewTab();

    private void OnTabClose(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is TabModel t) Vm.CloseTab(t);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabStrip.SelectedItem is TabModel t && !ReferenceEquals(t, Vm.ActiveTab))
            Vm.ActiveTab = t;
    }

    private void SelectActiveInStrip()
    {
        if (!ReferenceEquals(TabStrip.SelectedItem, Vm.ActiveTab))
            TabStrip.SelectedItem = Vm.ActiveTab;
    }

    // ---- address bar ----
    private void OnAddressTextChanged(AutoSuggestBox box, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        box.ItemsSource = Vm.Suggest(box.Text).Select(x => x.Url).ToList();
    }

    private void OnSuggestionChosen(AutoSuggestBox box, AutoSuggestBoxSuggestionChosenEventArgs args)
        => box.Text = args.SelectedItem?.ToString() ?? box.Text;

    private void OnAddressSubmitted(AutoSuggestBox box, AutoSuggestBoxQuerySubmittedEventArgs args)
        => Vm.NavigateCommand.Execute(args.QueryText ?? box.Text);

    // ---- CEF child-window placement ----
    private void OnBrowserRegionChanged(object sender, SizeChangedEventArgs e) => UpdateBrowserBounds();

    private void UpdateBrowserBounds()
    {
        if (BrowserRegion.ActualWidth < 1) return;
        var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        var origin = BrowserRegion.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        Vm.SetBrowserBounds(
            (int)Math.Round(origin.X * scale),
            (int)Math.Round(origin.Y * scale),
            (int)Math.Round(BrowserRegion.ActualWidth * scale),
            (int)Math.Round(BrowserRegion.ActualHeight * scale));
    }
}
