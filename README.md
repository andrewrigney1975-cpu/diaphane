# diaphane

A privacy-first, Windows-native web browser: a **WinUI 3** shell over an
**ungoogled-chromium** engine (via `libcef`). No telemetry, no phone-home. History
and cookies clear in one click. A **Sandbox tab** is Incognito taken seriously —
its own in-memory storage partition, gone without trace when the last tab closes.

Full design: **[Architecture & Roadmap](https://claude.ai/code/artifact/7760756e-a209-4adb-8d85-26f5ebe62354)**

## Layout
| Project | What | Builds without the engine? |
|---|---|---|
| `src/Diaphane.Shell` | Tab model, omnibox parser, engine contract (`IBrowserEngine`) | ✅ |
| `src/Diaphane.Data` | SQLite bookmark + history + extension stores, local frecency suggestions | ✅ |
| `src/Diaphane.Privacy` | `DataClearer`, `PrivacyService`, clear-on-exit, panic wipe | ✅ |
| `src/Diaphane.Core` | C++/WinRT bridge to libcef — the only code that touches CEF | ❌ (needs `engine/`) |
| `src/Diaphane.App` | WinUI 3 window, tab strip, address bar, `diaphane://` pages | ❌ (M3) |
| `engine/` | Build recipe for `libcef.dll` from ungoogled + Chrome ffmpeg | — |

## Build & test now
```
dotnet test tests/Diaphane.Shell.Tests   # 59 tests, runs against FakeEngine
dotnet test tests/Diaphane.Core.Tests    # 1 live test (needs the engine build)
powershell scripts/ui-smoke.ps1          # real mouse+keyboard against a running window
powershell scripts/package.ps1           # -> dist/diaphane-<version>-win-x64.zip
dotnet build src/Diaphane.Privacy
```

Manual test checklist: `docs/manual-test-plan.md`.

The shell and all its logic build and test on any machine. The ~100 GB Chromium
checkout is only needed to produce `Diaphane.Core`'s native payload — see `engine/README.md`.

## Features

**Browsing**
- Tabs with drag-and-drop reordering; middle-click a tab to close it
- Middle-click / ctrl+click a link, or a `target="_blank"` link, opens a real new
  tab — never a stray native window
- Sandbox tabs share one ephemeral, in-memory context per window: no history,
  cookies, or downloads history ever touch disk
- Right-click a link, image, video, or audio element → **Save as…**, backed by a
  real Windows file picker

**Bookmarks**
- Left-hand panel (not a horizontal bar) — bookmarks and groups listed vertically,
  matching Docket's "This PC" panel treatment
- Right-click a group: Create Group, Rename, Open All in new tabs / new Sandbox
  tabs, Remove
- Right-click a bookmark: Open, Open in new tab, Edit (title + URL), Remove
- Drag a bookmark or group onto another group to reparent it

**Downloads**
- Tracked with filename, size/progress, and timestamp, most-recent-first
- Opens in the right-hand pane (shares space with DevTools); clear individually
  or all at once
- Default save location is overridable in Settings

**Chrome**
- VS Code–style left action rail for every non-navigation action (bookmarks,
  downloads, history, sandbox tab, extensions, DevTools, privacy, settings)
- Custom Mica title bar with a right-pane collapse/expand toggle (glyph, styling,
  and position matched to the Docket/Dispatch sibling apps)
- Omnibar spans the full toolbar width, styled like a command palette
- Window position/size and panel widths persist across restarts

**Privacy & extensions**
- One-click clear (cookies, cache, storage, history, DNS), clear-on-exit, and a
  Sandbox tab as permanent Incognito
- Load unpacked extensions; update checks are manual, never automatic
- DevTools docked in a resizable pane, driven over the loopback debugging port

**Media**
- Codec/DRM-key-system probe (in Settings), Widevine opt-in that's never
  auto-downloaded

## Milestones
✅ M1 engine pipeline · M2 core bridge · M3 single-tab shell · M4 tabs · M5 bookmarks+history+omnibox
· M6 sandbox tabs · M7 privacy dashboard + clear-data · M8 extensions (management + startup load)
· M9 DevTools + codecs + Widevine opt-in · M10 settings, session restore, packaging, manual update check
· M11 tab reorder, VS Code–style rail, bookmark groups + drag/drop, downloads, save-link/image/video-as,
  popup-to-real-tab routing, title-bar right-pane toggle

The roadmap milestones are complete. Remaining polish is tracked in `engine/STATUS.md`.
