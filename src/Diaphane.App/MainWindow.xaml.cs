using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Diaphane.App.Browser;
using Diaphane.Shell.Engine;
using Diaphane.Shell.Tabs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Diaphane.App;

public sealed partial class MainWindow : Window
{
    private readonly CefHost _cef;
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

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diaphane");

        _cef = new CefHost(DispatcherQueue, CefHost.ResolveNativeBinDir());
        Vm = new ShellViewModel(_cef.Engine, dataDir);
        Vm.PropertyChanged += OnVmPropertyChanged;

        Root.Loaded += (_, _) =>
        {
            try
            {
                _scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
                Vm.Start(0);
                SelectActiveInStrip();
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
        Closed += (_, _) => { Vm.Dispose(); _cef.Dispose(); };
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
        }
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
}
