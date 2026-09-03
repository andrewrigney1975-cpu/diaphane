#include "src/dc_client.h"

#if defined(_WIN32)
#include <windows.h>
#endif

#include "include/cef_browser.h"
#include "include/cef_frame.h"

DcClient::DcClient(std::string view_id, dc_view_callbacks cb, int width, int height)
    : view_id_(std::move(view_id)), cb_(cb), width_(width), height_(height) {}

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

void DcClient::GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) {
  rect.Set(0, 0, width_ > 0 ? width_ : 1280, height_ > 0 ? height_ : 800);
}
