using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diaphane.Data;
using Diaphane.Shell.Engine;
using Diaphane.Shell.Omnibox;
using Diaphane.Shell.Tabs;

namespace Diaphane.App.Browser;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IBrowserEngine _engine;
    private readonly HistoryStore _history;
    private readonly BookmarkStore _bookmarks;
    private readonly ISearchEngine _search = new DuckDuckGoEngine();
    private TabManager? _tabs;

    [ObservableProperty] private TabModel? _activeTab;
    [ObservableProperty] private string _addressText = "";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<TabModel> Tabs { get; } = new();
    public bool CanGoBack => ActiveTab?.CanGoBack ?? false;
    public bool CanGoForward => ActiveTab?.CanGoForward ?? false;

    public ShellViewModel(IBrowserEngine engine, string dataDir)
    {
        _engine = engine;
        Directory.CreateDirectory(dataDir);
        _history = new HistoryStore(Path.Combine(dataDir, "history.db"));
        _bookmarks = new BookmarkStore(Path.Combine(dataDir, "bookmarks.db"));
    }

    /// <summary>Create the tab manager against a now-valid host window and open the first tab.</summary>
    public void Start(nint hostHwnd)
    {
        if (_tabs is not null) return;
        _tabs = new TabManager(_engine, hostHwnd);
        _tabs.Tabs.CollectionChanged += (_, e) =>
        {
            Tabs.Clear();
            foreach (var t in _tabs!.Tabs) Tabs.Add(t);
        };
        NewTab();
    }

    partial void OnActiveTabChanged(TabModel? oldValue, TabModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnTabPropertyChanged;
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnTabPropertyChanged;
            _tabs?.Activate(newValue);
            AddressText = newValue.Url;
            IsLoading = newValue.IsLoading;
        }
        NotifyNav();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender != ActiveTab) return;
        switch (e.PropertyName)
        {
            case nameof(TabModel.Url):
                if (!string.IsNullOrEmpty(ActiveTab!.Url) && ActiveTab.Url != "diaphane://newtab")
                {
                    AddressText = ActiveTab.Url;
                    _history.RecordVisit(ActiveTab.Url, ActiveTab.Title);
                }
                break;
            case nameof(TabModel.IsLoading):
                IsLoading = ActiveTab!.IsLoading;
                break;
        }
        NotifyNav();
    }

    private void NotifyNav()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void NewTab()
    {
        var t = _tabs!.NewStandardTab("https://duckduckgo.com/");
        ActiveTab = t;
    }

    [RelayCommand]
    public void NewSandboxTab()
    {
        var t = _tabs!.NewSandboxTab(url: "https://duckduckgo.com/");
        ActiveTab = t;
    }

    [RelayCommand]
    public void CloseTab(TabModel tab)
    {
        _tabs?.Close(tab);
        ActiveTab = _tabs?.Active;
    }

    [RelayCommand]
    public void Navigate(string? input)
    {
        var text = input ?? AddressText;
        if (string.IsNullOrWhiteSpace(text) || ActiveTab is null) return;
        var intent = OmniboxParser.Parse(text);
        ActiveTab.Navigate(intent.ToNavigationUrl(_search));
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void GoBack() => ActiveTab?.Back();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    public void GoForward() => ActiveTab?.Forward();

    [RelayCommand]
    public void Reload() => ActiveTab?.Reload();

    [RelayCommand]
    public void ToggleBookmark()
    {
        if (ActiveTab is { Url: { Length: > 0 } url })
            _bookmarks.Add(ActiveTab.Title, url);
    }

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
