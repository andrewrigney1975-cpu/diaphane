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

## Rendering: OSR — works

The shell renders CEF **off-screen**: `CefHost` inits `Windowless: true`,
`CefBrowserView : IOffscreenBrowserView`, `MainWindow` blits each `OnPaint` BGRA frame
into a `WriteableBitmap` behind an `Image` and forwards pointer / wheel / key / char to
`CefBrowserHost::SendMouse*Event` / `SendKeyEvent`.

Windowed hosting (`SetAsChild`) was tried and abandoned: Chromium's windowed GPU
compositor hits `child_window_win.cc:117 NOTREACHED` when the browser HWND is a child of
the WinUI content island (an interposing Win32 child window didn't help). OSR sidesteps it.

### Capturing the window
GDI BitBlt and `PrintWindow` **cannot see WinUI 3 DirectComposition content** — use
`RenderTargetBitmap.RenderAsync`. Set `DIAPHANE_SELFSHOT=1` and the app dumps frames to
`%TEMP%\diaphane-shot*.png`. Unhandled exceptions and paint errors go to
`%TEMP%\diaphane-app.log`.
