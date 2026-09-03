using Diaphane.Core;
using Microsoft.UI.Dispatching;

namespace Diaphane.App.Browser;

/// <summary>
/// Owns the process-wide <see cref="CefEngine"/> and pumps its message loop on
/// the WinUI dispatcher (CEF runs in external-message-pump mode). The WinUI UI
/// thread is STA, which is what CEF requires.
/// </summary>
public sealed class CefHost : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _pumpTimer;

    public CefEngine Engine { get; }

    public CefHost(DispatcherQueue dispatcher, string nativeBinDir)
    {
        _dispatcher = dispatcher;

        NativeLoader.Use(nativeBinDir);
        Engine = new CefEngine(new CefEngineOptions(
            ResourcesDir: NativeLoader.ResourcesDir,
            LocalesDir: NativeLoader.LocalesDir,
            SubprocessPath: NativeLoader.SubprocessPath,
            RootCacheDir: EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Diaphane", "UserData")),
            Windowless: true,    // OSR — the shell owns the surface (windowed hosting hits the GPU child-window NOTREACHED)
            NoSandbox: true));    // TODO(M-later): validate the CEF sandbox + helper, then flip.

        _pumpTimer = _dispatcher.CreateTimer();
        _pumpTimer.IsRepeating = false;
        _pumpTimer.Tick += (_, _) => Engine.DoMessageLoopWork();

        Engine.ScheduleMessagePump += (_, delayMs) =>
        {
            // CEF asks to be pumped after |delayMs|. Coalesce onto one one-shot timer.
            _dispatcher.TryEnqueue(() =>
            {
                _pumpTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(delayMs, 0, 33));
                _pumpTimer.Start();
            });
        };

        // A steady heartbeat keeps CEF responsive even when OnScheduleMessagePumpWork
        // is quiet (and is essential during browser creation — the render-process
        // handshake times out if the loop is starved).
        var heartbeat = _dispatcher.CreateTimer();
        heartbeat.Interval = TimeSpan.FromMilliseconds(33);
        heartbeat.IsRepeating = true;
        heartbeat.Tick += (_, _) => Engine.DoMessageLoopWork();
        heartbeat.Start();
    }

    public void Dispose() => Engine.Dispose();

    private static string EnsureDir(string p) { Directory.CreateDirectory(p); return p; }

    /// <summary>Best-effort locate of the DiaphaneCore staging dir for a dev run.</summary>
    public static string ResolveNativeBinDir()
    {
        var configured = Environment.GetEnvironmentVariable("DIAPHANE_CEF_BIN");
        if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "DiaphaneCore.dll")))
            return configured;

        // walk up from the app dir looking for src/Diaphane.Core/native/build/bin
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "Diaphane.Core", "native", "build", "bin");
            if (File.Exists(Path.Combine(candidate, "DiaphaneCore.dll")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        // fall back to next-to-the-exe
        return AppContext.BaseDirectory;
    }
}
