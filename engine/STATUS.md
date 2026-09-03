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
