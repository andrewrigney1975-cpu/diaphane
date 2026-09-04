# M1 engine build — status: ✅ COMPLETE (reduced patch set)

**Built on this machine** (24 cores, 128 GB RAM, F: drive). Workspace `F:\cef-build\`.
Final successful build: 2026-09-04. Total wall time across iterations: ~2 days (mostly
compile + a long tail of ungoogled↔CEF↔SDK-26100 patch conflicts).

## Result
| | |
|---|---|
| `libcef.dll` | 441 MB — `151.3.24+g2384915+chromium-151.0.7922.174` |
| `cefsimple.exe` | runs; loaded wikipedia.org across 7 processes, stable |
| Codecs | `proprietary_codecs=true ffmpeg_branding="Chrome"` → H.264/AAC + VP8/9, AV1, Opus, Vorbis, FLAC, MP3 (verified in `third_party/ffmpeg/.../Chrome/win/x64/config.h`) |
| Config | Release, **non-official** (no LTO/PGO — M1 speed; official is a later pass) |
| SDK | `F:\cef-build\dist\cef\` — `include/`, `Release/` (libcef.dll + .lib + libcef_dll_wrapper.lib + ANGLE + SwiftShader + snapshots), `Resources/` (paks, icudtl, 84 locales). Assembled by `engine/scripts/assemble-sdk.ps1`. |

## Target
| | |
|---|---|
| CEF branch | `7922` (stable) → Chromium `151.0.7922.174` |
| ungoogled tag | `151.0.7922.173-1` (closest; no `.174` exists) |
| Toolchain | VS 2026 Community v18.8 + Windows SDK 10.0.26100, CEF `WIN_CUSTOM_TOOLCHAIN=1` |

## Build pipeline (`engine/scripts/`, run from `F:\cef-build\`)
1. **`build-all.ps1`** — one pass: reset pristine → (prune skipped) → CEF `gclient_hook`
   (CEF patches, once) → ungoogled patches on top (`patch --fuzz=3`, denylist) → domain
   substitution → **`apply-diaphane-fixes.ps1`** → merge `flags.gn` → `gn gen` → `autoninja`.
2. **`gn-build.ps1`** — just `gn gen` + `autoninja` (resume after a manual tree fix).
3. **`assemble-sdk.ps1`** — hand-assemble the binary SDK (make_distrib needs the full
   `cef` target; we build the `cefsimple libcef libcef_dll_wrapper` subset).

## Ungoogled patch set: ~53 of 109 applied
CORE network/telemetry hardening applied. **Skipped** (see `engine/scripts/ungoogled-skipped.txt`):
- the `add-flag-*` / `add-flags-*` browser-flag family + `add-components-ungoogled` +
  `add-ungoogled-flag-headers` — need the ungoogled *browser* flag system; half-apply
  under CEF-first and leave code that won't compile
- `remove-unused-preferences-fields` (300-file patch, fuzzes inconsistently → dangling
  `prefs::k*`) + its companion `move-js-optimizer-unfamiliar-sites`
- `fix-building-without-safebrowsing` (123 hunks, doesn't rebase clean onto Cr151) →
  **`safe_browsing_mode` left at Chromium default (1)**; SB is disabled at runtime by the
  default-prefs patch and de-phoned by the iridium reporting patches. Compile-time SB
  removal is the top backlog item.
- `disable-rlz` (CEF hard-requires `enable_rlz=true` for CDM storage id)
- ~24 more that don't apply under `--fuzz=3` against the CEF-patched tree

Privacy still substantially intact: GN `flags.gn` (no Google API keys, `enable_reporting=false`,
`safe_browsing` runtime-off, no field-trial config, no hangout/mdns/remoting), domain
substitution (rewrites every google/gstatic/etc. URL in the source), + the ~53 core patches.

## diaphane build fixes (`apply-diaphane-fixes.ps1`, idempotent, post-domsub)
1. `message_compiler.py` ×2 — Win SDK 26100 `mc.exe` emits extra `*TEMP.BIN`/`*_MSG*.bin`
2. `bake_in_configs.py` — domain_reliability whitelist vs domsub'd configs
3. `grit/tool/build.py` — `--assert-file-list` set-equal/order-artifact tolerance
4. `toolchain.gni` — `alink` `/llvmlibempty` (empty `safe_browsing.lib` under `/WX`)
5. `cef_strings.grd` — +27 Cr151 `platform_pak_locales` missing from CEF 7922's grd

## Key lessons (chronological)
1. `gclient sync` must pin `--revision src@<tag>` — bare sync rolled src to Chromium main (155).
2. Toolchain (clang/rust/ninja/siso) is a **DEPS/gcs** dependency, not a hook — needs `gclient sync`.
3. `git reset --hard` on `src` doesn't touch sub-repos (`src/third_party/depot_tools`, `src/tools/clang`).
4. `gclient sync` on Windows needs `-j1` — parallel gsutil-bootstrap lock race (`LockFileEx` err 6).
5. ungoogled `prune_binaries.py` deletes prebuilt toolchains + files DEPS needs → **skip prune** for local builds.
6. **Never** have the `src/cef` junction present during `gclient sync --delete_unversioned_trees` (it deletes through the junction). Use a real directory; siso can't traverse junctions anyway.
7. ungoogled CEF-first ordering: CEF patches on pristine (0 fail), ungoogled on top with fuzz + denylist.
8. `remove-unused-preferences-fields` + `fix-building-without-safebrowsing` + the flag family are the fragile ones.

## Backlog (post-M1)
- Rebase `fix-building-without-safebrowsing` → get `safe_browsing_mode=0` (compile-time SB removal)
- Port the ungoogled flag infrastructure so the valuable `extra/` patches work under CEF
  (canvas-fp-noise, webgl-renderer-spoof, client-hints removal, clear-data-on-exit, reduce-system-info)
- Official build (`is_official_build=true` + PGO) for release
- Widevine: opt-in CDM download flow (compiled in, not fetched)
- CI: nightly engine build + the no-phone-home MITM gate (M7)

---

# M2 — DiaphaneCore bridge: ✅ COMPLETE (2026-09-04)

`DiaphaneCore.dll` + `diaphane_helper.exe` built against the M1 SDK. Flat C ABI
(`src/Diaphane.Core/native/include/diaphane_core.h`) → P/Invoke → `CefEngine : IBrowserEngine`.

**Gate met:** `dotnet test tests/Diaphane.Core.Tests` drives a headless `data:` + https
load end-to-end (C# → DiaphaneCore → libcef_dll_wrapper → libcef → Chromium 151), title
callback flows back to C#. Also `tools/HeadlessLoad` as a console app.

Key points:
- `libcef_dll_wrapper` is **rebuilt from SDK sources with MSVC** (`/MT`, C++20) so its
  CRT/STL matches the bridge — the Chromium-built wrapper uses bundled libc++.
- Exact wrapper source list from `cef_paths*.gypi` (`cef_wrapper_sources.cmake`) — a glob
  pulls in DLL-side-only + bootstrap/sandbox trees that need full `//base`.
- CEF wants an **STA thread**; the test uses a dedicated one, the WinUI shell's
  dispatcher will satisfy it.
- `CefExecuteProcess` must run before `CefInitialize` even in the browser process.
- Needed one extra header vs the raw include tree: `net/base/net_error_list.h` →
  `include/base/internal/cef_net_error_list.h` (CEF's `transfer.cfg`).
- GCM "cannot start" errors in the log are **expected** — ungoogled disabled it.

Bridge M2 stubs (later milestones): storage-clear (needs BrowsingDataRemover),
extension loading, per-context proxy, windowed (non-OSR) HWND hosting.

---

# M3 — WinUI 3 shell: ⚠️ SCAFFOLD COMPLETE, CEF hosting blocked

`src/Diaphane.App/` — unpackaged WinUI 3 (net8.0-windows10.0.19041, WindowsAppSDK 1.7).
**Build with VS MSBuild** (`F:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\...`).

**Works:** window + chrome, `TabView` strip, toolbar, `AutoSuggestBox` address bar with
history/bookmark suggestions, `ShellViewModel` (CommunityToolkit.Mvvm) wiring `TabManager` +
`OmniboxParser` + `HistoryStore`/`BookmarkStore` (SQLite) + `CefEngine`. CEF inits, spawns
6 subprocesses, per-tab request contexts.

**Critical fix found:** CEF's Chrome runtime with an empty `cache_path` **inherits the
machine's real Chrome/Edge profile** — bookmarks, open session, everything. Fixed by pinning
`cache_path == root_cache_path == %LOCALAPPDATA%\Diaphane\UserData`. (`diaphane_core.cc`.)

**Blocked:** CEF page pixels never reach the window. `FATAL:ui\gl\child_window_win.cc:117
NOTREACHED` in the GPU process — Chromium's **windowed** GPU compositor can't create its GL
child window when the CEF browser HWND is `SetAsChild` under the WinUI content island (tried
an interposing plain-Win32 child window too; same result). Process-wide GPU switches
(`--disable-gpu`, `--in-process-gpu`, `--use-angle`) fix the crash but break WinUI 3's own
compositor (shared process). This is the architecture-doc §04 airspace problem.

**M3 completion = OSR path:** the M2 bridge already runs CEF windowless (headless test
passes). Expose `OnPaint` BGRA through the C ABI → `WriteableBitmap` in a `SwapChainPanel`/
`Image` → forward mouse/keyboard/IME/DPI to `CefBrowserHost::SendMouse*Event`. See
`src/Diaphane.App/README.md`.

## M3 update — OSR rendering path

Windowed `SetAsChild` hosting hits `child_window_win.cc:117 NOTREACHED` (GPU
compositor under the WinUI island). Switched the shell to **OSR**: bridge exposes
`OnPaint` BGRA + windowless input (`dc_view_mouse_*` / `dc_view_key` /
`dc_view_osr_size`), `IOffscreenBrowserView` in Diaphane.Shell, `MainWindow` blits
frames to a `WriteableBitmap`. Builds Debug+Release; Core+Shell tests green (14).

**Rendering verified as far as the environment allows:** `PrintWindow(PW_RENDERFULLCONTENT)`
shows the WinUI window frame + title bar render correctly (`engine/m3-window.png`); the
content region is solid black — the content island's D3D swapchain can't initialise
here (same root cause as CEF's GPU `Failed to create shared context for virtualization`).
This box has no working accelerated composition in this context (the user's own Chrome/
Edge render fine, so it's driver/session-specific). App + bridge code is complete and
builds; run `Diaphane.App` where WinUI 3 composites to see pages render.

---

# M3 — CORRECTION: ✅ IT WORKS

The "content black / no accelerated composition" conclusion above was **wrong** — an
artefact of screen-capture tooling, not the app.

`Graphics.CopyFromScreen` (GDI BitBlt) and `PrintWindow` **cannot capture WinUI 3
DirectComposition content**: BitBlt returned the desktop wallpaper, PrintWindow returned
the frame with a black content area. `RenderTargetBitmap.RenderAsync` (reads from the
compositor) shows the truth: **~96% non-black pixels, DuckDuckGo fully rendered in the
shell** (`engine/m3-running.png`). The Intel Arc A770 is fine; D3D11 hardware device
creation succeeds in-process.

So M3 is done: WinUI 3 shell + tabs + omnibox + history/bookmarks + **CEF pages
rendering via OSR** into a `WriteableBitmap`, mouse/keyboard forwarded. Profile isolation
fix (CEF was inheriting the real Chrome/Edge profile) is in.

Diagnostic: set `DIAPHANE_SELFSHOT=1` → the app dumps `RenderTargetBitmap` frames to
`%TEMP%\diaphane-shot*.png`.

## M3 polish backlog
- tab header, favicon in tab, CEF sandbox (currently no_sandbox), IME, per-monitor DPI on the OSR surface
- `--disable-gpu-compositing` scoped test (OSR still spins a GPU process that logs child_window NOTREACHED but recovers)

---

# M4 / M5 / M6 — ✅ tabs, bookmarks+history+omnibox, sandbox tabs

**M4 — Tabs.** Multi-tab strip built by hand (`TabView.TabItems` + code-behind sync in
`MainWindow.xaml.cs`) rather than `TabItemsSource`+`TabItemTemplate`: TabView does **not**
refresh a templated header when the bound model changes, so the title froze at "New Tab".
Hand-built `TabViewItem`s subscribe to `TabModel.PropertyChanged` and update the header
`TextBlock` directly — verified: tab now reads "DuckDuckGo - Protection. Privacy…".
Switching the active tab calls `IOffscreenBrowserView.Invalidate()` after `ResizeSurface()`
so the newly-shown surface repaints immediately. New C ABI: `dc_view_invalidate` →
`CefBrowserHost::Invalidate(PET_VIEW)`, plus invalidate-on-show in `dc_view_set_visible`.
Keyboard accelerators on `Root`: Ctrl+T/W, Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+L, F5,
Alt+Left/Right, Ctrl+D, Ctrl+Shift+B, Ctrl+Shift+N.

**M5 — Bookmarks + history + omnibox.** `HistoryStore.Recent(limit)` for the history
flyout (ListView, "Clear all"). Bookmarks bar (`ItemsControl`, toggle with Ctrl+Shift+B,
per-item Remove flyout). `ShellViewModel.MaybeRecordVisit` records one visit per settled
URL, **skipping sandbox tabs and about:/data:/chrome://diaphane:// schemes**, deduped via
`_lastRecorded`. Omnibox suggestions merge bookmark+history (frecency), dedup by URL, top 8.

**M6 — Sandbox tabs.** One ephemeral in-memory `CefRequestContext` per window shared by
all sandbox tabs (`_sandboxGroup` guid); destroyed — RAM released, nothing persisted —
when the last sandbox tab closes (`TabManager`). Sandbox tabs never touch `HistoryStore`.
Visual: violet shield glyph in the tab, violet toolbar tint when a sandbox tab is active
(`MainWindow.ToolbarBrush`). Covered by `SandboxTabTests` (own context / shared group /
context disposed on last close).

Tests: 13/13 `Diaphane.Shell.Tests` green. App builds with VS 18 MSBuild, runs, renders
DuckDuckGo with the full M4/M5/M6 chrome (`%TEMP%\diaphane-shot*.png`).

---

# Bugfix — navigation after the first hangs forever

Symptom: type a URL, hit Enter, the load bar animates but the page never
changes. Only the *first* page load (the one at startup) worked.

Root cause: in `CefHost` the steady 33 ms pump `heartbeat` timer was a **local
variable**, not a field. Nothing rooted it, so the GC collected it — and a
collected `DispatcherQueueTimer` silently stops firing. The first navigation
allocates enough (SQLite, marshalling, event args) to trigger a GC; the
heartbeat dies; `CefDoMessageLoopWork()` stops being called; every subsequent
navigation commits (`OnLoadStart`) but then stalls because CEF's UI-thread work
is never pumped. Fix: hold it in a field (`_heartbeatTimer`).

Also added while chasing it (both worth keeping):
- `dc_pump` now guards against reentrant `CefDoMessageLoopWork()` (CEF forbids it).
- `DcClient` implements `CefJSDialogHandler` — OSR has no dialog surface, so
  `OnBeforeUnloadDialog` auto-continues (a page with `onbeforeunload` would
  otherwise wedge navigation) and `OnJSDialog` auto-dismisses.

Note: a hard `Process.Kill()` (as the dev screenshot loop was doing) never lets
CEF close its block-file HTTP cache cleanly and corrupts it
(`backend_impl.cc:1869 Destroying invalid entry` flood). Delete
`%LOCALAPPDATA%\Diaphane\UserData\Default\Cache` if that happens; normal
window-close shuts down cleanly via `Engine.Dispose()`.

---

# M7 — Privacy dashboard + clear-data  ✅

- `Diaphane.Privacy`: `ClearTimeRange` (hour/day/week/4wk/all → cut-off instant),
  `PrivacySettings` + `PrivacySettingsStore` (one JSON file, never throws),
  `PrivacyService` (range→cutoff, drives the existing `DataClearer`, persists
  `ClearOnExit` and the default scope/range).
- `PrivacyViewModel` + `diaphane://privacy` overlay panel: five scope checkboxes
  (cookies / site storage / http cache / history / DNS+connections), a time-range
  combo, "Clear now", and "clear … every time diaphane closes".
- Toolbar shield button, `Ctrl+Shift+Del`; `diaphane://privacy` and
  `diaphane://settings` in the omnibox open the panel.
- Clear-on-exit runs from the window `Closed` handler before engine shutdown.
- 9 `PrivacyService` tests.

# M8 — Extensions  ✅ (management + startup load)

- `Diaphane.Shell.Extensions.ExtensionManifest`: parses `manifest.json`
  (comments + trailing commas tolerated), pulls name/version/permissions/
  update_url, and derives Chromium's unpacked-extension id (SHA-256 of the
  UTF-16LE path, first 16 bytes → a–p alphabet).
- `Diaphane.Data.ExtensionStore` (SQLite): id/path/name/version/enabled/perms.
  `EnabledPaths()` feeds the engine.
- ABI: `dc_settings.extension_dirs` (';'-separated) → `DcApp` appends
  `--load-extension=<csv>` in `OnBeforeCommandLineProcessing`. **No `update_url`
  is ever contacted** — matches the "no silent auto-update" golden rule.
- `CefRequestContext.LoadExtensionAsync` parses the manifest and records it;
  the extension actually loads on the next launch (like Chrome's
  `--load-extension`), surfaced in the UI as "restart to apply".
- `ExtensionsViewModel` + `diaphane://extensions` panel: list with per-row
  enable/disable + remove, "Load unpacked…" (folder picker), and a manual
  "Check for updates" that only re-reads the folder on disk.
- Toolbar puzzle button, `Ctrl+Shift+E`.
- 8 manifest/store tests. 30 shell tests green total.

Not yet done for M8: content-script / background-page runtime validation against
a real page (needs the engine + a test extension), CRX (packed) install,
per-Sandbox-context extension isolation.

---

# M9 — DevTools + codecs  ✅

**DevTools.** New ABI `dc_view_show_devtools` / `dc_view_close_devtools` /
`dc_view_has_devtools` → `CefBrowserHost::ShowDevTools` into a normal top-level
popup window (works fine from the OSR main browser — it's a separate HWND tree,
not parented into the WinUI island). F12 and Ctrl+Shift+I, plus a toolbar button.
Verified: `HasDevTools` true after toggle, no GPU child-window crash.

**Script evaluation.** `dc_view_eval_js` runs `Runtime.evaluate` via
`CefBrowserHost::ExecuteDevToolsMethod` and routes the result JSON back through a
`CefDevToolsMessageObserver` (own class — can't multiply-inherit two
`CefBaseRefCounted`). Surfaced as `IBrowserView.EvaluateJavaScriptAsync` →
`TaskCompletionSource` keyed by CDP message id. No render-process code needed.

**Media & codecs** (`diaphane://media`, Ctrl+Shift+M). `Diaphane.Shell.Media.MediaProbe`
— a self-contained expression running `canPlayType` / `MediaSource.isTypeSupported`
/ `requestMediaKeySystemAccess` in the page; `MediaViewModel` renders the table.
Live result on this build: **H.264, AAC, MP3, VP9, AV1, Opus, FLAC, Vorbis all
"Probably"** (confirms the M1 Chrome-branded ffmpeg), H.265 absent, Clear Key EME
available, Widevine/PlayReady not available.

**Widevine opt-in.** `PrivacySettings.EnableWidevine` (default **off**) →
`dc_settings.allow_widevine`; when off, `DcApp` appends `WidevineCdm` to
`--disable-features`. Checkbox in the privacy panel. The module is **never
downloaded** — the toggle only permits an already-present CDM. Applied at launch.

Tests: `MediaProbe` parse + `TabModel` devtools/eval — 35 shell tests green.

---

# M9 — DevTools: docked right-hand pane (revised)

The first cut opened `CefBrowserHost::ShowDevTools` in a popup / windowless view;
with the **Chrome runtime** that renders blank (OSR DevTools is an Alloy-runtime
feature). Reworked to a docked pane:

- `dc_settings.devtools` → `DcApp` adds `--remote-debugging-port=0`
  `--remote-debugging-address=127.0.0.1` `--remote-allow-origins=*`. Ephemeral
  port, **loopback only**, written to `<cache>/DevToolsActivePort`.
- `dc_devtools_port()` reads that file. `CefEngine.ResolveDevToolsFrontendUrl`
  does one `GET http://127.0.0.1:<port>/json/list` (on a background thread — the
  handler needs the UI/pump thread free), matches the page target by URL, and
  returns its `devtoolsFrontendUrl`.
- `CefBrowserView.OpenDevToolsAsync` creates a second windowless view
  (`CefEngine.CreateRawView`) and navigates it to that front-end URL — so the
  DevTools frontend renders as an ordinary OSR page.
- `MainWindow`: a 3-column browser row (page | drag splitter | DevTools pane).
  `CefSurface` was extracted so the page and the DevTools pane share the same
  BGRA-blit + input-forwarding code. F12 / Ctrl+Shift+I toggle it.
- Verified: full Elements/Console/Styles UI, live DOM of the inspected tab,
  resizable pane.

`PrivacySettings.EnableDevTools` (default **on**) gates the whole loopback port;
turning it off in the privacy panel closes it (applied at launch), alongside the
Widevine toggle.

---

# M10 — settings, packaging, self-update  ✅

**Settings** (`diaphane://settings`, gear button). `Diaphane.Shell.Settings`:
`AppSettings` + `SettingsStore` (one JSON file, never throws). Panel wires:
- On startup — blank / homepage / **restore last session**
- Homepage (+ a Home toolbar button, `GoHomeCommand`)
- Search engine — DuckDuckGo / Startpage / Brave / Wikipedia / Mojeek / Google /
  Custom (`{q}` template). `SearchEngines.Resolve` in Omnibox; `ShellViewModel._search`
  re-resolves live. Every engine is a plain query GET — the omnibox still makes no
  call while you type.
- Appearance — Match Windows / Light / Dark → `Root.RequestedTheme`
- Bookmarks-bar visibility now persists

**Session restore.** `SessionStore` writes the standard tabs' URLs on close (only
when startup == RestoreSession; sandbox tabs never written; internal schemes
filtered). `ShellViewModel.OpenStartupTabs` replays them.

**Self-update — manual only.** `UpdateChecker` compares the running version to a
JSON manifest (`{"version","url"}`) at a URL the user sets in settings. Empty by
default = dormant. Runs *only* on the "Check for updates" click — no background
poll, nothing downloaded or installed; a newer version just offers an "Open the
download page" link. (Consistent with the no-silent-update golden rule.)

**Packaging.** `scripts/package.ps1`: publishes Diaphane.App self-contained
(win-x64, no runtime install needed), copies the engine payload from
`native/build/bin` beside the exe, zips to `dist/diaphane-<version>-win-x64.zip`
(~300 MB). Verified: the unpacked build runs standalone. `<Version>` in the
csproj (0.10.0) drives the zip name and the settings "About" line.

44 shell tests green.

---

# Live test: OSR input pipeline vs. every form control

`tests/Diaphane.Core.Tests/LiveEngineTests.cs` — the single live end-to-end test
(CEF initialises once per process, so the old HeadlessLoadTests folded in here).
On one STA thread / one engine it checks: the bridge version, a title callback,
and then drives **every kind of HTML form control** through
SetFocus → click → key/char and reads the result back via Runtime.evaluate:

  text · password · email · search · tel · url · number · date · time · month ·
  week · datetime-local · checkbox · radio · range · color · file · submit ·
  reset · button · textarea · select · contenteditable  — 24/24.

Native changes this surfaced (all kept — they matter for real use):
- `DcClient : CefDialogHandler` — `OnFileDialog` cancels (OSR has no window to
  parent an OS dialog to; the shell will drive file picking itself later).
- `GetScreenInfo` / `GetScreenPoint` implemented, and `OnPopupShow` /
  `OnPopupSize` tracked — without them `<select>` etc. hit
  `web_contents_view_osr GetNativeView() NOTIMPLEMENTED` and crashed on teardown
  (dangling raw_ptr).
- `ReleaseDevToolsObserver()` now runs *before* `CloseBrowser` in `dc_view_close`
  / `dc_shutdown` / `OnBeforeClose` — the `CefRegistration` dtor must not touch a
  half-closed browser.

Still open: `<select>` / date-picker dropdowns paint via `PET_POPUP`, which the
shell doesn't composite yet, so the dropdown isn't visible (keyboard still
works); `<input type=file>` needs wiring to a real WinUI file picker.
