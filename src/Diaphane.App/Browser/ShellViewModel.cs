using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diaphane.Data;
using Diaphane.Privacy;
using Diaphane.Shell.Engine;
using Diaphane.Shell.Multiview;
using Diaphane.Shell.Omnibox;
using Diaphane.Shell.Settings;
using Diaphane.Shell.Tabs;

namespace Diaphane.App.Browser;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private const string NewTabUrl = "about:blank";

    private readonly IBrowserEngine _engine;
    private readonly HistoryStore _history;
    private readonly BookmarkStore _bookmarks;
    private readonly DownloadStore _downloads;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly SessionStore _session;
    private ISearchEngine _search;
    private TabManager? _tabs;
    private Guid? _sandboxGroup;                 // one in-memory context per window
    private readonly Dictionary<TabModel, string> _lastRecorded = new();
    private readonly Dictionary<long, long> _downloadRowByNativeId = new();

    [ObservableProperty] private TabModel? _activeTab;
    [ObservableProperty] private string _addressText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _activeIsSandbox;
    [ObservableProperty] private bool _showBookmarksBar = true;
    [ObservableProperty] private bool _showPrivacyPanel;
    [ObservableProperty] private bool _showExtensionsPanel;
    [ObservableProperty] private bool _showSettingsPanel;
    [ObservableProperty] private bool _showDevTools;
    [ObservableProperty] private bool _showDownloadsPanel;

    /// <summary>Raised when a setting that the window must react to (theme) changes.</summary>
    public event Action? SettingsChanged;
    public AppTheme Theme => _settings.Theme;

    public ObservableCollection<TabModel> Tabs { get; } = new();

    /// <summary>The content area's split layout — see Diaphane.Shell.Multiview.PaneTree. Never
    /// persisted; every window starts with a single FollowActiveTab leaf (today's only reachable
    /// state until split/pin UI lands in later milestones).</summary>
    public PaneTree Panes { get; } = new();

    /// <summary>Root-level bookmarks and groups, for the left bookmarks panel's TreeView.</summary>
    public ObservableCollection<BookmarkNode> BookmarkTree { get; } = new();

    /// <summary>Most recent first. Never populated by Sandbox-tab downloads.</summary>
    public ObservableCollection<DownloadEntry> Downloads { get; } = new();

    private readonly PrivacyService _privacyService;
    public PrivacyViewModel Privacy { get; }
    public ExtensionsViewModel Extensions { get; }
    public MediaViewModel MediaTools { get; }
    public SettingsViewModel Settings { get; }

    public bool CanGoBack => ActiveTab?.CanGoBack ?? false;
    public bool CanGoForward => ActiveTab?.CanGoForward ?? false;

    public ShellViewModel(IBrowserEngine engine, string dataDir, ExtensionStore extensions)
    {
        _engine = engine;
        Directory.CreateDirectory(dataDir);
        _history = new HistoryStore(Path.Combine(dataDir, "history.db"));
        _bookmarks = new BookmarkStore(Path.Combine(dataDir, "bookmarks.db"));
        _downloads = new DownloadStore(Path.Combine(dataDir, "downloads.db"));

        _settingsStore = new SettingsStore(Path.Combine(dataDir, "settings.json"));
        _settings = _settingsStore.Load();
        _session = new SessionStore(Path.Combine(dataDir, "session.json"));
        _search = SearchEngines.Resolve(_settings.SearchEngineId, _settings.CustomSearchUrl);
        ShowBookmarksBar = _settings.ShowBookmarksBar;

        _privacyService = new PrivacyService(
            new DataClearer(engine.StandardContext, _history),
            new PrivacySettingsStore(Path.Combine(dataDir, "privacy.json")));
        Privacy = new PrivacyViewModel(_privacyService);
        Extensions = new ExtensionsViewModel(extensions);
        MediaTools = new MediaViewModel(() => ActiveTab);
        Settings = new SettingsViewModel(_settingsStore, _settings, OnSettingsChanged);
    }

    private void OnSettingsChanged()
    {
        _search = SearchEngines.Resolve(_settings.SearchEngineId, _settings.CustomSearchUrl);
        OnPropertyChanged(nameof(Theme));
        SettingsChanged?.Invoke();
    }

    public string HomeUrl =>
        string.IsNullOrWhiteSpace(_settings.Homepage) ? NewTabUrl : _settings.Homepage;

    [RelayCommand]
    public void GoHome() => ActiveTab?.Navigate(HomeUrl);

    /// <summary>Persist the open standard tabs for RestoreSession. Call from the window's Closed handler.</summary>
    public void SaveSession()
    {
        if (_settings.Startup != StartupMode.RestoreSession) { _session.Clear(); return; }
        var urls = Tabs.Where(t => !t.IsSandbox).Select(t => t.Url).ToArray();
        var active = ActiveTab is { IsSandbox: false } a ? Array.IndexOf(urls, a.Url) : 0;
        _session.Save(new SavedSession(urls, Math.Max(0, active)));
    }

    /// <summary>Run the configured clear-on-exit wipe. Called from the window's Closed handler.</summary>
    public Task RunClearOnExitAsync() => _privacyService.RunClearOnExitAsync();

    // ---- window geometry + panel sizes ----
    public (int X, int Y, int Width, int Height) WindowGeometry =>
        (_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);

    public double SavedBookmarksPanelWidth => _settings.BookmarksPanelWidth;
    public double SavedDevToolsPanelWidth => _settings.DevToolsPanelWidth;

    /// <summary>Persist window position/size and the current panel widths. Call from the window's
    /// Closed handler. A panel width of 0 (closed) is ignored — the last open width is kept.</summary>
    public void SaveWindowState(int x, int y, int width, int height, double bookmarksWidth, double devToolsWidth)
    {
        _settings.WindowX = x;
        _settings.WindowY = y;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        if (bookmarksWidth >= 1) _settings.BookmarksPanelWidth = bookmarksWidth;
        if (devToolsWidth >= 1) _settings.DevToolsPanelWidth = devToolsWidth;
        _settingsStore.Save(_settings);
    }

    public void Start(nint hostHwnd)
    {
        if (_tabs is not null) return;
        _tabs = new TabManager(_engine, hostHwnd);
        _tabs.Tabs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (TabModel t in e.NewItems)
                {
                    t.DownloadUpdated += (_, p) => OnDownloadUpdated(t, p);
                    t.PopupRequested += (_, url) => OnPopupRequested(t, url);
                }
            Tabs.Clear();
            foreach (var t in _tabs!.Tabs) Tabs.Add(t);
        };
        RefreshBookmarkTree();
        RefreshDownloads();
        OpenStartupTabs();
    }

    private void OpenStartupTabs()
    {
        if (_settings.Startup == StartupMode.RestoreSession)
        {
            var saved = _session.Load();
            if (saved.Urls.Count > 0)
            {
                foreach (var url in saved.Urls) ActiveTab = _tabs!.NewStandardTab(url);
                ActiveTab = Tabs.ElementAtOrDefault(saved.ActiveIndex) ?? Tabs.FirstOrDefault();
                return;
            }
        }

        ActiveTab = _tabs!.NewStandardTab(
            _settings.Startup == StartupMode.Homepage ? HomeUrl : NewTabUrl);
    }

    // ---- active tab ----
    partial void OnActiveTabChanged(TabModel? oldValue, TabModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnTabPropertyChanged;
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnTabPropertyChanged;
            _tabs?.Activate(newValue);
            AddressText = Presentable(newValue.Url);
            IsLoading = newValue.IsLoading;
            ActiveIsSandbox = newValue.IsSandbox;
        }
        OnPropertyChanged(nameof(ActiveIsBookmarked));
        NotifyNav();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TabModel t || !ReferenceEquals(t, ActiveTab)) return;
        switch (e.PropertyName)
        {
            case nameof(TabModel.Url):
                AddressText = Presentable(t.Url);
                MaybeRecordVisit(t);
                OnPropertyChanged(nameof(ActiveIsBookmarked));
                break;
            case nameof(TabModel.Title):
                MaybeRecordVisit(t);
                break;
            case nameof(TabModel.IsLoading):
                IsLoading = t.IsLoading;
                if (!t.IsLoading) MaybeRecordVisit(t);
                break;
        }
        NotifyNav();
    }

    /// <summary>Record a visit once per settled URL. Never for Sandbox tabs or internal schemes.</summary>
    private void MaybeRecordVisit(TabModel t)
    {
        if (t.IsSandbox) return;
        var url = t.Url;
        if (string.IsNullOrEmpty(url) || t.IsLoading) return;
        if (url.StartsWith("about:") || url.StartsWith("data:") || url.StartsWith("diaphane://")
            || url.StartsWith("chrome://")) return;
        if (_lastRecorded.TryGetValue(t, out var prev) && prev == url) return;
        _lastRecorded[t] = url;
        _history.RecordVisit(url, t.Title);
    }

    private static string Presentable(string url) =>
        string.IsNullOrEmpty(url) || url is "about:blank" or "diaphane://newtab" ? "" : url;

    private void NotifyNav()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    // ---- M4: tabs ----
    [RelayCommand]
    public void NewTab()
    {
        ActiveTab = _tabs!.NewStandardTab(NewTabUrl);
    }

    [RelayCommand]
    public void NewSandboxTab()
    {
        // All Sandbox tabs in this window share one ephemeral in-memory context
        // (they can link to each other, exactly like an Incognito window). It is
        // destroyed — and its RAM released — when the last Sandbox tab closes.
        var t = _tabs!.NewSandboxTab(group: _sandboxGroup, url: NewTabUrl);
        _sandboxGroup ??= t.ContextId;
        ActiveTab = t;
    }

    [RelayCommand]
    public void CloseTab(TabModel tab)
    {
        _lastRecorded.Remove(tab);
        _tabs?.Close(tab);
        if (_tabs is not null && _tabs.Tabs.All(x => !x.IsSandbox)) _sandboxGroup = null;
        ActiveTab = _tabs?.Active;
        if (Tabs.Count == 0) NewTab();
    }

    [RelayCommand] public void CloseActiveTab() { if (ActiveTab is { } t) CloseTab(t); }

    [RelayCommand]
    public void NextTab()
    {
        if (Tabs.Count < 2 || ActiveTab is null) return;
        var i = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(i + 1) % Tabs.Count];
    }

    [RelayCommand]
    public void PrevTab()
    {
        if (Tabs.Count < 2 || ActiveTab is null) return;
        var i = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(i - 1 + Tabs.Count) % Tabs.Count];
    }

    /// <summary>Reorder open tabs to match a tab-strip drag. UI-driven, not a user command.</summary>
    public void ReorderTabs(IReadOnlyList<TabModel> newOrder) => _tabs?.Reorder(newOrder);

    // ---- Multiview: split the content area ----
    // Splits whichever pane the live browser is currently in — there's always exactly one
    // FollowActiveTab leaf (SplitLeaf always preserves a leaf's mode on the side that keeps its
    // content, and nothing can turn it into a Pinned leaf until drag-to-pin lands), so "split"
    // needs no separate notion of a focused pane yet.
    [RelayCommand] public void SplitPaneRight() => SplitFollowLeaf(SplitOrientation.SideBySide);
    [RelayCommand] public void SplitPaneDown() => SplitFollowLeaf(SplitOrientation.Stacked);

    private void SplitFollowLeaf(SplitOrientation orientation)
    {
        var leaf = Panes.Leaves().FirstOrDefault(l => l.Mode == LeafMode.FollowActiveTab);
        if (leaf is not null) Panes.SplitLeaf(leaf.Id, orientation);
    }

    // ---- navigation ----
    [RelayCommand]
    public void Navigate(string? input)
    {
        var text = input ?? AddressText;
        if (string.IsNullOrWhiteSpace(text) || ActiveTab is null) return;
        var intent = OmniboxParser.Parse(text);
        if (intent.Kind == OmniboxIntentKind.InternalPage && TryOpenInternalPage(intent.Target))
            return;
        ActiveTab.Navigate(intent.ToNavigationUrl(_search));
    }

    /// <summary>diaphane:// pages are shell UI, not web content. Returns false for unknown names.</summary>
    private bool TryOpenInternalPage(string url)
    {
        var name = url["diaphane://".Length..].TrimEnd('/').ToLowerInvariant();
        switch (name)
        {
            case "privacy":
                OpenPanel(p => ShowPrivacyPanel = p, "diaphane://privacy");
                return true;
            case "settings":
                OpenPanel(p => ShowSettingsPanel = p, "diaphane://settings");
                return true;
            case "extensions":
                OpenPanel(p => ShowExtensionsPanel = p, "diaphane://extensions");
                return true;
            case "media":
            case "codecs":
                OpenPanel(p => ShowSettingsPanel = p, "diaphane://settings"); // now a Settings section
                return true;
            case "newtab":
                return false; // handled as a real (blank) navigation
            default:
                return false;
        }
    }

    // Only one internal panel is visible at a time.
    private void OpenPanel(Action<bool> set, string address)
    {
        ShowPrivacyPanel = ShowExtensionsPanel = ShowSettingsPanel = false;
        set(true);
        AddressText = address;
    }

    private void ClosePanel(bool wasOpen, string address)
    {
        if (AddressText == address) AddressText = Presentable(ActiveTab?.Url ?? "");
    }

    [RelayCommand] public void TogglePrivacyPanel()
    {
        if (ShowPrivacyPanel) { ShowPrivacyPanel = false; ClosePanel(true, "diaphane://privacy"); }
        else OpenPanel(p => ShowPrivacyPanel = p, "diaphane://privacy");
    }
    [RelayCommand] public void ClosePrivacyPanel()
    {
        ShowPrivacyPanel = false;
        ClosePanel(true, "diaphane://privacy");
    }

    [RelayCommand] public void ToggleExtensionsPanel()
    {
        if (ShowExtensionsPanel) { ShowExtensionsPanel = false; ClosePanel(true, "diaphane://extensions"); }
        else OpenPanel(p => ShowExtensionsPanel = p, "diaphane://extensions");
    }
    [RelayCommand] public void CloseExtensionsPanel()
    {
        ShowExtensionsPanel = false;
        ClosePanel(true, "diaphane://extensions");
    }

    [RelayCommand] public void ToggleSettingsPanel()
    {
        if (ShowSettingsPanel) { ShowSettingsPanel = false; ClosePanel(true, "diaphane://settings"); }
        else OpenPanel(p => ShowSettingsPanel = p, "diaphane://settings");
    }
    [RelayCommand] public void CloseSettingsPanel()
    {
        ShowSettingsPanel = false;
        ClosePanel(true, "diaphane://settings");
    }

    // DevTools and the Downloads panel share the right-hand pane — only one at a time.
    [RelayCommand]
    public void ToggleDevTools()
    {
        ShowDevTools = !ShowDevTools;
        if (ShowDevTools) ShowDownloadsPanel = false;
    }

    [RelayCommand]
    public void ToggleDownloadsPanel()
    {
        ShowDownloadsPanel = !ShowDownloadsPanel;
        if (ShowDownloadsPanel) ShowDevTools = false;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))] public void GoBack() => ActiveTab?.Back();
    [RelayCommand(CanExecute = nameof(CanGoForward))] public void GoForward() => ActiveTab?.Forward();
    [RelayCommand] public void Reload() => ActiveTab?.Reload();

    /// <summary>Tab-strip "Reload tab…" — starts (or restarts, at a new interval) a per-tab reload
    /// timer. Not a global setting: each tab keeps its own interval, and none of this is persisted
    /// between sessions — every tab starts with auto-reload off.</summary>
    public void StartTabAutoReload(TabModel tab, int seconds) => tab.StartAutoReload(seconds);
    public void StopTabAutoReload(TabModel tab) => tab.StopAutoReload();

    // ---- bookmarks + history ----
    [RelayCommand]
    public void ToggleBookmark()
    {
        if (ActiveTab is not { Url: { Length: > 0 } url } || url.StartsWith("about:")) return;
        var existing = FindBookmarkByUrl(null, url);
        if (existing is not null) _bookmarks.Remove(existing.Id);
        else _bookmarks.Add(string.IsNullOrEmpty(ActiveTab.Title) ? url : ActiveTab.Title, url);
        RefreshBookmarkTree();
        OnPropertyChanged(nameof(ActiveIsBookmarked));
    }

    public bool ActiveIsBookmarked =>
        ActiveTab is { Url: { Length: > 0 } url } && FindBookmarkByUrl(null, url) is not null;

    /// <summary>Search a folder's children, recursively, for a bookmark with this URL.</summary>
    private Bookmark? FindBookmarkByUrl(long? parentId, string url)
    {
        foreach (var b in _bookmarks.Children(parentId))
        {
            if (!b.IsFolder && b.Url == url) return b;
            if (b.IsFolder && FindBookmarkByUrl(b.Id, url) is { } found) return found;
        }
        return null;
    }

    [RelayCommand] public void ToggleBookmarksBar()
    {
        ShowBookmarksBar = !ShowBookmarksBar;
        _settings.ShowBookmarksBar = ShowBookmarksBar;
        _settingsStore.Save(_settings);
    }

    [RelayCommand]
    public void OpenBookmark(Bookmark? b)
    {
        if (b?.Url is { Length: > 0 } url) { ActiveTab?.Navigate(url); AddressText = url; }
    }

    [RelayCommand]
    public void OpenBookmarkInNewTab(Bookmark? b)
    {
        if (b?.Url is { Length: > 0 } url) ActiveTab = _tabs!.NewStandardTab(url);
    }

    [RelayCommand]
    public void RemoveBookmark(Bookmark? b)
    {
        if (b is null) return;
        _bookmarks.Remove(b.Id); // cascades to any children of a removed group
        RefreshBookmarkTree();
        OnPropertyChanged(nameof(ActiveIsBookmarked));
    }

    /// <summary>Create a new group (folder) under <paramref name="parentId"/>, or at the root.</summary>
    public void CreateGroup(long? parentId, string title)
    {
        _bookmarks.Add(title, null, parentId, isFolder: true);
        RefreshBookmarkTree();
    }

    /// <summary>Rename a group or a bookmark (title only — a bookmark's URL is untouched).</summary>
    public void RenameBookmark(Bookmark b, string title)
    {
        _bookmarks.Update(b.Id, title, b.Url);
        RefreshBookmarkTree();
    }

    /// <summary>Retitle and/or repoint a bookmark.</summary>
    public void EditBookmark(Bookmark b, string title, string url)
    {
        _bookmarks.Update(b.Id, title, url);
        RefreshBookmarkTree();
        OnPropertyChanged(nameof(ActiveIsBookmarked));
    }

    /// <summary>Open every bookmark in a group (recursively, including nested groups), each in its own
    /// new tab — standard or Sandbox. Sandbox tabs share this window's one ephemeral context, same as
    /// the New Sandbox tab action.</summary>
    public void OpenGroupInNewTabs(BookmarkNode group, bool sandbox)
    {
        foreach (var url in CollectUrls(group))
        {
            if (sandbox)
            {
                var t = _tabs!.NewSandboxTab(group: _sandboxGroup, url: url);
                _sandboxGroup ??= t.ContextId;
                ActiveTab = t;
            }
            else
            {
                ActiveTab = _tabs!.NewStandardTab(url);
            }
        }
    }

    private static IEnumerable<string> CollectUrls(BookmarkNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.IsFolder)
                foreach (var url in CollectUrls(child)) yield return url;
            else if (child.Model.Url is { Length: > 0 } url)
                yield return url;
        }
    }

    /// <summary>Drag-and-drop reparent. Refuses to move a group into itself or one of its own descendants.</summary>
    public void MoveBookmark(long id, long? newParentId)
    {
        if (id == newParentId || IsDescendantOf(newParentId, id)) return;
        _bookmarks.MoveTo(id, newParentId);
        RefreshBookmarkTree();
    }

    private bool IsDescendantOf(long? candidateId, long ancestorId)
    {
        for (var current = candidateId; current is { } id; current = _bookmarks.Get(id)?.ParentId)
            if (id == ancestorId) return true;
        return false;
    }

    private void RefreshBookmarkTree()
    {
        BookmarkTree.Clear();
        foreach (var node in BuildBookmarkNodes(null)) BookmarkTree.Add(node);
    }

    private List<BookmarkNode> BuildBookmarkNodes(long? parentId)
    {
        var nodes = new List<BookmarkNode>();
        foreach (var b in _bookmarks.Children(parentId))
        {
            var node = new BookmarkNode { Model = b };
            if (b.IsFolder)
                foreach (var child in BuildBookmarkNodes(b.Id)) node.Children.Add(child);
            nodes.Add(node);
        }
        return nodes;
    }

    public IReadOnlyList<VisitEntry> RecentHistory(int n = 200) => _history.Recent(n);

    [RelayCommand]
    public void ClearHistory()
    {
        _history.Clear();
        _lastRecorded.Clear();
    }

    // ---- downloads ----
    // Never persisted for Sandbox tabs — same "no disk trace" rule as history/favicons.
    // The downloaded file itself still lands on disk (that's the point); only the
    // record of it stays out of the store.
    private void OnDownloadUpdated(TabModel tab, DownloadProgress p)
    {
        if (tab.IsSandbox) return;

        // CEF's "suggested" filename is reliable in OnBeforeDownload but comes back empty
        // on the OnDownloadUpdated progress events this is actually built from — fall back
        // to the resolved file path's own name, then the URL, rather than storing blank.
        var fileName = ResolveFileName(p.FileName, p.FilePath, p.Url);
        var state = (Diaphane.Data.DownloadState)(int)p.State;
        if (_downloadRowByNativeId.TryGetValue(p.NativeId, out var rowId))
            _downloads.UpdateProgress(rowId, fileName, p.FilePath, p.ReceivedBytes, p.TotalBytes, state);
        else
            _downloadRowByNativeId[p.NativeId] =
                _downloads.Start(p.Url, fileName, p.FilePath, p.TotalBytes);

        RefreshDownloads();
    }

    private static string ResolveFileName(string suggested, string filePath, string url)
    {
        if (!string.IsNullOrWhiteSpace(suggested)) return suggested;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var name = Path.GetFileName(filePath);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name)) return Uri.UnescapeDataString(name);
        }
        return "download";
    }

    private void RefreshDownloads()
    {
        Downloads.Clear();
        foreach (var d in _downloads.Recent()) Downloads.Add(d);
    }

    /// <summary>A page tried to open a new window/tab (target="_blank", window.open(), …).
    /// The engine already cancelled the popup itself; open it as a real tab instead — a
    /// Sandbox opener's popup stays in that same Sandbox context, never leaks to a standard tab.</summary>
    private void OnPopupRequested(TabModel opener, string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "about:blank") return;
        ActiveTab = opener.IsSandbox
            ? _tabs!.NewSandboxTab(group: opener.ContextId, url: url)
            : _tabs!.NewStandardTab(url);
    }

    [RelayCommand]
    public void RemoveDownload(DownloadEntry? d)
    {
        if (d is null) return;
        _downloads.Remove(d.Id);
        RefreshDownloads();
    }

    [RelayCommand]
    public void ClearDownloads()
    {
        _downloads.Clear();
        _downloadRowByNativeId.Clear();
        RefreshDownloads();
    }

    // ---- omnibox ----
    public IReadOnlyList<Suggestion> Suggest(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return Array.Empty<Suggestion>();
        var list = new List<Suggestion>();
        list.AddRange(_bookmarks.Suggest(typed, 4));
        list.AddRange(_history.Suggest(typed, 6));
        return list
            .GroupBy(s => s.Url)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(s => s.Score)
            .Take(8)
            .ToList();
    }

    public void Dispose()
    {
        _tabs?.Dispose();
        _history.Dispose();
        _bookmarks.Dispose();
        _downloads.Dispose();
    }
}
