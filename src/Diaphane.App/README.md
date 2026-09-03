# Diaphane.App — WinUI 3 shell (M3)

Unpackaged WinUI 3 (`net8.0-windows10.0.19041.0`, WindowsAppSDK 1.7). **Build with
Visual Studio MSBuild**, not `dotnet build`:

```
& "F:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    src\Diaphane.App\Diaphane.App.csproj /r /p:Configuration=Debug /p:Platform=x64
```
Requires `src/Diaphane.Core/native/build/bin` (run `native/build.ps1` first) —
`CefHost.ResolveNativeBinDir()` walks up to find it, or set `DIAPHANE_CEF_BIN`.

## What works
- Window, custom chrome, `TabView` strip, toolbar (back/fwd/reload/bookmark/sandbox),
  `AutoSuggestBox` address bar with history + bookmark suggestions.
- `ShellViewModel` (CommunityToolkit.Mvvm) wires `TabManager`, `OmniboxParser`,
  `HistoryStore` + `BookmarkStore` (SQLite in `%LOCALAPPDATA%\Diaphane`), `CefEngine`.
- `CefHost`: engine bootstrap + external-message-pump on the WinUI dispatcher.
- **Profile isolation**: CEF's user-data dir is pinned to `%LOCALAPPDATA%\Diaphane\UserData`
  (`cache_path` == `root_cache_path`). Without this the Chrome runtime inherits the
  machine's real Chrome/Edge profile — bookmarks, session, everything. Never do that.
- CEF initialises and spawns its subprocesses (via `diaphane_helper.exe`).

## What's NOT working — the CEF page pixels don't reach the window
Root cause: `FATAL:ui\gl\child_window_win.cc:117 NOTREACHED` in the GPU process.
Chromium's **windowed** GPU compositing creates a GL child window of the browser
HWND; when that HWND is `SetAsChild` of the WinUI content island the hierarchy
trips a NOTREACHED and the GPU process crash-loops, so nothing paints (and the
empty child window would occlude the chrome — it's created hidden for that reason).

This is the "airspace" problem from the architecture doc §04. Process-wide GPU
switches (`--disable-gpu` / `--in-process-gpu` / `--use-angle`) are not an option —
they break WinUI 3's own compositor, which shares this process.

**Fix (M3 completion):** OSR / windowless rendering. The M2 bridge already runs
CEF windowless (headless test passes); expose `OnPaint`'s BGRA buffer through the
C ABI, blit it to a `WriteableBitmap` in a `SwapChainPanel`/`Image`, and forward
mouse/keyboard/IME/DPI from that element to `CefBrowserHost::SendMouse*Event` etc.
Alternatively host via `DesktopChildSiteBridge` with a top-level (not child) CEF
window positioned over the region.
