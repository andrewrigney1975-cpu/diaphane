using System.Collections.Concurrent;
using Diaphane.Shell.Engine;
using static Diaphane.Core.NativeMethods;

namespace Diaphane.Core;

public sealed record CefEngineOptions(
    string ResourcesDir,
    string LocalesDir,
    string SubprocessPath,
    string RootCacheDir,
    bool Windowless = false,
    bool NoSandbox = false,
    string? UserAgent = null,
    IReadOnlyList<string>? ExtensionDirs = null,
    bool AllowWidevine = false,
    bool EnableDevTools = true);

/// <summary>
/// The real <see cref="IBrowserEngine"/> — a thin managed shell over DiaphaneCore.dll.
/// Every member must be called from the single thread that also calls
/// <see cref="DoMessageLoopWork"/> (CEF's UI thread).
/// </summary>
public sealed class CefEngine : IBrowserEngine, IDisposable
{
    private readonly SchedulePumpCb _pumpCb;           // rooted for the process lifetime
    private readonly ConcurrentDictionary<string, CefBrowserView> _views = new();
    private CefRequestContext? _standard;
    private bool _initialized;

    public CefEngine(CefEngineOptions o)
    {
        _pumpCb = (delayMs, _) => ScheduleMessagePump?.Invoke(this, delayMs);

        var settings = new Settings
        {
            ResourcesDir = o.ResourcesDir,
            LocalesDir = o.LocalesDir,
            SubprocessPath = o.SubprocessPath,
            RootCacheDir = o.RootCacheDir,
            UserAgent = o.UserAgent,
            Windowless = o.Windowless ? 1 : 0,
            NoSandbox = o.NoSandbox ? 1 : 0,
            ExtensionDirs = o.ExtensionDirs is { Count: > 0 } dirs ? string.Join(';', dirs) : null,
            AllowWidevine = o.AllowWidevine ? 1 : 0,
            Devtools = o.EnableDevTools ? 1 : 0,
        };

        if (dc_initialize(settings, _pumpCb, IntPtr.Zero) == 0)
            throw new InvalidOperationException("CefInitialize failed (see DiaphaneCore/CEF logs).");
        _initialized = true;
    }

    public string Version => PtrToUtf8(dc_version());

    public event EventHandler<int>? ScheduleMessagePump;

    public void DoMessageLoopWork() => dc_pump();

    public IRequestContext StandardContext =>
        _standard ??= new CefRequestContext(PtrToUtf8(dc_context_standard()), persistent: true, owned: false);

    public IRequestContext CreateContext(RequestContextOptions options)
    {
        var p = dc_context_create(options.Persistent ? 1 : 0, options.CachePath, options.ProxyUri);
        if (p == IntPtr.Zero) throw new InvalidOperationException("CefRequestContext::CreateContext failed.");
        return new CefRequestContext(PtrToUtf8(p), options.Persistent, owned: true);
    }

    public IBrowserView CreateView(IRequestContext context, nint hostHwnd)
    {
        var ctxId = ((CefRequestContext)context).Id2;
        return Register(new CefBrowserView(ctxId, hostHwnd) { Engine = this });
    }

    /// <summary>A plain windowless view on the global context — used for the DevTools front-end pane.</summary>
    internal CefBrowserView CreateRawView(int width, int height) =>
        Register(new CefBrowserView("", IntPtr.Zero, width, height) { Engine = this });

    private CefBrowserView Register(CefBrowserView view)
    {
        _views[view.NativeId] = view;
        view.Closed += (_, _) => _views.TryRemove(view.NativeId, out _);
        return view;
    }

    /// <summary>
    /// Resolve the DevTools front-end URL for the page currently at
    /// <paramref name="inspectedUrl"/> via the loopback debugging endpoint.
    /// Best-effort and synchronous (short timeout); returns null if unavailable.
    /// </summary>
    internal string? ResolveDevToolsFrontendUrl(string inspectedUrl)
    {
        var port = dc_devtools_port();
        if (port <= 0) return null;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = http.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            System.Text.Json.JsonElement? pick = null;
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                if (t.TryGetProperty("type", out var ty) && ty.GetString() != "page") continue;
                pick ??= t;   // fall back to the first page target
                if (t.TryGetProperty("url", out var u) && u.GetString() == inspectedUrl) { pick = t; break; }
            }
            if (pick is not { } target) return null;

            var fe = target.TryGetProperty("devtoolsFrontendUrl", out var feEl) ? feEl.GetString() : null;
            var ws = target.TryGetProperty("webSocketDebuggerUrl", out var wsEl) ? wsEl.GetString() : null;
            if (string.IsNullOrEmpty(fe) && !string.IsNullOrEmpty(ws))
                fe = $"/devtools/inspector.html?ws={ws!["ws://".Length..]}";
            if (string.IsNullOrEmpty(fe)) return null;
            return fe!.StartsWith("http") ? fe : $"http://127.0.0.1:{port}{fe}";
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_initialized) return;
        foreach (var v in _views.Values) v.Dispose();
        _views.Clear();
        dc_shutdown();
        _initialized = false;
    }
}

internal sealed class CefRequestContext(string id, bool persistent, bool owned) : IRequestContext
{
    private readonly List<ExtensionInfo> _extensions = new();

    public Guid Id { get; } = DeterministicGuid(id);
    internal string Id2 { get; } = id;
    public bool IsPersistent => persistent;
    public IReadOnlyList<ExtensionInfo> Extensions => _extensions;

    public Task ClearCookiesAsync(DateTimeOffset? since = null)
    {
        dc_context_clear_cookies(Id2, since?.ToUnixTimeSeconds() ?? 0);
        return Task.CompletedTask;
    }

    public Task ClearStorageAsync(DateTimeOffset? since = null)
    {
        dc_context_clear_storage(Id2, since?.ToUnixTimeSeconds() ?? 0);
        return Task.CompletedTask;
    }

    public Task ClearHttpCacheAsync()
    {
        dc_context_clear_cache(Id2);
        return Task.CompletedTask;
    }

    public Task FlushDnsAsync()
    {
        dc_context_flush_dns(Id2);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Parse an unpacked extension's manifest and record it. The engine actually
    /// loads enabled extensions at process start via --load-extension, so a
    /// freshly-added one takes effect on the next launch.
    /// </summary>
    public Task<ExtensionInfo> LoadExtensionAsync(string path)
    {
        var m = Diaphane.Shell.Extensions.ExtensionManifest.FromDirectory(path);
        var info = new ExtensionInfo(m.Id, m.Name, m.Version, Enabled: true, m.Permissions);
        _extensions.RemoveAll(e => e.Id == m.Id);
        _extensions.Add(info);
        return Task.FromResult(info);
    }

    public void Dispose()
    {
        if (owned) dc_context_release(Id2);
    }

    private static Guid DeterministicGuid(string s)
    {
        Span<byte> b = stackalloc byte[16];
        System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(s)).AsSpan(0, 16).CopyTo(b);
        return new Guid(b);
    }
}
