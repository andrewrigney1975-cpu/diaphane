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

    event EventHandler<NavigationState>? NavigationStateChanged;
    event EventHandler<string>? TitleChanged;
    event EventHandler<string>? FaviconUrlChanged;
}

public sealed record NavigationState(string Url, string Title, bool IsLoading, bool CanGoBack, bool CanGoForward, double Progress);

/// <summary>Off-screen-rendered browser: the shell owns the surface and forwards input.</summary>
public interface IOffscreenBrowserView : IBrowserView
{
    /// <summary>Raised on the CEF UI thread with a pointer to a top-down BGRA32 frame, valid only for the call.</summary>
    event EventHandler<FramePaint>? FramePainted;

    void ResizeSurface(int width, int height);
    void SendMouseMove(int x, int y, bool leaving);
    void SendMouseButton(int x, int y, int button, bool down, int clickCount);
    void SendMouseWheel(int x, int y, int deltaX, int deltaY);
    void SendKey(bool isDown, int windowsKeyCode, int nativeKeyCode, uint modifiers, char character);
}

public readonly record struct FramePaint(nint Bgra, int Width, int Height, int DirtyX, int DirtyY, int DirtyW, int DirtyH);
