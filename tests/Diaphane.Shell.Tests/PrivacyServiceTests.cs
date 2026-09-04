using Diaphane.Data;
using Diaphane.Privacy;
using Xunit;

namespace Diaphane.Shell.Tests;

public sealed class PrivacyServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "diaphane-test-" + Guid.NewGuid().ToString("N"));
    private readonly FakeContext _ctx = new(persistent: true);
    private readonly HistoryStore _history;
    private readonly PrivacySettingsStore _store;
    private readonly DateTimeOffset _now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    public PrivacyServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _history = new HistoryStore(Path.Combine(_dir, "history.db"));
        _store = new PrivacySettingsStore(Path.Combine(_dir, "privacy.json"));
    }

    private PrivacyService NewService() =>
        new(new DataClearer(_ctx, _history), _store, () => _now);

    [Fact]
    public async Task ClearAsync_history_everything_removes_all_rows()
    {
        _history.RecordVisit("https://a.example", "A");
        _history.RecordVisit("https://b.example", "B");

        await NewService().ClearAsync(ClearScope.History, ClearTimeRange.Everything);

        Assert.Empty(_history.Recent());
    }

    [Fact]
    public async Task ClearAsync_maps_scope_flags_to_context_calls()
    {
        await NewService().ClearAsync(ClearScope.Cookies | ClearScope.HttpCache | ClearScope.DnsCache,
            ClearTimeRange.Everything);

        Assert.Equal(1, _ctx.CookiesCleared);
        Assert.Equal(1, _ctx.HttpCacheCleared);
        Assert.Equal(1, _ctx.DnsFlushed);
        Assert.Equal(0, _ctx.StorageCleared);
    }

    [Fact]
    public async Task ClearAsync_lastHour_passes_a_one_hour_cutoff()
    {
        await NewService().ClearAsync(ClearScope.Cookies, ClearTimeRange.LastHour);
        Assert.Equal(_now.AddHours(-1), _ctx.LastSince);
    }

    [Fact]
    public async Task ClearAsync_everything_passes_no_cutoff()
    {
        await NewService().ClearAsync(ClearScope.Cookies, ClearTimeRange.Everything);
        Assert.Null(_ctx.LastSince);
    }

    [Fact]
    public async Task ClearAsync_none_is_a_noop()
    {
        await NewService().ClearAsync(ClearScope.None, ClearTimeRange.Everything);
        Assert.Equal(0, _ctx.CookiesCleared);
        Assert.Equal(0, _ctx.HttpCacheCleared);
    }

    [Fact]
    public void SetClearOnExit_persists_across_store_instances()
    {
        NewService().SetClearOnExit(ClearScope.Cookies | ClearScope.History);

        var reloaded = new PrivacySettingsStore(_store.Path).Load();
        Assert.Equal(ClearScope.Cookies | ClearScope.History, reloaded.ClearOnExit);
    }

    [Fact]
    public async Task RunClearOnExitAsync_noop_when_disabled()
    {
        await NewService().RunClearOnExitAsync();
        Assert.Equal(0, _ctx.CookiesCleared);
    }

    [Fact]
    public async Task RunClearOnExitAsync_runs_configured_scope_with_no_time_bound()
    {
        _history.RecordVisit("https://a.example", "A");
        var svc = NewService();
        svc.SetClearOnExit(ClearScope.Cookies | ClearScope.History);

        await svc.RunClearOnExitAsync();

        Assert.Equal(1, _ctx.CookiesCleared);
        Assert.Null(_ctx.LastSince);
        Assert.Empty(_history.Recent());
    }

    [Fact]
    public void SaveDefaults_round_trips_scope_and_range()
    {
        NewService().SaveDefaults(ClearScope.HttpCache, ClearTimeRange.LastWeek);

        var reloaded = new PrivacySettingsStore(_store.Path).Load();
        Assert.Equal(ClearScope.HttpCache, reloaded.DefaultClearScope);
        Assert.Equal(ClearTimeRange.LastWeek, reloaded.DefaultTimeRange);
    }

    public void Dispose()
    {
        _history.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
