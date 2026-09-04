#include "src/dc_app.h"

#include <string>

#include "include/cef_command_line.h"

void DcApp::OnBeforeCommandLineProcessing(const CefString& process_type,
                                          CefRefPtr<CefCommandLine> command_line) {
  // Belt-and-braces privacy hardening on top of the ungoogled patch set + GN flags.
  if (process_type.empty()) {
    const char* kDisableFeatures[] = {
        "OptimizationHints", "MediaRouter", "AutofillServerCommunication",
        "InterestFeedContentSuggestions", "Translate",
        "SafeBrowsingEnhancedProtection",
    };
    std::string disabled;
    if (command_line->HasSwitch("disable-features"))
      disabled = command_line->GetSwitchValue("disable-features").ToString();
    for (const char* f : kDisableFeatures) {
      if (!disabled.empty()) disabled += ",";
      disabled += f;
    }
    command_line->AppendSwitchWithValue("disable-features", disabled);

    command_line->AppendSwitch("no-pings");
    command_line->AppendSwitch("no-default-browser-check");
    command_line->AppendSwitch("no-first-run");
    command_line->AppendSwitch("disable-domain-reliability");
    command_line->AppendSwitch("disable-background-networking");
    command_line->AppendSwitch("disable-breakpad");
    command_line->AppendSwitch("disable-crash-reporter");
    command_line->AppendSwitch("disable-component-update");
    command_line->AppendSwitch("disable-session-crashed-bubble");
    command_line->AppendSwitch("hide-crash-restore-bubble");

    // Unpacked extensions the user explicitly loaded (M8). Comma-separated for
    // Chromium; we store them ';'-separated. No update_url is ever contacted —
    // extension update checks are user-initiated only (see CLAUDE.md).
    if (!extension_dirs_.empty()) {
      std::string csv = extension_dirs_;
      for (char& c : csv) if (c == ';') c = ',';
      command_line->AppendSwitchWithValue("load-extension", csv);
    }
    // NOTE: process-wide GPU switches (--disable-gpu / --in-process-gpu /
    // --use-angle) break WinUI 3's own compositor since it shares this process.
    // The GPU-process instability seen when hosting via a raw child HWND is one
    // reason the ContentIsland / OSR path is the right host for the shell.
  }
}

void DcApp::OnScheduleMessagePumpWork(int64_t delay_ms) {
  // CEF (external_message_pump mode) asks us to call CefDoMessageLoopWork()
  // after |delay_ms|. Hand that to the C# dispatcher via the caller's callback.
  if (pump_cb_)
    pump_cb_(static_cast<int32_t>(delay_ms), pump_user_);
}
