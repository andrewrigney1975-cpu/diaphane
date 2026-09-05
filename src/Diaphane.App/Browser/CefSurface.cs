using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Diaphane.Shell.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Diaphane.App.Browser;

/// <summary>
/// Binds one off-screen CEF view to a display <see cref="Image"/> (pointer
/// target, sized to the frame) hosted inside a focusable <paramref name="host"/>
/// control (keyboard focus + key events — a bare Image can't hold focus in
/// WinUI). Blits BGRA frames and forwards pointer / keyboard input. Used for
/// both the page surface and the docked DevTools pane.
/// </summary>
internal sealed class CefSurface(Image image, Control host)
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

    /// <summary>Asks CEF to repaint at the current size unconditionally — unlike <see cref="Resize"/>,
    /// which only does anything when the computed pixel size has actually changed. A sibling panel
    /// (bookmarks, the right pane) toggling can leave this pane's own size unchanged while still
    /// needing a fresh paint, if the layout pass that would have changed it hasn't settled by the
    /// time something reads ActualWidth/Height.</summary>
    public void Invalidate() => _view?.Invalidate();

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
        // ResizeSurface alone doesn't reliably repaint at the new size in every case (a splitter
        // drag, a window resize, or a sibling panel toggling can all change this without any
        // mouse/keyboard input of their own to otherwise trigger one) — ask explicitly.
        _view.Invalidate();
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

    private int _downButton = -1;

    public void PointerPressed(PointerRoutedEventArgs e)
    {
        host.Focus(FocusState.Pointer);       // route keyboard to the focusable host
        image.CapturePointer(e.Pointer);
        var (x, y) = Px(e);
        _downButton = ButtonOf(e);
        // OSR: the browser must be told it has focus or clicks won't land DOM
        // focus on form fields and key events are dropped.
        _view?.SetFocus(true);
        _view?.SendMouseMove(x, y, false);
        _view?.SendMouseButton(x, y, _downButton, true, 1);
    }

    /// <summary>The chrome (address bar, a panel) took focus — blur the page.</summary>
    public void Blur() => _view?.SetFocus(false);

    public void PointerReleased(PointerRoutedEventArgs e)
    {
        image.ReleasePointerCapture(e.Pointer);
        var (x, y) = Px(e);
        var btn = _downButton >= 0 ? _downButton : ButtonOf(e);
        _downButton = -1;
        _view?.SendMouseButton(x, y, btn, false, 1);
    }

    public void PointerWheel(PointerRoutedEventArgs e)
    {
        var (x, y) = Px(e);
        _view?.SendMouseWheel(x, y, 0, e.GetCurrentPoint(image).Properties.MouseWheelDelta);
    }

    public void KeyDown(KeyRoutedEventArgs e) => SendKey(e, true);
    public void KeyUp(KeyRoutedEventArgs e) => SendKey(e, false);
    public void Char(CharacterReceivedRoutedEventArgs e)
        => _view?.SendKey(true, e.Character, 0, 0, e.Character);

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
