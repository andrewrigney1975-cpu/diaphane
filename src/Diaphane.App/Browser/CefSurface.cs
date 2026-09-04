using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Diaphane.Shell.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Diaphane.App.Browser;

/// <summary>
/// Binds one off-screen CEF view to an <see cref="Image"/> inside a host panel:
/// blits BGRA frames into a <see cref="WriteableBitmap"/> and forwards pointer /
/// keyboard input. Used for both the page surface and the docked DevTools pane.
/// </summary>
internal sealed class CefSurface(Image image, FrameworkElement host)
{
    private IOffscreenBrowserView? _view;
    private WriteableBitmap? _bitmap;
    private byte[] _frame = Array.Empty<byte>();
    private int _pxW, _pxH;
    private double _scale = 1.0;

    public IOffscreenBrowserView? View => _view;

    public void Attach(IOffscreenBrowserView? view)
    {
        if (ReferenceEquals(view, _view)) return;
        if (_view is not null) _view.FramePainted -= OnFramePainted;
        _view = view;
        if (_view is null) { image.Source = null; return; }

        _view.FramePainted += OnFramePainted;
        _pxW = _pxH = 0;               // force a fresh surface
        Resize();
        _view.SetVisible(true);
        _view.Invalidate();
    }

    public void Resize()
    {
        if (_view is null || host.ActualWidth < 1 || host.ActualHeight < 1) return;
        _scale = host.XamlRoot?.RasterizationScale ?? 1.0;
        int w = Math.Max(1, (int)Math.Round(host.ActualWidth * _scale));
        int h = Math.Max(1, (int)Math.Round(host.ActualHeight * _scale));
        if (w == _pxW && h == _pxH) return;
        _pxW = w; _pxH = h;
        _bitmap = new WriteableBitmap(w, h);
        _frame = new byte[w * h * 4];
        image.Source = _bitmap;
        image.Width = w / _scale;
        image.Height = h / _scale;
        _view.ResizeSurface(w, h);
    }

    private void OnFramePainted(object? sender, FramePaint f)
    {
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

    // ---- input (positions in device px) ----
    private (int x, int y) Px(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(image).Position;
        return ((int)Math.Round(p.X * _scale), (int)Math.Round(p.Y * _scale));
    }

    public void PointerMoved(PointerRoutedEventArgs e) { var (x, y) = Px(e); _view?.SendMouseMove(x, y, false); }
    public void PointerExited(PointerRoutedEventArgs e) { var (x, y) = Px(e); _view?.SendMouseMove(x, y, true); }

    public void PointerPressed(PointerRoutedEventArgs e)
    {
        image.Focus(FocusState.Pointer);
        image.CapturePointer(e.Pointer);
        var (x, y) = Px(e);
        _view?.SendMouseButton(x, y, ButtonOf(e), true, 1);
    }

    public void PointerReleased(PointerRoutedEventArgs e)
    {
        image.ReleasePointerCapture(e.Pointer);
        var (x, y) = Px(e);
        _view?.SendMouseButton(x, y, ButtonOf(e), false, 1);
    }

    public void PointerWheel(PointerRoutedEventArgs e)
    {
        var (x, y) = Px(e);
        _view?.SendMouseWheel(x, y, 0, e.GetCurrentPoint(image).Properties.MouseWheelDelta);
    }

    public void KeyDown(KeyRoutedEventArgs e) => SendKey(e, true);
    public void KeyUp(KeyRoutedEventArgs e) => SendKey(e, false);
    public void Char(CharacterReceivedRoutedEventArgs e) => _view?.SendKey(true, 0, 0, 0, e.Character);

    private void SendKey(KeyRoutedEventArgs e, bool down)
    {
        int scan = (int)e.KeyStatus.ScanCode;
        _view?.SendKey(down, (int)e.Key, scan != 0 ? scan : (int)e.Key, 0, '\0');
    }

    private static int ButtonOf(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(null).Properties;
        return p.IsRightButtonPressed || p.PointerUpdateKind is Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased ? 2
             : p.IsMiddleButtonPressed || p.PointerUpdateKind is Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased ? 1
             : 0;
    }
}
