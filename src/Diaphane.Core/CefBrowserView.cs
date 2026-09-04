using Diaphane.Shell.Engine;
using static Diaphane.Core.NativeMethods;

namespace Diaphane.Core;

/// <summary>One CEF browser, bound to a request context. Marshals CEF events to the shell.</summary>
internal sealed class CefBrowserView : IOffscreenBrowserView
{
    // Delegate instances rooted for the view's lifetime — native holds raw pointers to these.
    private readonly ViewCallbacks _cb;
    private readonly NavStateCb _navCb;
    private readonly StringCb _titleCb;
    private readonly StringCb _faviconCb;
    private readonly LoadEndCb _loadCb;
    private readonly ViewLifecycleCb _createdCb;
    private readonly ViewLifecycleCb _closedCb;
    private readonly PaintCb _paintCb;
    private readonly EvalCb _evalCb;
    private readonly Dictionary<int, TaskCompletionSource<string>> _pendingEvals = new();

    private NavigationState _state = new("about:blank", "", true, false, false, 0);
    private bool _disposed;

    internal string NativeId { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public bool CanGoBack => dc_view_can_back(NativeId) != 0;
    public bool CanGoForward => dc_view_can_forward(NativeId) != 0;

    internal event EventHandler? Closed;
    public event EventHandler<NavigationState>? NavigationStateChanged;
    public event EventHandler<string>? TitleChanged;
    public event EventHandler<string>? FaviconUrlChanged;
    public event EventHandler<FramePaint>? FramePainted;

    public CefBrowserView(string contextId, nint hostHwnd, int width = 1280, int height = 800)
    {
        _navCb = (_, url, title, loading, back, fwd, progress, _) =>
        {
            _state = _state with
            {
                Url = url,
                Title = string.IsNullOrEmpty(title) ? _state.Title : title,
                IsLoading = loading != 0,
                CanGoBack = back != 0,
                CanGoForward = fwd != 0,
                Progress = progress,
            };
            NavigationStateChanged?.Invoke(this, _state);
        };
        _titleCb = (_, title, _) =>
        {
            _state = _state with { Title = title };
            TitleChanged?.Invoke(this, title);
            NavigationStateChanged?.Invoke(this, _state);
        };
        _faviconCb = (_, iconUrl, _) => FaviconUrlChanged?.Invoke(this, iconUrl);
        _loadCb = (_, _, _) =>
        {
            _state = _state with { IsLoading = false, Progress = 1.0 };
            NavigationStateChanged?.Invoke(this, _state);
        };
        _createdCb = (_, _) => { };
        _closedCb = (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        _evalCb = (_, requestId, ok, resultJson, _) =>
        {
            if (!_pendingEvals.Remove(requestId, out var tcs)) return;
            if (ok != 0) tcs.TrySetResult(resultJson);
            else tcs.TrySetException(new InvalidOperationException(
                $"Script evaluation failed: {resultJson}"));
        };
        _paintCb = (_, bgra, w, h, dx, dy, dw, dh, _) =>
            FramePainted?.Invoke(this, new FramePaint(bgra, w, h, dx, dy, dw, dh));

        _cb = new ViewCallbacks
        {
            OnNavState = _navCb,
            OnTitle = _titleCb,
            OnFavicon = _faviconCb,
            OnLoadEnd = _loadCb,
            OnCreated = _createdCb,
            OnClosed = _closedCb,
            OnPaint = _paintCb,
            User = IntPtr.Zero,
        };

        var p = dc_view_create(contextId, hostHwnd, width, height, _cb);
        NativeId = PtrToUtf8(p);
        if (string.IsNullOrEmpty(NativeId))
            throw new InvalidOperationException("CefBrowserHost::CreateBrowserSync failed.");
    }

    public void Navigate(string url) => dc_view_navigate(NativeId, url);
    public void Reload(bool ignoreCache = false) => dc_view_reload(NativeId, ignoreCache ? 1 : 0);
    public void Stop() => dc_view_stop(NativeId);
    public void GoBack() => dc_view_back(NativeId);
    public void GoForward() => dc_view_forward(NativeId);
    public void SetBounds(int x, int y, int width, int height) => dc_view_set_bounds(NativeId, x, y, width, height);
    public void SetVisible(bool visible) => dc_view_set_visible(NativeId, visible ? 1 : 0);
    public void SetFocus(bool focused) => dc_view_set_focus(NativeId, focused ? 1 : 0);

    public void ShowDevTools() => dc_view_show_devtools(NativeId, 0, 0);
    public void CloseDevTools() => dc_view_close_devtools(NativeId);
    public bool HasDevTools => dc_view_has_devtools(NativeId) != 0;

    public Task<string> EvaluateJavaScriptAsync(string script)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = dc_view_eval_js(NativeId, script, _evalCb, IntPtr.Zero);
        if (id == 0)
            tcs.TrySetException(new InvalidOperationException("The view has no live browser."));
        else
            _pendingEvals[id] = tcs;
        return tcs.Task;
    }

    public void ResizeSurface(int width, int height) => dc_view_osr_size(NativeId, width, height);
    public void Invalidate() => dc_view_invalidate(NativeId);
    public void SendMouseMove(int x, int y, bool leaving) => dc_view_mouse_move(NativeId, x, y, leaving ? 1 : 0);
    public void SendMouseButton(int x, int y, int button, bool down, int clickCount) =>
        dc_view_mouse_button(NativeId, x, y, button, down ? 1 : 0, clickCount);
    public void SendMouseWheel(int x, int y, int deltaX, int deltaY) => dc_view_mouse_wheel(NativeId, x, y, deltaX, deltaY);
    public void SendKey(bool isDown, int windowsKeyCode, int nativeKeyCode, uint modifiers, char character) =>
        dc_view_key(NativeId, isDown ? 1 : 0, windowsKeyCode, nativeKeyCode, modifiers, character);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        dc_view_close(NativeId);
        GC.KeepAlive(_cb);
    }
}
