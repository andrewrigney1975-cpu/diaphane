# DiaphaneCore native bridge (M2)

`DiaphaneCore.dll` — the **only** code that touches libcef. Flat C ABI
(`include/diaphane_core.h`) consumed from C# via P/Invoke (`NativeMethods.cs`).
`diaphane_helper.exe` is the CEF sub-process host (render / GPU / utility / network).

## Build
```
pwsh src/Diaphane.Core/native/build.ps1   # -Sdk <path> defaults to F:\cef-build\dist\cef
```
Uses the CMake + Ninja bundled with Visual Studio 2026. Rebuilds
`libcef_dll_wrapper` from the SDK sources with MSVC so its CRT/STL matches ours
(`/MT`, C++20). Stages a runnable layout in `build/bin/`:
`DiaphaneCore.dll`, `diaphane_helper.exe`, `libcef.dll`, ANGLE/SwiftShader,
`*.pak`, `icudtl.dat`, `locales/`.

## Contract
`CefEngine : IBrowserEngine` (Diaphane.Shell.Engine). Everything must be called
from **one STA thread** that also calls `DoMessageLoopWork()` — CEF initializes
COM as STA on that thread. `external_message_pump` mode: CEF raises
`ScheduleMessagePump` to ask for a pump slice.

## Gate (M2) — passing
`dotnet test tests/Diaphane.Core.Tests` : headless `data:`/https load, title
callback flows C# ← libcef. `tools/HeadlessLoad` is the same as a console app.

## SDK headers added beyond `cef/include/` + `cef/libcef_dll/`
- `gen/cef/include/*` (cef_version.h, cef_api_versions.h, ...)
- `net/base/net_error_list.h` → `include/base/internal/cef_net_error_list.h` (per CEF's transfer.cfg)
