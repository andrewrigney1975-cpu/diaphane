using Diaphane.Shell.Omnibox;
using Diaphane.Shell.Settings;
using Xunit;

namespace Diaphane.Shell.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "diaphane-set-" + Guid.NewGuid().ToString("N"));
    public SettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void AppSettings_round_trip_through_the_store()
    {
        var path = Path.Combine(_dir, "settings.json");
        new SettingsStore(path).Save(new AppSettings
        {
            Startup = StartupMode.RestoreSession,
            Homepage = "https://example.org",
            SearchEngineId = "brave",
            Theme = AppTheme.Dark,
            ShowBookmarksBar = false,
            WindowX = 120,
            WindowY = 80,
            WindowWidth = 1600,
            WindowHeight = 1000,
            BookmarksPanelWidth = 320,
            DevToolsPanelWidth = 420,
        });

        var loaded = new SettingsStore(path).Load();
        Assert.Equal(StartupMode.RestoreSession, loaded.Startup);
        Assert.Equal("https://example.org", loaded.Homepage);
        Assert.Equal("brave", loaded.SearchEngineId);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.False(loaded.ShowBookmarksBar);
        Assert.Equal(120, loaded.WindowX);
        Assert.Equal(80, loaded.WindowY);
        Assert.Equal(1600, loaded.WindowWidth);
        Assert.Equal(1000, loaded.WindowHeight);
        Assert.Equal(320, loaded.BookmarksPanelWidth);
        Assert.Equal(420, loaded.DevToolsPanelWidth);
    }

    [Fact]
    public void AppSettings_defaults_are_first_run_friendly()
    {
        var defaults = new AppSettings();
        Assert.Equal(int.MinValue, defaults.WindowX);
        Assert.Equal(int.MinValue, defaults.WindowY);
        Assert.Equal(1400, defaults.WindowWidth);
        Assert.Equal(900, defaults.WindowHeight);
        Assert.Equal(260, defaults.BookmarksPanelWidth);
        Assert.Equal(0, defaults.DevToolsPanelWidth);
    }

    [Fact]
    public void SettingsStore_returns_defaults_when_missing_or_corrupt()
    {
        Assert.Equal(StartupMode.Blank, new SettingsStore(Path.Combine(_dir, "nope.json")).Load().Startup);

        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "{ not json");
        Assert.Equal("duckduckgo", new SettingsStore(bad).Load().SearchEngineId);
    }

    [Fact]
    public void SearchEngines_resolve_builtin_and_custom()
    {
        Assert.Equal("https://search.brave.com/search?q=hello%20world",
            SearchEngines.Resolve("brave").BuildSearchUrl("hello world"));

        // unknown id falls back to the first engine
        Assert.Equal("DuckDuckGo", SearchEngines.Resolve("bogus").Name);

        Assert.Equal("https://x.test/s?query=a%2Fb",
            SearchEngines.Resolve("custom", "https://x.test/s?query={q}").BuildSearchUrl("a/b"));
    }

    [Fact]
    public void SessionStore_filters_non_restorable_urls_and_clamps_index()
    {
        var path = Path.Combine(_dir, "session.json");
        var store = new SessionStore(path);
        store.Save(new SavedSession(
            new[] { "https://a.example", "about:blank", "diaphane://settings", "https://b.example" }, 9));

        var s = store.Load();
        Assert.Equal(new[] { "https://a.example", "https://b.example" }, s.Urls);
        Assert.Equal(1, s.ActiveIndex);
    }

    [Fact]
    public void SessionStore_save_with_no_restorable_urls_clears_the_file()
    {
        var path = Path.Combine(_dir, "session2.json");
        var store = new SessionStore(path);
        store.Save(new SavedSession(new[] { "https://a.example" }, 0));
        store.Save(new SavedSession(new[] { "about:blank" }, 0));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void UpdateChecker_compares_versions()
    {
        Assert.True(UpdateChecker.IsNewer("1.2.4", "1.2.3"));
        Assert.False(UpdateChecker.IsNewer("1.2.3", "1.2.3"));
        Assert.False(UpdateChecker.IsNewer("garbage", "1.2.3"));
    }

    [Fact]
    public async Task UpdateChecker_reads_the_manifest()
    {
        var checker = new UpdateChecker("0.10.0",
            _ => Task.FromResult("""{ "version": "0.11.0", "url": "https://dl.example/diaphane" }"""));

        var info = await checker.CheckAsync("https://manifest.example");
        Assert.NotNull(info);
        Assert.Equal("0.11.0", info!.LatestVersion);
        Assert.True(info.IsNewer);
        Assert.Equal("https://dl.example/diaphane", info.DownloadUrl);
    }

    [Fact]
    public async Task UpdateChecker_returns_null_for_an_empty_url()
    {
        var checker = new UpdateChecker("1.0.0", _ => throw new InvalidOperationException("should not fetch"));
        Assert.Null(await checker.CheckAsync(""));
    }
}
