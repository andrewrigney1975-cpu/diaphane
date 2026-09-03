using Diaphane.Core;
using Diaphane.Shell.Engine;
using Xunit;

namespace Diaphane.Core.Tests;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CefBinDirAttribute(string dir) : Attribute
{
    public string Dir { get; } = dir;
}

/// <summary>
/// Live end-to-end test of the C# → DiaphaneCore.dll → libcef bridge. Skipped
/// automatically when the native staging dir isn't present (CI without an engine
/// build). CEF requires an STA thread, so these run on a dedicated one.
/// </summary>
public class HeadlessLoadTests
{
    private static string? BinDir
    {
        get
        {
            var d = typeof(HeadlessLoadTests).Assembly
                .GetCustomAttributes(typeof(CefBinDirAttribute), false)
                .Cast<CefBinDirAttribute>().FirstOrDefault()?.Dir;
            return d is not null && File.Exists(Path.Combine(d, "DiaphaneCore.dll")) ? d : null;
        }
    }

    private static T RunSta<T>(Func<T> body)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw error;
        return result;
    }

    [Fact]
    public void Headless_dataurl_load_fires_title_through_the_bridge()
    {
        if (BinDir is null)
            return; // native CEF staging dir not built — nothing to exercise here


        var title = RunSta(() =>
        {
            NativeLoader.Use(BinDir!);
            var cache = Path.Combine(Path.GetTempPath(), "diaphane-test-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cache);

            using var engine = new CefEngine(new CefEngineOptions(
                NativeLoader.ResourcesDir, NativeLoader.LocalesDir, NativeLoader.SubprocessPath,
                cache, Windowless: true, NoSandbox: true));

            Assert.Contains("151.0.7922.174", engine.Version);

            var tcs = new TaskCompletionSource<NavigationState>();
            var view = engine.CreateView(engine.StandardContext, 0);
            view.NavigationStateChanged += (_, s) =>
            {
                if (!s.IsLoading && !string.IsNullOrEmpty(s.Title)) tcs.TrySetResult(s);
            };
            view.Navigate("data:text/html,<title>bridge-ok</title>x");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!tcs.Task.IsCompleted && sw.Elapsed < TimeSpan.FromSeconds(30))
            {
                engine.DoMessageLoopWork();
                Thread.Sleep(5);
            }
            view.Dispose();
            for (int i = 0; i < 30; i++) { engine.DoMessageLoopWork(); Thread.Sleep(10); }

            Assert.True(tcs.Task.IsCompletedSuccessfully, "no load-end within 30s");
            return tcs.Task.Result.Title;
        });

        Assert.Equal("bridge-ok", title);
    }
}
