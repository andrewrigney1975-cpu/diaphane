using Diaphane.Shell.Engine;

namespace Diaphane.Shell.Tests;

public sealed class FakeEngine : IBrowserEngine
{
    public List<FakeContext> Contexts { get; } = new();
    public FakeEngine() => StandardContext = NewCtx(new RequestContextOptions(true, "disk"));

    public IRequestContext StandardContext { get; }
    public IRequestContext CreateContext(RequestContextOptions o) => NewCtx(o);
    public IBrowserView CreateView(IRequestContext c, nint hwnd) => new FakeView();
    public void DoMessageLoopWork() { }
    public event EventHandler<int>? ScheduleMessagePump { add { } remove { } }

    private FakeContext NewCtx(RequestContextOptions o)
    {
        var c = new FakeContext(o.Persistent);
        Contexts.Add(c);
        return c;
    }
}

public sealed class FakeContext(bool persistent) : IRequestContext
{
    public Guid Id { get; } = Guid.NewGuid();
    public bool IsPersistent => persistent;
    public bool Disposed { get; private set; }
    public IReadOnlyList<ExtensionInfo> Extensions => Array.Empty<ExtensionInfo>();

    public Task ClearCookiesAsync(DateTimeOffset? since = null) => Task.CompletedTask;
    public Task ClearStorageAsync(DateTimeOffset? since = null) => Task.CompletedTask;
    public Task ClearHttpCacheAsync() => Task.CompletedTask;
    public Task FlushDnsAsync() => Task.CompletedTask;
    public Task<ExtensionInfo> LoadExtensionAsync(string path) =>
        Task.FromResult(new ExtensionInfo("id", "x", "1", true, Array.Empty<string>()));
    public void Dispose() => Disposed = true;
}

public sealed class FakeView : IBrowserView
{
    public Guid Id { get; } = Guid.NewGuid();
    public string CurrentUrl { get; private set; } = "";
    public bool Visible { get; private set; }
    public bool CanGoBack => false;
    public bool CanGoForward => false;

    public void Navigate(string url)
    {
        CurrentUrl = url;
        NavigationStateChanged?.Invoke(this, new NavigationState(url, url, false, false, false, 1));
    }
    public void Reload(bool ignoreCache = false) { }
    public void Stop() { }
    public void GoBack() { }
    public void GoForward() { }
    public void SetBounds(int x, int y, int w, int h) { }
    public void SetVisible(bool v) => Visible = v;
    public void SetFocus(bool f) { }
    public void Dispose() { }

    public event EventHandler<NavigationState>? NavigationStateChanged;
    public event EventHandler<string>? TitleChanged { add { } remove { } }
    public event EventHandler<string>? FaviconUrlChanged { add { } remove { } }
}
