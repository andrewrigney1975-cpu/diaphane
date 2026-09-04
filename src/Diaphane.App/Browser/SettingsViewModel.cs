using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diaphane.Shell.Omnibox;
using Diaphane.Shell.Settings;

namespace Diaphane.App.Browser;

/// <summary>diaphane://settings — general preferences. Changes persist immediately;
/// a few (startup mode) take effect next launch, flagged in the UI.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppSettings _s;
    private readonly Action _onChanged;      // let the shell re-read search engine / theme
    private bool _loading;

    public SettingsViewModel(SettingsStore store, AppSettings settings, Action onChanged)
    {
        _store = store;
        _s = settings;
        _onChanged = onChanged;

        _loading = true;
        _startupIndex     = (int)_s.Startup;
        _homepage         = _s.Homepage;
        _themeIndex       = (int)_s.Theme;
        _searchEngineIndex = Math.Max(0, SearchEngineIds.IndexOf(_s.SearchEngineId));
        _customSearchUrl  = _s.CustomSearchUrl;
        _downloadDirectory = _s.DownloadDirectory;
        _updateManifestUrl = _s.UpdateManifestUrl;
        _updateStatus     = "";
        _loading = false;
    }

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    public string EngineVersion { get; set; } = "";

    public string[] StartupModes { get; } = { "Open a blank page", "Open my homepage", "Restore my last session" };
    public string[] Themes { get; } = { "Match Windows", "Light", "Dark" };

    public IReadOnlyList<string> SearchEngineNames { get; } =
        SearchEngines.Builtin.Select(e => e.Name).Append("Custom…").ToArray();
    private static readonly List<string> SearchEngineIds =
        SearchEngines.Builtin.Select(e => e.Id).Append(SearchEngines.CustomId).ToList();

    [ObservableProperty] private int _startupIndex;
    [ObservableProperty] private string _homepage;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private int _searchEngineIndex;
    [ObservableProperty] private string _customSearchUrl;
    [ObservableProperty] private string _downloadDirectory;
    [ObservableProperty] private string _updateManifestUrl;
    [ObservableProperty] private string _updateStatus;
    [ObservableProperty] private bool _checkingUpdate;
    [ObservableProperty] private string? _updateDownloadUrl;

    public bool IsCustomSearch => SearchEngineIndex == SearchEngineIds.Count - 1;

    partial void OnStartupIndexChanged(int value) { _s.Startup = (StartupMode)value; Persist(); }
    partial void OnHomepageChanged(string value) { _s.Homepage = value?.Trim() ?? ""; Persist(); }
    partial void OnThemeIndexChanged(int value) { _s.Theme = (AppTheme)value; Persist(); _onChanged(); }
    partial void OnUpdateManifestUrlChanged(string value) { _s.UpdateManifestUrl = value?.Trim() ?? ""; Persist(); }
    partial void OnDownloadDirectoryChanged(string value) { _s.DownloadDirectory = value?.Trim() ?? ""; Persist(); _onChanged(); }

    partial void OnSearchEngineIndexChanged(int value)
    {
        _s.SearchEngineId = SearchEngineIds[Math.Clamp(value, 0, SearchEngineIds.Count - 1)];
        OnPropertyChanged(nameof(IsCustomSearch));
        Persist();
        _onChanged();
    }

    partial void OnCustomSearchUrlChanged(string value)
    {
        _s.CustomSearchUrl = value?.Trim() ?? "";
        Persist();
        if (IsCustomSearch) _onChanged();
    }

    private void Persist()
    {
        if (_loading) return;
        _store.Save(_s);
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (CheckingUpdate) return;
        UpdateDownloadUrl = null;

        if (string.IsNullOrWhiteSpace(_s.UpdateManifestUrl))
        {
            UpdateStatus = "Set an update manifest URL first. diaphane never checks on its own.";
            return;
        }

        CheckingUpdate = true;
        UpdateStatus = "Checking…";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var checker = new UpdateChecker(Version, url => http.GetStringAsync(url));
            var result = await checker.CheckAsync(_s.UpdateManifestUrl);

            UpdateStatus = result switch
            {
                null => "Couldn't read the manifest.",
                { IsNewer: true } r => $"Version {r.LatestVersion} is available (you have {Version}).",
                { } r => $"You're up to date ({Version}; latest is {r.LatestVersion}).",
            };
            if (result is { IsNewer: true, DownloadUrl.Length: > 0 })
                UpdateDownloadUrl = result.DownloadUrl;
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update check failed: " + ex.Message;
        }
        finally
        {
            CheckingUpdate = false;
        }
    }
}
