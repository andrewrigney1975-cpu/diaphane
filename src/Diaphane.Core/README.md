# Diaphane.Core

The **only** place that touches libcef. A C++/WinRT component that implements the
`Diaphane.Shell.Engine.IBrowserEngine` contract in native code and projects it into C#.

## Why C++/WinRT and not CefSharp / P/Invoke
- CEF's API is C++; the `libcef_dll_wrapper` is C++. A C++/WinRT layer wraps it directly
  with no marshalling guesswork.
- WinRT projection gives the WinUI app first-class async types and events.
- Keeps every CEF thread-affinity rule on the native side; C# only ever sees the
  marshalled `IBrowserEngine`.

## Responsibilities
| Area | Detail |
|---|---|
| Lifecycle | `CefInitialize` with `external_message_pump = true`; raise `ScheduleMessagePump`. |
| Contexts | Map `RequestContextOptions` → `CefRequestContextSettings`. Sandbox = no cache path → in-memory. |
| Views | Windowed `CefBrowserHost` child HWND parented to the host window passed from the shell. |
| Events | Translate `CefLoadHandler` / `CefDisplayHandler` callbacks to WinRT events on the dispatcher. |
| Extensions | `CefRequestContext::LoadExtension`; expose list + enable/disable. No auto-update. |
| Clearing | `DeleteCookies`, storage-partition clear, cache-dir wipe, host-resolver flush. |

## Native payload
`native/` holds the vcxproj. It consumes the engine build output
(`engine/out/Release/*`) via a local NuGet package `Diaphane.Core.Native`.
Not buildable without that package — CI builds it in the engine job.

## Status
Contract defined in `Diaphane.Shell/Engine/IBrowserEngine.cs`. Native implementation
is milestone **M2**. Until then the shell runs against `FakeEngine` (see tests).
