// M2 gate: drive a headless page load through the C# -> DiaphaneCore -> libcef bridge.
//
//   dotnet run --project tools/HeadlessLoad -- <path-to-native-bin> [url]
//
// CEF initializes COM as STA on its UI thread, so the engine must be created and
// pumped from an [STAThread]. The WinUI shell's dispatcher thread satisfies this.
using System.Diagnostics;
using Diaphane.Core;
using Diaphane.Shell.Engine;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string binDir = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                           "src", "Diaphane.Core", "native", "build", "bin");
        binDir = Path.GetFullPath(binDir);

        string url = args.Length > 1
            ? args[1]
            : "data:text/html,<title>Diaphane%20M2%20OK</title><h1>bridge works</h1>";

        Console.WriteLine($"native bin : {binDir}");
        if (!File.Exists(Path.Combine(binDir, "DiaphaneCore.dll")))
        {
            Console.Error.WriteLine("DiaphaneCore.dll not found — run src/Diaphane.Core/native/build.ps1 first.");
            return 2;
        }

        NativeLoader.Use(binDir);

        var cache = Path.Combine(Path.GetTempPath(), "diaphane-m2-cache");
        Directory.CreateDirectory(cache);

        using var engine = new CefEngine(new CefEngineOptions(
            ResourcesDir: NativeLoader.ResourcesDir,
            LocalesDir: NativeLoader.LocalesDir,
            SubprocessPath: NativeLoader.SubprocessPath,
            RootCacheDir: cache,
            Windowless: true,
            NoSandbox: true));

        Console.WriteLine($"engine     : {engine.Version}");

        var tcs = new TaskCompletionSource<NavigationState>();
        var view = engine.CreateView(engine.StandardContext, hostHwnd: 0);
        view.NavigationStateChanged += (_, s) =>
        {
            Console.WriteLine($"  nav: loading={s.IsLoading} title='{s.Title}' url={Trim(s.Url)}");
            if (!s.IsLoading && !string.IsNullOrEmpty(s.Title))
                tcs.TrySetResult(s);
        };

        Console.WriteLine($"navigate   : {Trim(url)}");
        view.Navigate(url);

        var sw = Stopwatch.StartNew();
        while (!tcs.Task.IsCompleted && sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            engine.DoMessageLoopWork();
            Thread.Sleep(5);
        }

        int exit;
        if (tcs.Task.IsCompletedSuccessfully)
        {
            var s = tcs.Task.Result;
            Console.WriteLine($"\nLOADED  title='{s.Title}'  url={Trim(s.Url)}  ({sw.ElapsedMilliseconds} ms)");
            exit = 0;
        }
        else
        {
            Console.Error.WriteLine("\nTIMEOUT — no load-end within 30 s");
            exit = 1;
        }

        view.Dispose();
        for (int i = 0; i < 40; i++) { engine.DoMessageLoopWork(); Thread.Sleep(10); }
        return exit;
    }

    private static string Trim(string s) => s.Length <= 80 ? s : s[..77] + "...";
}
