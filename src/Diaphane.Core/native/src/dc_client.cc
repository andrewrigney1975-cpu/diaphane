#include "src/dc_client.h"

#if defined(_WIN32)
#include <windows.h>
#endif

#include <string>

#include "include/cef_browser.h"
#include "include/cef_frame.h"

namespace {

// Bridges CefDevToolsMessageObserver -> the C dc_eval_cb. Separate class so
// DcClient doesn't multiply-inherit two CefBaseRefCounted interfaces.
class DcEvalObserver : public CefDevToolsMessageObserver {
 public:
  explicit DcEvalObserver(std::string view_id) : view_id_(std::move(view_id)) {}
  void set_callback(dc_eval_cb cb, void* user) { cb_ = cb; user_ = user; }

  void OnDevToolsMethodResult(CefRefPtr<CefBrowser> browser, int message_id,
                              bool success, const void* result,
                              size_t result_size) override {
    if (!cb_) return;
    std::string json(static_cast<const char*>(result), result_size);
    cb_(view_id_.c_str(), message_id, success ? 1 : 0, json.c_str(), user_);
  }

 private:
  std::string view_id_;
  dc_eval_cb cb_ = nullptr;
  void* user_ = nullptr;
  IMPLEMENT_REFCOUNTING(DcEvalObserver);
  DISALLOW_COPY_AND_ASSIGN(DcEvalObserver);
};

}  // namespace

DcClient::DcClient(std::string view_id, dc_view_callbacks cb, int width, int height)
    : view_id_(std::move(view_id)), cb_(cb), width_(width), height_(height) {}

void DcClient::EnsureEvalObserver(dc_eval_cb cb, void* user) {
  if (!browser_) return;
  if (!eval_observer_) {
    auto obs = new DcEvalObserver(view_id_);
    eval_observer_ = obs;
    devtools_reg_ = browser_->GetHost()->AddDevToolsMessageObserver(eval_observer_);
  }
  static_cast<DcEvalObserver*>(eval_observer_.get())->set_callback(cb, user);
}

void DcClient::OnAfterCreated(CefRefPtr<CefBrowser> browser) {
  browser_ = browser;
  // The interpose host window starts hidden (WS_CHILD without WS_VISIBLE); the
  // shell reveals it via dc_view_set_bounds once the content rect is known.
  if (cb_.on_created) cb_.on_created(view_id_.c_str(), cb_.user);
  if (!pending_url_.empty()) {
    browser_->GetMainFrame()->LoadURL(CefString(pending_url_));
    pending_url_.clear();
  }
}

void DcClient::OnBeforeClose(CefRefPtr<CefBrowser> browser) {
  if (cb_.on_closed) cb_.on_closed(view_id_.c_str(), cb_.user);
  browser_ = nullptr;
}

void DcClient::PushNavState(CefRefPtr<CefBrowser> browser, bool is_loading,
                            bool can_back, bool can_fwd) {
  if (!cb_.on_nav_state) return;
  CefRefPtr<CefFrame> main = browser->GetMainFrame();
  const std::string url = main ? main->GetURL().ToString() : std::string();
  cb_.on_nav_state(view_id_.c_str(), url.c_str(), /*title*/ "",
                   is_loading ? 1 : 0, can_back ? 1 : 0, can_fwd ? 1 : 0,
                   is_loading ? 0.0 : 1.0, cb_.user);
}

void DcClient::OnLoadingStateChange(CefRefPtr<CefBrowser> browser, bool is_loading,
                                    bool can_go_back, bool can_go_forward) {
  PushNavState(browser, is_loading, can_go_back, can_go_forward);
}

void DcClient::OnLoadEnd(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                         int http_status_code) {
  if (frame && frame->IsMain() && cb_.on_load_end)
    cb_.on_load_end(view_id_.c_str(), http_status_code, cb_.user);
}

void DcClient::OnTitleChange(CefRefPtr<CefBrowser> browser, const CefString& title) {
  if (cb_.on_title) cb_.on_title(view_id_.c_str(), title.ToString().c_str(), cb_.user);
}

void DcClient::OnFaviconURLChange(CefRefPtr<CefBrowser> browser,
                                  const std::vector<CefString>& icon_urls) {
  if (cb_.on_favicon && !icon_urls.empty())
    cb_.on_favicon(view_id_.c_str(), icon_urls.front().ToString().c_str(), cb_.user);
}

bool DcClient::OnJSDialog(CefRefPtr<CefBrowser> browser, const CefString& origin_url,
                          JSDialogType dialog_type, const CefString& message_text,
                          const CefString& default_prompt_text,
                          CefRefPtr<CefJSDialogCallback> callback,
                          bool& suppress_message) {
  // We render off-screen with no dialog surface. Suppress the prompt and treat
  // it as dismissed (alert = ok, confirm/prompt = cancel) so a page can never
  // wedge the UI thread waiting on a dialog that can't be shown.
  suppress_message = true;
  callback->Continue(dialog_type == JSDIALOGTYPE_ALERT, CefString());
  return true;
}

bool DcClient::OnBeforeUnloadDialog(CefRefPtr<CefBrowser> browser,
                                    const CefString& message_text, bool is_reload,
                                    CefRefPtr<CefJSDialogCallback> callback) {
  // Always allow the navigation / reload to proceed — without this, navigating
  // away from any page that registers `onbeforeunload` hangs in OSR mode.
  callback->Continue(true, CefString());
  return true;
}

void DcClient::GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) {
  rect.Set(0, 0, width_ > 0 ? width_ : 1280, height_ > 0 ? height_ : 800);
}

void DcClient::OnPaint(CefRefPtr<CefBrowser> browser, PaintElementType type,
                       const RectList& dirtyRects, const void* buffer,
                       int width, int height) {
  if (type != PET_VIEW || !cb_.on_paint) return;
  CefRect d = dirtyRects.empty() ? CefRect(0, 0, width, height) : dirtyRects.front();
  cb_.on_paint(view_id_.c_str(), buffer, width, height,
               d.x, d.y, d.width, d.height, cb_.user);
}
