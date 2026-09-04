using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diaphane.Data;
using Diaphane.Privacy;
using Diaphane.Shell.Engine;
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
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly SessionStore _session;
    private ISearchEngine _search;
    private TabManager? _tabs;
    private Guid? _sandboxGroup;                 // one in-memory context per window
    private readonly Dictionary<TabModel, string> _lastRecorded = new();

    [ObservableProperty] private TabModel? _activeTab;
    [ObservableProperty] private string _addressText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _activeIsSandbox;
    [ObservableProperty] private bool _showBookmarksBar = true;
    [ObservableProperty] private bool _showPrivacyPanel;
    [ObservableProperty] private bool _showExtensionsPanel;
    [ObservableProperty] private bool _showMediaPanel;
    [ObservableProperty] private bool _showSettingsPanel;
    [ObservableProperty] private bool _showDevTools;

    /// <summary>Raised when a setting that the window must react to (theme) changes.</summary>
    public event Action? SettingsChanged;
    public AppTheme Theme => _settings.Theme;

    public ObservableCollection<TabModel> Tabs { get; } = new();
    public ObservableCollection<Bookmark> BookmarksBar { get; } = new();

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

    public void Start(nint hostHwnd)
    {
        if (_tabs is not null) return;
        _tabs = new TabManager(_engine, hostHwnd);
        _tabs.Tabs.CollectionChanged += (_, _) =>
        {
            Tabs.Clear();
            foreach (var t in _tabs!.Tabs) Tabs.Add(t);
        };
        RefreshBookmarksBar();
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
                OpenPanel(p => ShowMediaPanel = p, "diaphane://media");
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
        ShowPrivacyPanel = ShowExtensionsPanel = ShowMediaPanel = ShowSettingsPanel = false;
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

    [RelayCommand] public void ToggleMediaPanel()
    {
        if (ShowMediaPanel) { ShowMediaPanel = false; ClosePanel(true, "diaphane://media"); }
        else OpenPanel(p => ShowMediaPanel = p, "diaphane://media");
    }
    [RelayCommand] public void CloseMediaPanel()
    {
        ShowMediaPanel = false;
        ClosePanel(true, "diaphane://media");
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

    [RelayCommand] public void ToggleDevTools() => ShowDevTools = !ShowDevTools;

    [RelayCommand(CanExecute = nameof(CanGoBack))] public void GoBack() => ActiveTab?.Back();
    [RelayCommand(CanExecute = nameof(CanGoForward))] public void GoForward() => ActiveTab?.Forward();
    [RelayCommand] public void Reload() => ActiveTab?.Reload();

    // ---- M5: bookmarks + history ----
    [RelayCommand]
    public void ToggleBookmark()
    {
        if (ActiveTab is not { Url: { Length: > 0 } url } || url.StartsWith("about:")) return;
        var existing = _bookmarks.Children(null).FirstOrDefault(b => b.Url == url);
        if (existing is not null) _bookmarks.Remove(existing.Id);
        else _bookmarks.Add(string.IsNullOrEmpty(ActiveTab.Title) ? url : ActiveTab.Title, url);
        RefreshBookmarksBar();
        OnPropertyChanged(nameof(ActiveIsBookmarked));
    }

    public bool ActiveIsBookmarked =>
        ActiveTab is { Url: { Length: > 0 } url } && _bookmarks.Children(null).Any(b => b.Url == url);

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
    public void RemoveBookmark(Bookmark? b)
    {
        if (b is not null) { _bookmarks.Remove(b.Id); RefreshBookmarksBar(); }
    }

    private void RefreshBookmarksBar()
    {
        BookmarksBar.Clear();
        foreach (var b in _bookmarks.Children(null).Where(b => !b.IsFolder)) BookmarksBar.Add(b);
    }

    public IReadOnlyList<VisitEntry> RecentHistory(int n = 200) => _history.Recent(n);

    [RelayCommand]
    public void ClearHistory()
    {
        _history.Clear();
        _lastRecorded.Clear();
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
    }
}
