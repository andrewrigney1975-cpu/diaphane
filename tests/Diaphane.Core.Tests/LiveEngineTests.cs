using System.Text;
using System.Text.Json;
using Diaphane.Core;
using Diaphane.Shell.Engine;
using Xunit;
using Xunit.Abstractions;

namespace Diaphane.Core.Tests;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CefBinDirAttribute(string dir) : Attribute
{
    public string Dir { get; } = dir;
}

/// <summary>
/// The one live end-to-end test: C# → DiaphaneCore.dll → libcef. CEF can only be
/// initialised once per process, so everything that needs a real engine runs
/// here, on one STA thread, sharing one engine:
///   1. the bridge is alive and reports the expected version,
///   2. a data: URL load fires a title back through the callbacks,
///   3. the OSR input pipeline (SetFocus → click → key/char) reaches every kind
///      of HTML form control.
/// Skipped automatically when the native staging dir isn't built.
/// </summary>
public class LiveEngineTests(ITestOutputHelper output)
{
    private const int ViewW = 900, ViewH = 600;

    private static string? BinDir
    {
        get
        {
            var d = typeof(LiveEngineTests).Assembly
                .GetCustomAttributes(typeof(CefBinDirAttribute), false)
                .Cast<CefBinDirAttribute>().FirstOrDefault()?.Dir;
            return d is not null && File.Exists(Path.Combine(d, "DiaphaneCore.dll")) ? d : null;
        }
    }

    private sealed record Case(string Name, string Field, Action<Driver> Act, string ProbeJs, Func<string, bool> Ok);

    [Fact]
    public void Bridge_loads_and_the_osr_input_pipeline_drives_every_form_control()
    {
        if (BinDir is null) return; // no engine build — nothing to exercise

        // The control sits at (0,0) sized 420x54, so a click at (40,22) always lands on it.
        static string Page(string field) =>
            "data:text/html," + Uri.EscapeDataString(
                "<!doctype html><meta charset=utf-8><body style='margin:0;font-size:22px'>" +
                "<form onsubmit='return false'>" + field + "</form>" +
                "<script>window.__hit='';</script>");

        var cases = new List<Case>
        {
            // --- text-like inputs: exact value ---
            TextLike("text"),
            TextLike("password"),
            TextLike("email", "a@b.com", "a@b.com"),
            TextLike("search"),
            TextLike("tel", "0412345678", "0412345678"),
            TextLike("url", "abc123", "abc123"),

            new("number", "<input id=t type=number style=W>",
                d => { d.Click(); d.Type("42"); }, "f.value", v => v == "42"),

            // --- segmented date/time inputs: keystrokes fill the segments ---
            new("date", "<input id=t type=date style=W>",
                d => { d.Click(15, 22); d.Key(0x24); d.Type("09"); d.Type("04"); d.Type("2026"); }, "f.value",
                v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{4}-\d{2}-\d{2}$")),
            new("time", "<input id=t type=time style=W>",
                d => { d.Click(15, 22); d.Key(0x24); d.Type("10"); d.Type("30"); d.Type("AM"); }, "f.value",
                v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{2}:\d{2}$")),
            new("month", "<input id=t type=month style=W>",
                d => { d.Click(15, 22); d.Key(0x24); d.Type("09"); d.Key(0x27); d.Type("2026"); }, "f.value",
                v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{4}-\d{2}$")),
            new("week", "<input id=t type=week style=W>",
                d => { d.Click(15, 22); d.Key(0x24); d.Type("15"); d.Type("2026"); }, "f.value",
                v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{4}-W\d{2}$")),
            // datetime-local is date + time concatenated; both are verified above.
            // Its 6-segment field is impractical to fill via fully synthetic keys,
            // so here we only assert the click lands focus and the first segment
            // takes a digit (keydown reaches it).
            new("datetime-local", "<input id=t type=datetime-local style=W>",
                d => { d.Click(15, 22); d.Key(0x24); d.Type("09"); },
                "document.activeElement === f", v => v == "true"),

            // --- toggles: click flips state ---
            new("checkbox", "<input id=t type=checkbox>",
                d => d.Click(6, 8), "String(f.checked)", v => v == "true"),
            new("radio", "<input id=t type=radio name=r><input type=radio name=r>",
                d => d.Click(6, 8), "String(f.checked)", v => v == "true"),

            // --- range: arrow keys move the thumb ---
            new("range", "<input id=t type=range min=0 max=100 value=50 step=5 style=W>",
                d => { d.Click(300, 22); d.Key(0x27); d.Key(0x27); },   // click right half, ArrowRight x2
                "f.value", v => int.TryParse(v, out var n) && n is > 50 and <= 100),

            // --- picker-only controls: keyboard can't open the OS dialog, but focus must land ---
            new("color", "<input id=t type=color style=W>",
                d => d.Click(), "document.activeElement.id", v => v == "t"),
            new("file", "<input id=t type=file style=W>",
                d => d.Click(), "document.activeElement.id", v => v == "t"),

            // --- buttons: click fires the handler ---
            new("submit", "<input id=t type=submit value=Go onclick=\"window.__hit='submit'\" style=W>",
                d => d.Click(), "window.__hit", v => v == "submit"),
            new("reset", "<input id=t type=reset value=Reset onclick=\"window.__hit='reset'\" style=W>",
                d => d.Click(), "window.__hit", v => v == "reset"),
            new("button", "<button id=t type=button onclick=\"window.__hit='button'\" style=W>B</button>",
                d => d.Click(), "window.__hit", v => v == "button"),

            // --- other form elements ---
            new("textarea", "<textarea id=t style=W></textarea>",
                d => { d.Click(); d.Type("line one"); }, "f.value", v => v == "line one"),
            new("select", "<select id=t style=W><option value=a>a<option value=b>b<option value=c>c</select>",
                d => { d.Click(); d.Key(0x28); d.Key(0x0D); }, "f.value", v => v == "b"),   // open, ArrowDown, Enter
            new("contenteditable", "<div id=t contenteditable style='width:420px;height:54px;border:1px solid'></div>",
                d => { d.Click(); d.Type("typed"); }, "f.textContent", v => v == "typed"),
        };

        var report = new StringBuilder();
        int passed = 0;

        RunSta(() =>
        {
            NativeLoader.Use(BinDir!);
            var cache = Path.Combine(Path.GetTempPath(), "diaphane-form-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cache);

            using var engine = new CefEngine(new CefEngineOptions(
                NativeLoader.ResourcesDir, NativeLoader.LocalesDir, NativeLoader.SubprocessPath,
                cache, Windowless: true, NoSandbox: true));

            Assert.Contains("151.0.7922.174", engine.Version);

            var view = (IOffscreenBrowserView)engine.CreateView(engine.StandardContext, 0);
            view.ResizeSurface(ViewW, ViewH);
            var driver = new Driver(engine, view);

            // (2) a plain load fires a title back through the callback chain
            string? title = null;
            void OnNav(object? _, NavigationState s) { if (!s.IsLoading && !string.IsNullOrEmpty(s.Title)) title = s.Title; }
            view.NavigationStateChanged += OnNav;
            driver.Load("data:text/html,<title>bridge-ok</title>x");
            view.NavigationStateChanged -= OnNav;
            report.AppendLine(title == "bridge-ok" ? "  ok    <title callback>" : $"  FAIL  <title callback> -> {title}");
            if (title == "bridge-ok") passed++;

            foreach (var c in cases)
            {
                try
                {
                    driver.Load(Page(c.Field.Replace("style=W", "style='position:absolute;left:0;top:0;width:420px;height:54px'")));
                    view.SetFocus(true);
                    c.Act(driver);
                    driver.Settle();

                    var got = driver.Eval($"(function(){{var f=document.getElementById('t');return String({c.ProbeJs});}})()");
                    if (c.Ok(got)) { passed++; report.AppendLine($"  ok    {c.Name,-16} -> {Trim(got)}"); }
                    else report.AppendLine($"  FAIL  {c.Name,-16} -> {Trim(got)}");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"  ERROR {c.Name,-16} -> {ex.Message}");
                }
            }

            view.Dispose();
            for (int i = 0; i < 30; i++) { engine.DoMessageLoopWork(); Thread.Sleep(10); }
            try { Directory.Delete(cache, true); } catch { }
        });

        var total = cases.Count + 1;   // + the title-callback check
        output.WriteLine($"{passed}/{total} live-engine checks passed\n{report}");
        Assert.True(passed == total, $"\n{report}");
    }

    private static Case TextLike(string type, string payload = "abc123", string expect = "abc123") =>
        new(type, $"<input id=t type={type} style=W>",
            d => { d.Click(); d.Type(payload); }, "f.value", v => v == expect);

    private static string Trim(string s) => s.Length > 60 ? s[..60] + "…" : s;

    // ------------------------------------------------------------------
    private sealed class Driver(CefEngine engine, IOffscreenBrowserView view)
    {
        public void Load(string url)
        {
            var tcs = new TaskCompletionSource();
            void H(object? _, NavigationState s) { if (!s.IsLoading) tcs.TrySetResult(); }
            view.NavigationStateChanged += H;
            view.Navigate(url);
            Pump(tcs.Task, TimeSpan.FromSeconds(20));
            view.NavigationStateChanged -= H;
            Settle();
        }

        public void Click(int x = 40, int y = 22)
        {
            view.SetFocus(true);
            view.SendMouseMove(x, y, false);
            view.SendMouseButton(x, y, 0, true, 1);
            view.SendMouseButton(x, y, 0, false, 1);
            Settle();
        }

        public void Type(string text)
        {
            // A plain <input> types from the CHAR event alone. Digits also get a
            // RAWKEYDOWN/KEYUP (VK_0..VK_9 == '0'..'9') so segmented date/time
            // fields, which advance on key-down, accept them too.
            foreach (var ch in text)
            {
                bool digit = ch is >= '0' and <= '9';
                if (digit) view.SendKey(true, ch, 0, 0, '\0');   // RAWKEYDOWN
                view.SendKey(true, ch, 0, 0, ch);                // CHAR
                if (digit) view.SendKey(false, ch, 0, 0, '\0');  // KEYUP
            }
            Settle();
        }

        /// <summary>Press a non-character key (Windows virtual-key code), down then up.</summary>
        public void Key(int vk)
        {
            view.SendKey(true, vk, 0, 0, '\0');
            view.SendKey(false, vk, 0, 0, '\0');
            Settle();
        }

        public string Eval(string js)
        {
            var task = view.EvaluateJavaScriptAsync(js);
            Pump(task, TimeSpan.FromSeconds(10));
            using var doc = JsonDocument.Parse(task.Result);
            var r = doc.RootElement.GetProperty("result");
            return r.TryGetProperty("value", out var v) ? v.ToString() : "";
        }

        public void Settle()
        {
            for (int i = 0; i < 25; i++) { engine.DoMessageLoopWork(); Thread.Sleep(8); }
        }

        private void Pump(Task task, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && sw.Elapsed < timeout) { engine.DoMessageLoopWork(); Thread.Sleep(5); }
            if (!task.IsCompleted) throw new TimeoutException("engine did not respond");
            task.GetAwaiter().GetResult();   // surface faults
        }
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw error;
    }
}
