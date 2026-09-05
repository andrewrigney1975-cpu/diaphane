using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Diaphane.Shell.Engine;

namespace Diaphane.Shell.Tabs;

public enum TabKind { Standard, Sandbox }

public sealed class TabModel : INotifyPropertyChanged, IDisposable
{
    private readonly IBrowserView _view;
    private string _title = "New Tab";
    private string _url = "";
    private bool _isLoading;
    private Timer? _autoReloadTimer;
    private int? _autoReloadSeconds;

    public TabModel(IBrowserView view, TabKind kind, Guid contextId)
    {
        _view = view;
        Kind = kind;
        ContextId = contextId;
        _view.NavigationStateChanged += (_, s) =>
        {
            Url = s.Url;
            Title = string.IsNullOrEmpty(s.Title) ? s.Url : s.Title;
            IsLoading = s.IsLoading;
        };
    }

    public Guid Id { get; } = Guid.NewGuid();
    public TabKind Kind { get; }
    public Guid ContextId { get; }
    public bool IsSandbox => Kind == TabKind.Sandbox;

    /// <summary>Forwarded from the underlying view — see <see cref="IBrowserView.DownloadUpdated"/>.</summary>
    public event EventHandler<DownloadProgress>? DownloadUpdated
    {
        add => _view.DownloadUpdated += value;
        remove => _view.DownloadUpdated -= value;
    }

    /// <summary>Forwarded from the underlying view — see <see cref="IBrowserView.PopupRequested"/>.</summary>
    public event EventHandler<string>? PopupRequested
    {
        add => _view.PopupRequested += value;
        remove => _view.PopupRequested -= value;
    }

    /// <summary>Forwarded from the underlying view — see <see cref="IBrowserView.ContextMenuRequested"/>.</summary>
    public event EventHandler<ContextMenuInfo>? ContextMenuRequested
    {
        add => _view.ContextMenuRequested += value;
        remove => _view.ContextMenuRequested -= value;
    }

    public void StartDownload(string url, string? savePath = null) => _view.StartDownload(url, savePath);

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Url { get => _url; private set => Set(ref _url, value); }
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    public bool CanGoBack => _view.CanGoBack;
    public bool CanGoForward => _view.CanGoForward;

    public override string ToString() => string.IsNullOrEmpty(Title) ? "New Tab" : Title;

    public void Navigate(string url) => _view.Navigate(url);
    public void Reload() => _view.Reload();
    public void Back() => _view.GoBack();
    public void Forward() => _view.GoForward();
    public void SetBounds(int x, int y, int width, int height) => _view.SetBounds(x, y, width, height);

    /// <summary>Seconds between auto-reloads while <see cref="AutoReloadSeconds"/> is set; null when off.
    /// Never persisted — a fresh session always starts with auto-reload disabled on every tab.</summary>
    public int? AutoReloadSeconds { get => _autoReloadSeconds; private set => Set(ref _autoReloadSeconds, value); }

    /// <summary>Start (or restart, at a new interval) reloading this tab on a timer. The reload goes
    /// through the same <see cref="Reload"/> path a user-triggered reload would, so a page reached via
    /// POST resubmits its form the same way.</summary>
    public void StartAutoReload(int seconds)
    {
        if (seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        _autoReloadTimer?.Dispose();
        var period = TimeSpan.FromSeconds(seconds);
        _autoReloadTimer = new Timer(_ => Reload(), null, period, period);
        AutoReloadSeconds = seconds;
    }

    public void StopAutoReload()
    {
        _autoReloadTimer?.Dispose();
        _autoReloadTimer = null;
        AutoReloadSeconds = null;
    }

    public void Dispose() => StopAutoReload();

    /// <summary>The off-screen DevTools view for this tab, when open (shell renders it in a pane).</summary>
    public IOffscreenBrowserView? DevToolsView { get; private set; }
    public bool HasDevTools => Offscreen?.HasDevTools ?? false;

    public async Task<IOffscreenBrowserView?> OpenDevToolsAsync(int width, int height)
    {
        DevToolsView = Offscreen is { } osr ? await osr.OpenDevToolsAsync(width, height) : null;
        return DevToolsView;
    }

    public void CloseDevTools()
    {
        Offscreen?.CloseDevTools();
        DevToolsView = null;
    }

    public Task<string> EvaluateJavaScriptAsync(string script) => _view.EvaluateJavaScriptAsync(script);
    internal IBrowserView View => _view;

    /// <summary>Non-null when the engine renders off-screen (the shell owns the surface).</summary>
    public IOffscreenBrowserView? Offscreen => _view as IOffscreenBrowserView;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// Owns every tab and the request contexts they sit on. Standard tabs share one
/// persistent context. Each Sandbox <em>window</em> gets its own in-memory context,
/// disposed (and its RAM released) when the last tab on it closes — leaving no trace.
/// </summary>
public sealed class TabManager : IDisposable
{
    private readonly IBrowserEngine _engine;
    private readonly nint _hostHwnd;
    private readonly Dictionary<Guid, IRequestContext> _sandboxContexts = new();

    public TabManager(IBrowserEngine engine, nint hostHwnd)
    {
        _engine = engine;
        _hostHwnd = hostHwnd;
    }

    public ObservableCollection<TabModel> Tabs { get; } = new();
    public TabModel? Active { get; private set; }

    private readonly HashSet<TabModel> _visible = new();

    /// <summary>Every tab currently painting. With no splits this is just <see cref="Active"/>;
    /// once Multiview panes exist, several tabs can be visible at once (one per pane) — see
    /// <see cref="SetVisible"/>.</summary>
    public IReadOnlyCollection<TabModel> VisibleTabs => _visible;
    public bool IsVisible(TabModel tab) => _visible.Contains(tab);

    public TabModel NewStandardTab(string? url = null)
    {
        var view = _engine.CreateView(_engine.StandardContext, _hostHwnd);
        var tab = new TabModel(view, TabKind.Standard, _engine.StandardContext.Id);
        AddAndActivate(tab);
        view.Navigate(url ?? "diaphane://newtab");
        return tab;
    }

    /// <summary>Open a Sandbox tab. Pass an existing sandbox group id to share its
    /// context (link-through within a window); omit it to start a brand-new isolated one.</summary>
    public TabModel NewSandboxTab(Guid? group = null, string? url = null, string? proxyUri = null)
    {
        IRequestContext ctx;
        if (group is { } g && _sandboxContexts.TryGetValue(g, out var existing))
            ctx = existing;
        else
        {
            ctx = _engine.CreateContext(RequestContextOptions.Sandbox(proxyUri));
            _sandboxContexts[ctx.Id] = ctx;
        }

        var view = _engine.CreateView(ctx, _hostHwnd);
        var tab = new TabModel(view, TabKind.Sandbox, ctx.Id);
        AddAndActivate(tab);
        view.Navigate(url ?? "diaphane://newtab");
        return tab;
    }

    public void Close(TabModel tab)
    {
        Tabs.Remove(tab);
        _visible.Remove(tab);
        tab.Dispose();
        tab.View.Dispose();

        if (tab.IsSandbox && Tabs.All(t => t.ContextId != tab.ContextId)
            && _sandboxContexts.Remove(tab.ContextId, out var ctx))
        {
            ctx.Dispose(); // in-memory partition gone — no history, no cookies, nothing persisted
        }

        if (ReferenceEquals(Active, tab))
            Activate(Tabs.LastOrDefault());
    }

    /// <summary>Reorder tabs to match <paramref name="newOrder"/> (a permutation of
    /// <see cref="Tabs"/>, as produced by a tab-strip drag). Ignored if the sets differ.</summary>
    public void Reorder(IReadOnlyList<TabModel> newOrder)
    {
        if (newOrder.Count != Tabs.Count) return;
        for (int i = 0; i < newOrder.Count; i++)
        {
            var current = Tabs.IndexOf(newOrder[i]);
            if (current < 0) return;
            if (current != i) Tabs.Move(current, i);
        }
    }

    public void Activate(TabModel? tab)
    {
        Active = tab;
        SetVisible(tab is null ? Array.Empty<TabModel>() : new[] { tab });
        tab?.View.SetFocus(true);
    }

    /// <summary>Like <see cref="Activate"/>, but doesn't touch visibility — for the shell layer
    /// to call once it owns computing the full visible set itself (every pinned Multiview pane,
    /// plus whichever tab the one FollowActiveTab leaf shows), rather than have this collapse it
    /// back down to just the tab-strip's selection.</summary>
    public void SetActive(TabModel? tab)
    {
        Active = tab;
        tab?.View.SetFocus(true);
    }

    /// <summary>Marks exactly this set of tabs as visible (painting), hiding every other tab.
    /// Multiview will call this with everything currently shown across every pane; the classic,
    /// never-split case is just <see cref="Activate"/> calling it with a single tab.</summary>
    public void SetVisible(IReadOnlyCollection<TabModel> tabs)
    {
        _visible.Clear();
        foreach (var t in tabs) _visible.Add(t);
        foreach (var t in Tabs) t.View.SetVisible(_visible.Contains(t));
    }

    private void AddAndActivate(TabModel tab)
    {
        Tabs.Add(tab);
        Activate(tab);
    }

    public void Dispose()
    {
        foreach (var t in Tabs) { t.Dispose(); t.View.Dispose(); }
        foreach (var c in _sandboxContexts.Values) c.Dispose();
        _sandboxContexts.Clear();
    }
}
