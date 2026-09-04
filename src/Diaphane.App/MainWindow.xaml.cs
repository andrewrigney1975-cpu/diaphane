using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Diaphane.App.Browser;
using Diaphane.Data;
using Diaphane.Shell.Engine;
using Diaphane.Shell.Tabs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace Diaphane.App;

public sealed partial class MainWindow : Window
{
    private readonly CefHost _cef;
    private readonly ExtensionStore _extensions;
    public ShellViewModel Vm { get; }

    private IOffscreenBrowserView? _view;
    private WriteableBitmap? _bitmap;
    private byte[] _frame = Array.Empty<byte>();
    private int _pxW, _pxH;
    private double _scale = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        Title = "diaphane";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "diaphane.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diaphane");
        Directory.CreateDirectory(dataDir);

        _extensions = new ExtensionStore(Path.Combine(dataDir, "extensions.db"));
        var privacySettings = new Diaphane.Privacy.PrivacySettingsStore(Path.Combine(dataDir, "privacy.json")).Load();
        _cef = new CefHost(DispatcherQueue, CefHost.ResolveNativeBinDir(),
            _extensions.EnabledPaths(), allowWidevine: privacySettings.EnableWidevine);
        Vm = new ShellViewModel(_cef.Engine, dataDir, _extensions);
        Vm.PropertyChanged += OnVmPropertyChanged;
        Vm.Tabs.CollectionChanged += (_, _) => SyncTabStrip();

        Root.Loaded += (_, _) =>
        {
            try
            {
                _scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
                Vm.Start(0);
                SyncTabStrip();
                AttachActiveView();
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "diaphane-app.log"),
                    $"{DateTime.Now:o} Loaded FAILED:\n{ex}\n\n");
            }

            if (Environment.GetEnvironmentVariable("DIAPHANE_SELFSHOT") is not null)
                _ = SelfCaptureLoopAsync();
        };
        Closed += (_, _) =>
        {
            try { Vm.RunClearOnExitAsync().Wait(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
            Vm.Dispose();
            _cef.Dispose();
            _extensions.Dispose();
        };

        InstallAccelerators();
    }

    // ---- x:Bind function helpers (sandbox accent) ----
    private static readonly SolidColorBrush s_sandboxText = new(Color.FromArgb(0xFF, 0xB3, 0x9D, 0xDB));

    public static Brush TabBrush(bool isSandbox) => isSandbox
        ? s_sandboxText
        : (Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush ?? new SolidColorBrush(Colors.White));

    public static Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisIfText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool b) => !b;

    public static Brush ToolbarBrush(bool isSandbox) => isSandbox
        ? new SolidColorBrush(Color.FromArgb(0x33, 0x67, 0x3A, 0xB7))
        : (Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush ?? new SolidColorBrush(Colors.Transparent));

    // ---- keyboard accelerators ----
    private void InstallAccelerators()
    {
        void Add(VirtualKey key, VirtualKeyModifiers mods, Action run)
        {
            var a = new KeyboardAccelerator { Key = key, Modifiers = mods };
            a.Invoked += (_, e) => { e.Handled = true; run(); };
            Root.KeyboardAccelerators.Add(a);
        }

        const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;
        const VirtualKeyModifiers CtrlShift = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift;

        Add(VirtualKey.T, Ctrl, () => Vm.NewTab());
        Add(VirtualKey.W, Ctrl, () => Vm.CloseActiveTab());
        Add(VirtualKey.Tab, Ctrl, () => Vm.NextTab());
        Add(VirtualKey.Tab, CtrlShift, () => Vm.PrevTab());
        Add(VirtualKey.L, Ctrl, () => Address.Focus(FocusState.Programmatic));
        Add(VirtualKey.F5, VirtualKeyModifiers.None, () => Vm.ReloadCommand.Execute(null));
        Add(VirtualKey.Left, VirtualKeyModifiers.Menu, () => Vm.GoBackCommand.Execute(null));
        Add(VirtualKey.Right, VirtualKeyModifiers.Menu, () => Vm.GoForwardCommand.Execute(null));
        Add(VirtualKey.D, Ctrl, () => Vm.ToggleBookmarkCommand.Execute(null));
        Add(VirtualKey.B, CtrlShift, () => Vm.ToggleBookmarksBarCommand.Execute(null));
        Add(VirtualKey.N, CtrlShift, () => Vm.NewSandboxTabCommand.Execute(null));
        Add(VirtualKey.Delete, CtrlShift, () => Vm.TogglePrivacyPanelCommand.Execute(null));
        Add(VirtualKey.E, CtrlShift, () => Vm.ToggleExtensionsPanelCommand.Execute(null));
        Add(VirtualKey.I, CtrlShift, () => Vm.ToggleDevToolsCommand.Execute(null));
        Add(VirtualKey.F12, VirtualKeyModifiers.None, () => Vm.ToggleDevToolsCommand.Execute(null));
        Add(VirtualKey.M, CtrlShift, () => Vm.ToggleMediaPanelCommand.Execute(null));
    }

    // ---- diaphane://extensions ----
    private async void OnLoadUnpackedClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            Vm.Extensions.LoadUnpacked(folder.Path);
    }

    private void OnRemoveExtensionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Browser.ExtensionRow row)
            Vm.Extensions.RemoveCommand.Execute(row);
    }

    // Diagnostic: RenderTargetBitmap captures the live XAML visual tree (incl. the
    // CEF Image) straight from the compositor — the only reliable way to prove
    // what actually rendered, since BitBlt/PrintWindow can't see WinUI DComp content.
    private async System.Threading.Tasks.Task SelfCaptureLoopAsync()
    {
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
        for (int i = 0; i < 8; i++)
        {
            await System.Threading.Tasks.Task.Delay(2500);
            try
            {
                var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await rtb.RenderAsync(Root);
                var px = (await rtb.GetPixelsAsync()).ToArray();
                long nonBlack = 0;
                for (int k = 0; k < px.Length; k += 4)
                    if (px[k] > 8 || px[k + 1] > 8 || px[k + 2] > 8) nonBlack++;
                var file = await folder.CreateFileAsync($"diaphane-shot{i}.png",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);
                using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                var enc = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                enc.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, px);
                await enc.FlushAsync();
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "diaphane-app.log"),
                    $"{DateTime.Now:o} selfshot {i}: {rtb.PixelWidth}x{rtb.PixelHeight} nonBlackPx={nonBlack}\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "diaphane-app.log"),
                    $"{DateTime.Now:o} selfshot {i}: {ex.Message}\n");
            }
        }
    }

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.ActiveTab):
                SelectActiveInStrip();
                AttachActiveView();
                break;
            case nameof(ShellViewModel.IsLoading):
                LoadBar.Visibility = Vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                break;
        }
    }

    // ---- surface wiring ----
    private void AttachActiveView()
    {
        var next = Vm.ActiveTab?.Offscreen;
        if (ReferenceEquals(next, _view)) return;
        if (_view is not null) _view.FramePainted -= OnFramePainted;
        _view = next;
        if (_view is not null)
        {
            _view.FramePainted += OnFramePainted;
            ResizeSurface();
            _view.Invalidate();   // force a full repaint of the newly-shown tab's surface
        }
    }

    // ---- history flyout ----
    private void OnHistoryFlyoutOpening(object? sender, object e)
        => HistoryList.ItemsSource = Vm.RecentHistory();

    private void OnHistoryItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Diaphane.Data.VisitEntry v)
            Vm.NavigateCommand.Execute(v.Url);
    }

    // ---- bookmarks bar ----
    private void OnBookmarkClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Diaphane.Data.Bookmark b)
            Vm.OpenBookmarkCommand.Execute(b);
    }

    private void OnBookmarkRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Diaphane.Data.Bookmark b)
            Vm.RemoveBookmarkCommand.Execute(b);
    }

    private void OnBrowserRegionChanged(object sender, SizeChangedEventArgs e) => ResizeSurface();

    private void ResizeSurface()
    {
        if (_view is null || BrowserRegion.ActualWidth < 1) return;
        _scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        int w = Math.Max(1, (int)Math.Round(BrowserRegion.ActualWidth * _scale));
        int h = Math.Max(1, (int)Math.Round(BrowserRegion.ActualHeight * _scale));
        if (w == _pxW && h == _pxH) return;
        _pxW = w; _pxH = h;
        _bitmap = new WriteableBitmap(w, h);
        _frame = new byte[w * h * 4];
        BrowserImage.Source = _bitmap;
        BrowserImage.Width = w / _scale;
        BrowserImage.Height = h / _scale;
        _view.ResizeSurface(w, h);
    }

    private void OnFramePainted(object? sender, FramePaint f)
    {
        // Raised on the CEF UI thread == our dispatcher thread (external pump), so
        // we can touch the bitmap directly; still guard against a stale size.
        if (_bitmap is null || f.Width != _pxW || f.Height != _pxH) return;
        try
        {
            int bytes = f.Width * f.Height * 4;
            if (_frame.Length < bytes) _frame = new byte[bytes];
            Marshal.Copy(f.Bgra, _frame, 0, bytes);
            using (var s = _bitmap.PixelBuffer.AsStream())
            {
                s.Position = 0;
                s.Write(_frame, 0, bytes);
            }
            _bitmap.Invalidate();
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "diaphane-app.log"),
                $"{DateTime.Now:o} paint FAILED: {ex.Message}\n");
        }
    }

    // ---- input forwarding (positions in device px) ----
    private (int x, int y) Px(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(BrowserImage).Position;
        return ((int)Math.Round(p.X * _scale), (int)Math.Round(p.Y * _scale));
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var (x, y) = Px(e);
        _view?.SendMouseMove(x, y, false);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var (x, y) = Px(e);
        _view?.SendMouseMove(x, y, true);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        BrowserImage.Focus(FocusState.Pointer);
        BrowserImage.CapturePointer(e.Pointer);
        var (x, y) = Px(e);
        _view?.SendMouseButton(x, y, ButtonOf(e), true, 1);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        BrowserImage.ReleasePointerCapture(e.Pointer);
        var (x, y) = Px(e);
        _view?.SendMouseButton(x, y, ButtonOf(e), false, 1);
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        var (x, y) = Px(e);
        _view?.SendMouseWheel(x, y, 0, e.GetCurrentPoint(BrowserImage).Properties.MouseWheelDelta);
    }

    private static int ButtonOf(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(null).Properties;
        return p.IsRightButtonPressed || p.PointerUpdateKind is Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased ? 2
             : p.IsMiddleButtonPressed || p.PointerUpdateKind is Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased ? 1
             : 0;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e) => SendKey(e, true);
    private void OnKeyUp(object sender, KeyRoutedEventArgs e) => SendKey(e, false);

    private void SendKey(KeyRoutedEventArgs e, bool down)
    {
        int scan = (int)e.KeyStatus.ScanCode;
        _view?.SendKey(down, (int)e.Key, scan != 0 ? scan : (int)e.Key, 0, '\0');
        // let editable-field navigation keys through; text arrives via CharacterReceived
    }

    private void OnChar(UIElement sender, CharacterReceivedRoutedEventArgs e)
        => _view?.SendKey(true, 0, 0, 0, e.Character);

    // ---- tab strip ----
    // We build TabViewItems by hand (rather than TabItemsSource + TabItemTemplate)
    // because TabView does not refresh a templated header when the bound model's
    // properties change — the tab title would stay frozen at "New Tab".
    private readonly Dictionary<TabModel, TabViewItem> _tabItems = new();
    private bool _syncingStrip;

    private void SyncTabStrip()
    {
        // add missing
        foreach (var t in Vm.Tabs)
            if (!_tabItems.ContainsKey(t))
            {
                var item = BuildTabItem(t);
                _tabItems[t] = item;
                t.PropertyChanged += OnTabModelPropertyChanged;
            }

        // remove stale
        foreach (var kv in _tabItems.Where(kv => !Vm.Tabs.Contains(kv.Key)).ToList())
        {
            kv.Key.PropertyChanged -= OnTabModelPropertyChanged;
            _tabItems.Remove(kv.Key);
        }

        _syncingStrip = true;
        TabStrip.TabItems.Clear();
        foreach (var t in Vm.Tabs) TabStrip.TabItems.Add(_tabItems[t]);
        _syncingStrip = false;

        SelectActiveInStrip();
    }

    private static TabViewItem BuildTabItem(TabModel t)
    {
        var icon = new FontIcon
        {
            Glyph = "",
            FontSize = 12,
            Foreground = s_sandboxText,
            Visibility = t.IsSandbox ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var text = new TextBlock
        {
            Text = t.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(text);
        return new TabViewItem { Header = panel, Tag = t };
    }

    private void OnTabModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TabModel t || e.PropertyName != nameof(TabModel.Title)) return;
        if (_tabItems.TryGetValue(t, out var item) &&
            item.Header is StackPanel { Children: [_, TextBlock tb] })
            tb.Text = t.Title;
    }

    private void OnAddTab(TabView sender, object args) => Vm.NewTab();

    private void OnTabClose(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab?.Tag is TabModel t) Vm.CloseTab(t);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStrip) return;
        if (TabStrip.SelectedItem is TabViewItem { Tag: TabModel t } && !ReferenceEquals(t, Vm.ActiveTab))
            Vm.ActiveTab = t;
    }

    private void SelectActiveInStrip()
    {
        if (Vm.ActiveTab is { } a && _tabItems.TryGetValue(a, out var item)
            && !ReferenceEquals(TabStrip.SelectedItem, item))
        {
            _syncingStrip = true;
            TabStrip.SelectedItem = item;
            _syncingStrip = false;
        }
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
}
