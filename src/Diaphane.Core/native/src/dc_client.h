#ifndef DIAPHANE_DC_CLIENT_H_
#define DIAPHANE_DC_CLIENT_H_

#include <string>

#include "include/cef_client.h"
#include "include/cef_context_menu_handler.h"
#include "include/cef_devtools_message_observer.h"
#include "include/cef_dialog_handler.h"
#include "include/cef_download_handler.h"
#include "include/cef_jsdialog_handler.h"
#include "include/cef_registration.h"
#include "include/cef_request_handler.h"
#include "include/diaphane_core.h"

// Implemented in diaphane_core.cc (owns dc_set_default_download_dir's state).
// Empty means "no override — use the engine's own default".
std::string DcDefaultDownloadDir();

// One DcClient per browser view. Forwards the handful of CEF events the shell
// cares about to the caller-supplied C callbacks, tagged with the view id.
class DcClient : public CefClient,
                 public CefLifeSpanHandler,
                 public CefLoadHandler,
                 public CefDisplayHandler,
                 public CefJSDialogHandler,
                 public CefDialogHandler,
                 public CefDownloadHandler,
                 public CefContextMenuHandler,
                 public CefRequestHandler,
                 public CefRenderHandler {
 public:
  DcClient(std::string view_id, dc_view_callbacks cb, int width, int height);

  CefRefPtr<CefBrowser> browser() const { return browser_; }
  const std::string& view_id() const { return view_id_; }
  void set_size(int w, int h) { width_ = w; height_ = h; }
  void set_pending_url(const std::string& u) { pending_url_ = u; }

  // Lazily register a DevTools message observer and route Runtime.evaluate
  // results to |cb|. Safe to call repeatedly (updates the callback).
  void EnsureEvalObserver(dc_eval_cb cb, void* user);

  // Drop the DevTools message observer registration. MUST run before the
  // browser is torn down — the CefRegistration dtor otherwise touches a
  // half-closed browser (dangling raw_ptr).
  void ReleaseDevToolsObserver();

  // Registered separately from dc_view_callbacks (which is set once at
  // dc_view_create and never changes shape) so download support stays
  // additive to the native ABI. Null clears it.
  void SetDownloadCallback(dc_download_cb cb, void* user) { download_cb_ = cb; download_user_ = user; }
  void SetPopupCallback(dc_popup_cb cb, void* user) { popup_cb_ = cb; popup_user_ = user; }
  void SetContextMenuCallback(dc_context_menu_cb cb, void* user) { context_menu_cb_ = cb; context_menu_user_ = user; }

  // CefClient
  CefRefPtr<CefLifeSpanHandler> GetLifeSpanHandler() override { return this; }
  CefRefPtr<CefLoadHandler> GetLoadHandler() override { return this; }
  CefRefPtr<CefDisplayHandler> GetDisplayHandler() override { return this; }
  CefRefPtr<CefJSDialogHandler> GetJSDialogHandler() override { return this; }
  CefRefPtr<CefDialogHandler> GetDialogHandler() override { return this; }
  CefRefPtr<CefDownloadHandler> GetDownloadHandler() override { return this; }
  CefRefPtr<CefContextMenuHandler> GetContextMenuHandler() override { return this; }
  CefRefPtr<CefRequestHandler> GetRequestHandler() override { return this; }
  CefRefPtr<CefRenderHandler> GetRenderHandler() override { return this; }

  // CefLifeSpanHandler
  // No popup window support (no second WinUI host to give it, and the shell has no
  // concept of an extra top-level browser window). Left unhandled, CEF's default is
  // to allow the popup AND hand it this same DcClient — a real, un-parented native
  // window would appear, and closing it would fire our OnAfterCreated/OnBeforeClose
  // for the wrong browser and stomp browser_, taking the real tab down with it.
  bool OnBeforePopup(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                     int popup_id, const CefString& target_url,
                     const CefString& target_frame_name,
                     CefLifeSpanHandler::WindowOpenDisposition target_disposition,
                     bool user_gesture, const CefPopupFeatures& popup_features,
                     CefWindowInfo& window_info, CefRefPtr<CefClient>& client,
                     CefBrowserSettings& settings,
                     CefRefPtr<CefDictionaryValue>& extra_info,
                     bool* no_javascript_access) override;
  void OnAfterCreated(CefRefPtr<CefBrowser> browser) override;
  void OnBeforeClose(CefRefPtr<CefBrowser> browser) override;

  // CefLoadHandler
  void OnLoadingStateChange(CefRefPtr<CefBrowser> browser, bool is_loading,
                            bool can_go_back, bool can_go_forward) override;
  void OnLoadEnd(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                 int http_status_code) override;

  // CefDisplayHandler
  void OnTitleChange(CefRefPtr<CefBrowser> browser, const CefString& title) override;
  void OnFaviconURLChange(CefRefPtr<CefBrowser> browser,
                          const std::vector<CefString>& icon_urls) override;

  // CefJSDialogHandler — no native dialog UI (OSR). Auto-dismiss page dialogs and
  // let navigations proceed past beforeunload prompts.
  bool OnJSDialog(CefRefPtr<CefBrowser> browser, const CefString& origin_url,
                  JSDialogType dialog_type, const CefString& message_text,
                  const CefString& default_prompt_text,
                  CefRefPtr<CefJSDialogCallback> callback,
                  bool& suppress_message) override;
  bool OnBeforeUnloadDialog(CefRefPtr<CefBrowser> browser,
                            const CefString& message_text, bool is_reload,
                            CefRefPtr<CefJSDialogCallback> callback) override;

  // CefDialogHandler — OSR has no window to parent an OS file dialog to. The
  // shell will drive file selection itself later; for now, cancel.
  bool OnFileDialog(CefRefPtr<CefBrowser> browser, FileDialogMode mode,
                    const CefString& title, const CefString& default_file_path,
                    const std::vector<CefString>& accept_filters,
                    const std::vector<CefString>& accept_extensions,
                    const std::vector<CefString>& accept_descriptions,
                    CefRefPtr<CefFileDialogCallback> callback) override;

  // CefDownloadHandler
  bool OnBeforeDownload(CefRefPtr<CefBrowser> browser,
                        CefRefPtr<CefDownloadItem> download_item,
                        const CefString& suggested_name,
                        CefRefPtr<CefBeforeDownloadCallback> callback) override;
  void OnDownloadUpdated(CefRefPtr<CefBrowser> browser,
                         CefRefPtr<CefDownloadItem> download_item,
                         CefRefPtr<CefDownloadItemCallback> callback) override;

  // CefContextMenuHandler — no native menu chrome to host CEF's own in (same reasoning
  // as OnFileDialog/OnBeforePopup). OnBeforeContextMenu reports what was clicked to the
  // shell; RunContextMenu then unconditionally declines to show anything itself.
  void OnBeforeContextMenu(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                           CefRefPtr<CefContextMenuParams> params,
                           CefRefPtr<CefMenuModel> model) override;
  bool RunContextMenu(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                      CefRefPtr<CefContextMenuParams> params, CefRefPtr<CefMenuModel> model,
                      CefRefPtr<CefRunContextMenuCallback> callback) override;

  // CefRequestHandler — middle-click / ctrl+click "open in new tab" navigations arrive
  // here (not through OnBeforePopup, which is window.open()/target=_blank only). Reuses
  // the same popup_cb_ the shell already listens on to open a real tab.
  bool OnOpenURLFromTab(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame,
                       const CefString& target_url,
                       CefRequestHandler::WindowOpenDisposition target_disposition,
                       bool user_gesture) override;

  // CefRenderHandler (windowless / OSR)
  void GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) override;
  bool GetScreenInfo(CefRefPtr<CefBrowser> browser, CefScreenInfo& info) override;
  bool GetScreenPoint(CefRefPtr<CefBrowser> browser, int viewX, int viewY,
                      int& screenX, int& screenY) override;
  void OnPopupShow(CefRefPtr<CefBrowser> browser, bool show) override;
  void OnPopupSize(CefRefPtr<CefBrowser> browser, const CefRect& rect) override;
  void OnPaint(CefRefPtr<CefBrowser> browser, PaintElementType type,
               const RectList& dirtyRects, const void* buffer,
               int width, int height) override;

 private:
  void PushNavState(CefRefPtr<CefBrowser> browser, bool is_loading,
                    bool can_back, bool can_fwd);

  std::string view_id_;
  dc_view_callbacks cb_;
  CefRefPtr<CefBrowser> browser_;
  std::string pending_url_;   // navigation requested before OnAfterCreated
  int width_;
  int height_;

  CefRefPtr<CefDevToolsMessageObserver> eval_observer_;
  CefRefPtr<CefRegistration> devtools_reg_;   // keeps the observer registered
  CefRect popup_rect_;                        // <select> etc. dropdown, when open
  bool popup_open_ = false;

  dc_download_cb download_cb_ = nullptr;
  void* download_user_ = nullptr;
  dc_popup_cb popup_cb_ = nullptr;
  void* popup_user_ = nullptr;
  dc_context_menu_cb context_menu_cb_ = nullptr;
  void* context_menu_user_ = nullptr;

  IMPLEMENT_REFCOUNTING(DcClient);
  DISALLOW_COPY_AND_ASSIGN(DcClient);
};

#endif  // DIAPHANE_DC_CLIENT_H_
