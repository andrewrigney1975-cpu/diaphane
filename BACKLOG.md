# Backlog

Not yet scheduled. Rough notes on scope so each can be picked up cold.

## In-situ task manager

Chrome's Shift+Esc equivalent — a `diaphane://` page (or a panel like Downloads/
DevTools) listing every renderer process (one per tab/site-instance, plus the GPU
and network-service processes), each with live CPU/memory, and a way to kill a
single hung one without taking down the rest of the browser. Tabs already run as
isolated OS processes (CEF's normal multi-process architecture, no `single_process`
flag set — see `engine/README.md` and `CefHost`), so a crash or hang in one
renderer shouldn't affect others; this would just make that isolation visible and
actionable from inside the app instead of only via the OS's own Task Manager.
