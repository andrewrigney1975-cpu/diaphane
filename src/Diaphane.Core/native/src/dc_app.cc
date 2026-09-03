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
    command_line->AppendSwitch("disable-domain-reliability");
    command_line->AppendSwitch("disable-background-networking");
    command_line->AppendSwitch("disable-breakpad");
    command_line->AppendSwitch("disable-crash-reporter");
    command_line->AppendSwitch("disable-component-update");
  }
}

void DcApp::OnScheduleMessagePumpWork(int64_t delay_ms) {
  // CEF (external_message_pump mode) asks us to call CefDoMessageLoopWork()
  // after |delay_ms|. Hand that to the C# dispatcher via the caller's callback.
  if (pump_cb_)
    pump_cb_(static_cast<int32_t>(delay_ms), pump_user_);
}
