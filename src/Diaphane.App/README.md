# Diaphane.App

The WinUI 3 shell (milestone **M3**). Not scaffolded yet because it needs the
Windows App SDK workload and a running `Diaphane.Core` native payload.

## Planned shape
- `net10.0-windows10.0.19041.0`, `<UseWinUI>true</UseWinUI>`, packaged (MSIX) + unpackaged.
- `MainWindow`: custom title bar, `TabView` bound to `TabManager.Tabs`, unified address bar
  (`AutoSuggestBox` driven by `OmniboxParser` + `HistoryStore.Suggest` + `BookmarkStore.Suggest`).
- Per-tab `ContentIsland` / `DesktopChildSiteBridge` hosting the CEF child HWND.
- Floating popup windows for overlays that must sit above page content (find bar,
  permission prompts, omnibox dropdown overflow) — see architecture §04 airspace note.
- `diaphane://` pages rendered from `Diaphane.Pages` (privacy dashboard, settings, new tab).
- DI root wires `IBrowserEngine` (real `Diaphane.Core` in app, `FakeEngine` in design-time).

## First screen
New standard tab open on `diaphane://newtab`, address bar focused, bookmarks bar visible.
Sandbox windows: violet accent on title bar + tab strip.
