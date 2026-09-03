// diaphane_helper.exe — the CEF sub-process host.
//
// The main application is managed (.NET) and cannot call CefExecuteProcess()
// early enough, so CEF's render / GPU / utility / network processes are launched
// as this tiny native executable instead (CefSettings.browser_subprocess_path).
#include <windows.h>

#include "include/cef_app.h"

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE, LPWSTR, int) {
  CefMainArgs main_args(hInstance);
  // A null app is fine here: every non-browser process type gets CEF defaults,
  // and this binary is never used for the browser process.
  return CefExecuteProcess(main_args, nullptr, nullptr);
}
