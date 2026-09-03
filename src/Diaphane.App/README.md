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

## Rendering: OSR path wired; WinUI 3 compositor not verified here

The shell renders CEF **off-screen** (windowless): `CefHost` inits the engine with
`Windowless: true`, `CefBrowserView` implements `IOffscreenBrowserView`, and
`MainWindow` blits each `OnPaint` BGRA frame into a `WriteableBitmap` behind an
`Image`, forwarding pointer / wheel / key / char input to
`CefBrowserHost::SendMouse*Event` / `SendKeyEvent`.

Windowed hosting (`SetAsChild`) was tried first and abandoned: Chromium's windowed
GPU compositor hits `FATAL:ui\gl\child_window_win.cc:117 NOTREACHED` when the browser
HWND is a child of the WinUI content island (an interposing plain-Win32 child window
didn't help). Process-wide GPU switches fix that crash but break WinUI 3's own
compositor — they share the process. OSR sidesteps all of it.

**Not visually verified in this build environment:** a bare
`<Grid Background="Crimson"><TextBlock/></Grid>` WinUI 3 window also renders nothing
here — the window is created and visible but never composites (same signature as
CEF's GPU process: `Failed to create shared context for virtualization`). This
machine's D3D/GPU state doesn't support accelerated composition in this context;
the user's own Chrome/Edge render fine, so it is environment- or driver-specific.
Run `Diaphane.App` on a box where WinUI 3 composites to see it.

Debug: unhandled exceptions and paint failures are appended to
`%TEMP%\diaphane-app.log`.
