using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diaphane.Privacy;

namespace Diaphane.App.Browser;

/// <summary>
/// Drives the "Clear browsing data" panel (diaphane://privacy). All wiping goes
/// through <see cref="PrivacyService"/>; this only holds the checkbox/range UI state.
/// </summary>
public sealed partial class PrivacyViewModel : ObservableObject
{
    private readonly PrivacyService _service;

    public PrivacyViewModel(PrivacyService service)
    {
        _service = service;

        var d = service.Settings.DefaultClearScope;
        _clearCookies     = d.HasFlag(ClearScope.Cookies);
        _clearSiteStorage = d.HasFlag(ClearScope.SiteStorage);
        _clearHttpCache   = d.HasFlag(ClearScope.HttpCache);
        _clearHistory     = d.HasFlag(ClearScope.History);
        _clearDnsCache    = d.HasFlag(ClearScope.DnsCache);
        _timeRangeIndex   = (int)service.Settings.DefaultTimeRange;
        _clearOnExit      = service.Settings.ClearOnExit != ClearScope.None;
        _lastResult       = "";
    }

    public string[] TimeRanges { get; } =
        Enum.GetValues<ClearTimeRange>().Select(r => r.Label()).ToArray();

    [ObservableProperty] private bool _clearCookies;
    [ObservableProperty] private bool _clearSiteStorage;
    [ObservableProperty] private bool _clearHttpCache;
    [ObservableProperty] private bool _clearHistory;
    [ObservableProperty] private bool _clearDnsCache;
    [ObservableProperty] private int _timeRangeIndex;
    [ObservableProperty] private bool _clearOnExit;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _lastResult;

    private ClearScope Scope =>
        (ClearCookies     ? ClearScope.Cookies     : 0) |
        (ClearSiteStorage ? ClearScope.SiteStorage : 0) |
        (ClearHttpCache   ? ClearScope.HttpCache   : 0) |
        (ClearHistory     ? ClearScope.History     : 0) |
        (ClearDnsCache    ? ClearScope.DnsCache    : 0);

    private ClearTimeRange Range => (ClearTimeRange)TimeRangeIndex;

    partial void OnClearOnExitChanged(bool value)
    {
        // "Clear on exit" always wipes the full standard-context footprint —
        // a time window on exit makes no sense.
        _service.SetClearOnExit(value
            ? ClearScope.Cookies | ClearScope.SiteStorage | ClearScope.HttpCache | ClearScope.History | ClearScope.DnsCache
            : ClearScope.None);
    }

    [RelayCommand]
    private async Task ClearNowAsync()
    {
        if (Busy || Scope == ClearScope.None) { LastResult = "Nothing selected."; return; }
        Busy = true;
        LastResult = "";
        try
        {
            await _service.ClearAsync(Scope, Range);
            _service.SaveDefaults(Scope, Range);
            LastResult = $"Cleared {DescribeScope(Scope)} ({TimeRanges[TimeRangeIndex]}).";
        }
        catch (Exception ex)
        {
            LastResult = "Clear failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private static string DescribeScope(ClearScope s)
    {
        var parts = new List<string>();
        if (s.HasFlag(ClearScope.Cookies)) parts.Add("cookies");
        if (s.HasFlag(ClearScope.SiteStorage)) parts.Add("site storage");
        if (s.HasFlag(ClearScope.HttpCache)) parts.Add("cache");
        if (s.HasFlag(ClearScope.History)) parts.Add("history");
        if (s.HasFlag(ClearScope.DnsCache)) parts.Add("connections");
        return string.Join(", ", parts);
    }
}
