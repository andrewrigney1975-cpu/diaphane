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
dotnet test tests/Diaphane.Shell.Tests   # 44 tests, runs against FakeEngine
dotnet test tests/Diaphane.Core.Tests    # 1 live test (needs the engine build)
powershell scripts/ui-smoke.ps1          # real mouse+keyboard against a running window
powershell scripts/package.ps1           # -> dist/diaphane-<version>-win-x64.zip
dotnet build src/Diaphane.Privacy
```

Manual test checklist: `docs/manual-test-plan.md`.

The shell and all its logic build and test on any machine. The ~100 GB Chromium
checkout is only needed to produce `Diaphane.Core`'s native payload — see `engine/README.md`.

## Milestones
✅ M1 engine pipeline · M2 core bridge · M3 single-tab shell · M4 tabs · M5 bookmarks+history+omnibox
· M6 sandbox tabs · M7 privacy dashboard + clear-data · M8 extensions (management + startup load)
· M9 DevTools + codecs + Widevine opt-in · M10 settings, session restore, packaging, manual update check

The roadmap milestones are complete. Remaining polish is tracked in `engine/STATUS.md`.
