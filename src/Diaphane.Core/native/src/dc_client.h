#ifndef DIAPHANE_DC_CLIENT_H_
#define DIAPHANE_DC_CLIENT_H_

#include <string>

#include "include/cef_client.h"
#include "include/cef_jsdialog_handler.h"
#include "include/diaphane_core.h"

// One DcClient per browser view. Forwards the handful of CEF events the shell
// cares about to the caller-supplied C callbacks, tagged with the view id.
class DcClient : public CefClient,
                 public CefLifeSpanHandler,
                 public CefLoadHandler,
                 public CefDisplayHandler,
                 public CefJSDialogHandler,
                 public CefRenderHandler {
 public:
  DcClient(std::string view_id, dc_view_callbacks cb, int width, int height);

  CefRefPtr<CefBrowser> browser() const { return browser_; }
  const std::string& view_id() const { return view_id_; }
  void set_size(int w, int h) { width_ = w; height_ = h; }
  void set_pending_url(const std::string& u) { pending_url_ = u; }

  // CefClient
  CefRefPtr<CefLifeSpanHandler> GetLifeSpanHandler() override { return this; }
  CefRefPtr<CefLoadHandler> GetLoadHandler() override { return this; }
  CefRefPtr<CefDisplayHandler> GetDisplayHandler() override { return this; }
  CefRefPtr<CefJSDialogHandler> GetJSDialogHandler() override { return this; }
  CefRefPtr<CefRenderHandler> GetRenderHandler() override { return this; }

  // CefLifeSpanHandler
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

  // CefRenderHandler (windowless / OSR)
  void GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) override;
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

  IMPLEMENT_REFCOUNTING(DcClient);
  DISALLOW_COPY_AND_ASSIGN(DcClient);
};

#endif  // DIAPHANE_DC_CLIENT_H_
