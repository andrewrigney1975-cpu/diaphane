namespace Diaphane.Shell.Engine;

/// <summary>
/// The one seam between the C#/WinUI shell and the native Chromium engine
/// (libcef, built from an ungoogled-chromium checkout). Everything above this
/// interface builds and unit-tests without the ~100&nbsp;GB engine checkout.
/// Implemented for real by Diaphane.Core (C++/WinRT); faked in tests.
/// </summary>
public interface IBrowserEngine
{
    /// <summary>Create an isolated storage partition (cookies, cache, storage).</summary>
    IRequestContext CreateContext(RequestContextOptions options);

    /// <summary>The shared, on-disk context used by all normal tabs.</summary>
    IRequestContext StandardContext { get; }

    /// <summary>Spin up a browser view bound to a context, parented to a host window handle.</summary>
    IBrowserView CreateView(IRequestContext context, nint hostHwnd);

    /// <summary>Pump one slice of the CEF message loop (external_message_pump mode).</summary>
    void DoMessageLoopWork();

    event EventHandler<int>? ScheduleMessagePump; // delay in ms
}

public sealed record RequestContextOptions(
    bool Persistent,
    string? CachePath = null,
    string? ProxyUri = null)
{
    /// <summary>Ephemeral, fully in-memory partition — the Sandbox tab's isolation unit.</summary>
    public static RequestContextOptions Sandbox(string? proxyUri = null)
        => new(Persistent: false, CachePath: null, ProxyUri: proxyUri);
}

public interface IRequestContext : IDisposable
{
    Guid Id { get; }
    bool IsPersistent { get; }

    Task ClearCookiesAsync(DateTimeOffset? since = null);
    Task ClearStorageAsync(DateTimeOffset? since = null);
    Task ClearHttpCacheAsync();
    Task FlushDnsAsync();

    /// <summary>Install an unpacked directory or a .crx. No silent auto-update ever.</summary>
    Task<ExtensionInfo> LoadExtensionAsync(string path);
    IReadOnlyList<ExtensionInfo> Extensions { get; }
}

public sealed record ExtensionInfo(string Id, string Name, string Version, bool Enabled, IReadOnlyList<string> Permissions);

public interface IBrowserView : IDisposable
{
    Guid Id { get; }
    void Navigate(string url);
    void Reload(bool ignoreCache = false);
    void Stop();
    void GoBack();
    void GoForward();
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    void SetBounds(int x, int y, int width, int height);
    void SetVisible(bool visible);
    void SetFocus(bool focused);

    /// <summary>
    /// Evaluate <paramref name="script"/> in the page and return the JSON-serialised
    /// result (CDP Runtime.evaluate). Rejects if evaluation errors.
    /// </summary>
    Task<string> EvaluateJavaScriptAsync(string script);

    event EventHandler<NavigationState>? NavigationStateChanged;
    event EventHandler<string>? TitleChanged;
    event EventHandler<string>? FaviconUrlChanged;

    /// <summary>
    /// Raised as a browser-initiated download progresses. Requires an engine build with
    /// download support (CefDownloadHandler wired up natively) — a build without it never
    /// raises this event, and the shell degrades to "downloads aren't tracked" rather than
    /// failing.
    /// </summary>
    event EventHandler<DownloadProgress>? DownloadUpdated;

    /// <summary>
    /// Raised (with the target URL) when the page tries to open a new window/tab — there's
    /// no second top-level window to host a real popup in, so the engine always cancels it
    /// and leaves opening it up to the shell. Requires an engine build with popup-routing
    /// support; a build without it just cancels silently.
    /// </summary>
    event EventHandler<string>? PopupRequested;

    /// <summary>
    /// Raised on right-click with what was clicked. The engine always suppresses its own
    /// context menu (no native chrome to host it in) — the shell shows its own and acts on
    /// the result via <see cref="StartDownload"/>. Requires an engine build with context-menu
    /// support; a build without it just shows no menu at all.
    /// </summary>
    event EventHandler<ContextMenuInfo>? ContextMenuRequested;

    /// <summary>Explicitly download <paramref name="url"/> — "Save link/image/video as…".
    /// Goes through the same pipeline as a page-initiated download.</summary>
    void StartDownload(string url);
}

public sealed record NavigationState(string Url, string Title, bool IsLoading, bool CanGoBack, bool CanGoForward, double Progress);

public enum DownloadState { InProgress, Complete, Cancelled, Interrupted }

public sealed record DownloadProgress(
    long NativeId, string Url, string FileName, string FilePath,
    long ReceivedBytes, long TotalBytes, DownloadState State);

public enum ContextMenuKind { None, Link, Image, Video, Audio }

/// <summary>What was right-clicked, and where (view-local pixels, for positioning the shell's
/// own menu). <see cref="LinkUrl"/> is set whenever the click landed on/inside a hyperlink,
/// independent of <see cref="Kind"/>; <see cref="SrcUrl"/> is the media resource for Image/Video/Audio.</summary>
public sealed record ContextMenuInfo(ContextMenuKind Kind, string LinkUrl, string SrcUrl, int X, int Y);

/// <summary>Off-screen-rendered browser: the shell owns the surface and forwards input.</summary>
public interface IOffscreenBrowserView : IBrowserView
{
    /// <summary>Raised on the CEF UI thread with a pointer to a top-down BGRA32 frame, valid only for the call.</summary>
    event EventHandler<FramePaint>? FramePainted;

    /// <summary>
    /// Open DevTools for this view, rendered off-screen so the shell can dock it
    /// in a pane. Resolves the front-end over the loopback debugging endpoint.
    /// Returns the DevTools view (also off-screen), or null on failure.
    /// Idempotent — returns the existing one if already open.
    /// </summary>
    Task<IOffscreenBrowserView?> OpenDevToolsAsync(int width, int height);
    void CloseDevTools();
    bool HasDevTools { get; }

    void ResizeSurface(int width, int height);
    void Invalidate();
    void SendMouseMove(int x, int y, bool leaving);
    void SendMouseButton(int x, int y, int button, bool down, int clickCount);
    void SendMouseWheel(int x, int y, int deltaX, int deltaY);
    void SendKey(bool isDown, int windowsKeyCode, int nativeKeyCode, uint modifiers, char character);
}

public readonly record struct FramePaint(nint Bgra, int Width, int Height, int DirtyX, int DirtyY, int DirtyW, int DirtyH);
