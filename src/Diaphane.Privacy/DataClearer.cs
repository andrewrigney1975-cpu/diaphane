using Diaphane.Data;
using Diaphane.Shell.Engine;

namespace Diaphane.Privacy;

[Flags]
public enum ClearScope
{
    None        = 0,
    Cookies     = 1 << 0,
    SiteStorage = 1 << 1,
    HttpCache   = 1 << 2,
    History     = 1 << 3,
    DnsCache    = 1 << 4,
    All         = Cookies | SiteStorage | HttpCache | History | DnsCache,
}

public sealed record ClearRequest(ClearScope Scope, DateTimeOffset? Since = null);

/// <summary>
/// Reaches every store from a single call. Used by "Clear now", "Clear on exit",
/// and the panic hotkey. Sandbox contexts are never passed here — they hold
/// nothing on disk and vanish on close.
/// </summary>
public sealed class DataClearer(IRequestContext standardContext, HistoryStore history)
{
    public async Task ClearAsync(ClearRequest req)
    {
        var s = req.Scope;
        if (s.HasFlag(ClearScope.Cookies))     await standardContext.ClearCookiesAsync(req.Since);
        if (s.HasFlag(ClearScope.SiteStorage)) await standardContext.ClearStorageAsync(req.Since);
        if (s.HasFlag(ClearScope.HttpCache))   await standardContext.ClearHttpCacheAsync();
        if (s.HasFlag(ClearScope.DnsCache))    await standardContext.FlushDnsAsync();
        if (s.HasFlag(ClearScope.History))     history.Clear(req.Since);
    }
}

/// <summary>Wire ClearOnExit into the app's last-window-closed / process-exit path.</summary>
public sealed class ClearOnExitPolicy
{
    public ClearScope Scope { get; set; } = ClearScope.None;
    public bool Enabled => Scope != ClearScope.None;

    public Task RunAsync(DataClearer clearer) =>
        Enabled ? clearer.ClearAsync(new ClearRequest(Scope)) : Task.CompletedTask;
}
