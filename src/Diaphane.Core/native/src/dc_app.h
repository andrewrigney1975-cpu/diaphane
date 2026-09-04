#ifndef DIAPHANE_DC_APP_H_
#define DIAPHANE_DC_APP_H_

#include <string>

#include "include/cef_app.h"
#include "include/diaphane_core.h"

// Browser-process application delegate. Also serves as the render-process app in
// the same binary (the helper exe reuses this). Kept intentionally small:
// privacy-relevant command-line hardening happens here, everything else is CEF
// default behaviour.
class DcApp : public CefApp,
              public CefBrowserProcessHandler {
 public:
  DcApp() = default;

  void SetPumpCallback(dc_schedule_pump_cb cb, void* user) {
    pump_cb_ = cb;
    pump_user_ = user;
  }
  void SetUserDataDir(const CefString& dir) { user_data_dir_ = dir; }
  void SetExtensionDirs(const std::string& semi_list) { extension_dirs_ = semi_list; }
  bool context_initialized() const { return context_initialized_; }

  // CefApp
  void OnBeforeCommandLineProcessing(const CefString& process_type,
                                     CefRefPtr<CefCommandLine> command_line) override;
  CefRefPtr<CefBrowserProcessHandler> GetBrowserProcessHandler() override { return this; }

  // CefBrowserProcessHandler
  void OnContextInitialized() override { context_initialized_ = true; }
  void OnScheduleMessagePumpWork(int64_t delay_ms) override;

 private:
  dc_schedule_pump_cb pump_cb_ = nullptr;
  void* pump_user_ = nullptr;
  bool context_initialized_ = false;
  CefString user_data_dir_;
  std::string extension_dirs_;   // ';'-separated; becomes --load-extension

  IMPLEMENT_REFCOUNTING(DcApp);
  DISALLOW_COPY_AND_ASSIGN(DcApp);
};

#endif  // DIAPHANE_DC_APP_H_
