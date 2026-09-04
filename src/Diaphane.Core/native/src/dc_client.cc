#include "src/dc_client.h"

#if defined(_WIN32)
#include <windows.h>
#endif

#include <cstdlib>
#include <string>

#include "include/cef_browser.h"
#include "include/cef_download_item.h"
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

void DcClient::ReleaseDevToolsObserver() {
  devtools_reg_ = nullptr;
  eval_observer_ = nullptr;
}

bool DcClient::OnBeforePopup(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                             int popup_id, const CefString& target_url,
                             const CefString& target_frame_name,
                             CefLifeSpanHandler::WindowOpenDisposition target_disposition,
                             bool user_gesture, const CefPopupFeatures& popup_features,
                             CefWindowInfo& window_info, CefRefPtr<CefClient>& client,
                             CefBrowserSettings& settings,
                             CefRefPtr<CefDictionaryValue>& extra_info,
                             bool* no_javascript_access) {
  // Always cancel — there's no second top-level window to host a real popup in.
  // The shell opens target_url in one of its own tabs instead, if it's listening.
  if (popup_cb_ && !target_url.empty())
    popup_cb_(view_id_.c_str(), target_url.ToString().c_str(), popup_user_);
  return true;
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
  ReleaseDevToolsObserver();
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

bool DcClient::OnFileDialog(CefRefPtr<CefBrowser> browser, FileDialogMode mode,
                           const CefString& title, const CefString& default_file_path,
                           const std::vector<CefString>& accept_filters,
                           const std::vector<CefString>& accept_extensions,
                           const std::vector<CefString>& accept_descriptions,
                           CefRefPtr<CefFileDialogCallback> callback) {
  callback->Cancel();
  return true;
}

namespace {
int32_t DownloadStateOf(CefRefPtr<CefDownloadItem> item) {
  if (item->IsComplete()) return 1;
  if (item->IsCanceled()) return 2;
  if (item->IsInterrupted()) return 3;
  return 0;  // in progress
}
}  // namespace

bool DcClient::OnBeforeDownload(CefRefPtr<CefBrowser> browser,
                                CefRefPtr<CefDownloadItem> download_item,
                                const CefString& suggested_name,
                                CefRefPtr<CefBeforeDownloadCallback> callback) {
  std::string dir = DcDefaultDownloadDir();
  std::string name = suggested_name.ToString();
  std::string path = dir.empty() ? name : dir + name;
  // show_dialog=false: no native window to parent a Save As dialog to (OSR).
  callback->Continue(path, /*show_dialog=*/false);
  return true;  // returning false (the default) silently drops the download
}

void DcClient::OnDownloadUpdated(CefRefPtr<CefBrowser> browser,
                                 CefRefPtr<CefDownloadItem> download_item,
                                 CefRefPtr<CefDownloadItemCallback> callback) {
  if (!download_cb_ || !download_item->IsValid()) return;
  std::string url = download_item->GetURL().ToString();
  std::string name = download_item->GetSuggestedFileName().ToString();
  std::string path = download_item->GetFullPath().ToString();
  download_cb_(view_id_.c_str(), static_cast<int64_t>(download_item->GetId()), url.c_str(),
              name.c_str(), path.c_str(), download_item->GetReceivedBytes(),
              download_item->GetTotalBytes(), DownloadStateOf(download_item), download_user_);
}

void DcClient::GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) {
  rect.Set(0, 0, width_ > 0 ? width_ : 1280, height_ > 0 ? height_ : 800);
}

bool DcClient::GetScreenInfo(CefRefPtr<CefBrowser> browser, CefScreenInfo& info) {
  info.device_scale_factor = 1.0f;
  info.depth = 32;
  info.depth_per_component = 8;
  info.is_monochrome = false;
  info.rect.x = info.rect.y = 0;
  info.rect.width = width_ > 0 ? width_ : 1280;
  info.rect.height = height_ > 0 ? height_ : 800;
  info.available_rect = info.rect;
  return true;
}

bool DcClient::GetScreenPoint(CefRefPtr<CefBrowser> browser, int viewX, int viewY,
                              int& screenX, int& screenY) {
  screenX = viewX;
  screenY = viewY;
  return true;
}

void DcClient::OnPopupShow(CefRefPtr<CefBrowser> browser, bool show) {
  popup_open_ = show;
  if (!show) popup_rect_.Set(0, 0, 0, 0);
}

void DcClient::OnPopupSize(CefRefPtr<CefBrowser> browser, const CefRect& rect) {
  popup_rect_ = rect;
}

void DcClient::OnPaint(CefRefPtr<CefBrowser> browser, PaintElementType type,
                       const RectList& dirtyRects, const void* buffer,
                       int width, int height) {
  if (type != PET_VIEW || !cb_.on_paint) return;
  CefRect d = dirtyRects.empty() ? CefRect(0, 0, width, height) : dirtyRects.front();
  cb_.on_paint(view_id_.c_str(), buffer, width, height,
               d.x, d.y, d.width, d.height, cb_.user);
}
