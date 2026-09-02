# diaphane — working notes

Privacy-first WinUI 3 browser on an ungoogled-chromium engine (libcef).

## Golden rules
- **No new network callbacks.** Any code path that contacts a host must be traceable to an
  explicit user action. The `Diaphane.Privacy.NoPhoneHome` CI gate (M7) fails on any
  unexpected DNS query. When in doubt, don't call out.
- **`IBrowserEngine` is the only seam to native.** Everything in `Diaphane.Shell` / `Data` /
  `Privacy` must build and unit-test with no CEF. Native lives solely in `Diaphane.Core`.
- **Sandbox tabs never touch disk stores.** No `HistoryStore.RecordVisit`, no persistent
  context. A sandbox context is disposed when its last tab closes (see `TabManager.Close`).
- **Extensions: no silent auto-update.** Update checks are user-initiated only.

## Build
- `dotnet test tests/Diaphane.Shell.Tests` — fast, no engine needed.
- `Diaphane.Core` + `Diaphane.App` need the engine build (`engine/README.md`).
- Target framework for engine-free projects: `net8.0`. App: `net10.0-windows` + Windows App SDK.

## Conventions
- Records for DTOs, `INotifyPropertyChanged` for view models.
- SQLite via `Microsoft.Data.Sqlite`, hand-written SQL, no ORM.
- Internal pages are `diaphane://` (privacy, settings, newtab, extensions).
