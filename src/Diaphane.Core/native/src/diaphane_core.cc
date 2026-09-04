// diaphane_core.cc — implementation of the flat C ABI in include/diaphane_core.h.
//
// Threading contract: every dc_* function (except the pump callback, which CEF
// invokes) MUST be called from the single STA thread that owns the message loop
// and calls dc_pump(). CEF initializes COM as STA on that thread.
// DIAPHANE_CORE_IMPL is defined by the build (CMakeLists.txt).
#include "include/diaphane_core.h"

#include <atomic>
#include <fstream>
#include <string>
#include <unordered_map>

#include "include/cef_app.h"
#include "include/cef_browser.h"
#include "include/cef_cookie.h"
#include "include/cef_request_context.h"
#include "include/cef_values.h"
#include "include/cef_version.h"
#include "include/wrapper/cef_helpers.h"

#include "src/dc_app.h"
#include "src/dc_client.h"

#if defined(_WIN32)
#include <windows.h>
#endif

namespace {

CefRefPtr<DcApp> g_app;
bool g_initialized = false;
int g_windowless = 0;
std::string g_root_cache;
std::atomic<uint64_t> g_next_id{1};

std::unordered_map<std::string, CefRefPtr<CefRequestContext>> g_contexts;
std::unordered_map<std::string, CefRefPtr<DcClient>> g_views;
std::unordered_map<std::string, HWND> g_view_hosts;   // intermediate Win32 host per view
std::string g_version_str;

#if defined(_WIN32)
// Chromium's windowed GPU compositor creates a GL child window of the browser
// HWND and hits NOTREACHED (child_window_win.cc) if that HWND sits directly under
// a foreign framework window (the WinUI content island). Interposing a plain
// Win32 child window gives Chromium the "normal" parent it expects.
const wchar_t* kHostClass = L"DiaphaneCefHost";

HWND CreateInterposeWindow(HWND parent, int w, int h) {
  static bool registered = false;
  if (!registered) {
    WNDCLASSEXW wc = {sizeof(wc)};
    wc.lpfnWndProc = ::DefWindowProcW;
    wc.hInstance = ::GetModuleHandleW(nullptr);
    wc.hCursor = ::LoadCursor(nullptr, IDC_ARROW);
    wc.lpszClassName = kHostClass;
    ::RegisterClassExW(&wc);
    registered = true;
  }
  return ::CreateWindowExW(
      0, kHostClass, L"", WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
      0, 0, w > 0 ? w : 1280, h > 0 ? h : 800,
      parent, nullptr, ::GetModuleHandleW(nullptr), nullptr);
}
#endif

std::string NewId(const char* prefix) {
  return std::string(prefix) + std::to_string(g_next_id.fetch_add(1));
}

CefRefPtr<CefRequestContext> LookupContext(const char* id) {
  if (!id) return nullptr;
  auto it = g_contexts.find(id);
  return it != g_contexts.end() ? it->second : nullptr;
}

CefRefPtr<DcClient> LookupView(const char* id) {
  if (!id) return nullptr;
  auto it = g_views.find(id);
  return it != g_views.end() ? it->second : nullptr;
}

CefRefPtr<CefBrowser> LookupBrowser(const char* id) {
  auto c = LookupView(id);
  return c ? c->browser() : nullptr;
}

}  // namespace

extern "C" {

int32_t dc_initialize(const dc_settings* s,
                      dc_schedule_pump_cb pump_cb, void* pump_user) {
  if (g_initialized || !s) return 0;

#if defined(_WIN32)
  CefMainArgs main_args(::GetModuleHandle(nullptr));
#else
  CefMainArgs main_args(0, nullptr);
#endif

  g_app = new DcApp();
  g_app->SetPumpCallback(pump_cb, pump_user);
  if (s->root_cache_dir && *s->root_cache_dir)
    g_app->SetUserDataDir(CefString(s->root_cache_dir));
  if (s->extension_dirs && *s->extension_dirs)
    g_app->SetExtensionDirs(s->extension_dirs);
  g_app->SetWidevineAllowed(s->allow_widevine != 0);
  g_app->SetDevToolsEnabled(s->devtools != 0);
  if (s->root_cache_dir) g_root_cache = s->root_cache_dir;

  // Even in the browser process CEF wants this called first (early init). It
  // returns >= 0 only when this process is actually a sub-process, which it
  // never is here (we ship diaphane_helper.exe for that), so treat >=0 as fatal.
  if (CefExecuteProcess(main_args, g_app.get(), nullptr) >= 0)
    return 0;

  CefSettings settings;
  settings.no_sandbox = s->no_sandbox ? 1 : 0;
  settings.multi_threaded_message_loop = 0;
  settings.external_message_pump = 1;
  settings.windowless_rendering_enabled = s->windowless ? 1 : 0;
  settings.log_severity = LOGSEVERITY_INFO;
  settings.persist_session_cookies = 0;   // don't keep session cookies across runs
  if (s->root_cache_dir && *s->root_cache_dir) {
    // Both must be set and equal — otherwise the Chrome runtime falls back to a
    // platform-default (or an existing Chrome/Edge) user-data dir and inherits
    // its bookmarks / session. This is a privacy-first browser: never do that.
    CefString(&settings.root_cache_path) = s->root_cache_dir;
    CefString(&settings.cache_path) = s->root_cache_dir;
    std::string log = std::string(s->root_cache_dir) + "\\diaphane_cef.log";
    CefString(&settings.log_file) = log;
  }
  g_windowless = s->windowless ? 1 : 0;

  if (s->root_cache_dir && *s->root_cache_dir)
    CefString(&settings.root_cache_path) = s->root_cache_dir;
  if (s->resources_dir && *s->resources_dir)
    CefString(&settings.resources_dir_path) = s->resources_dir;
  if (s->locales_dir && *s->locales_dir)
    CefString(&settings.locales_dir_path) = s->locales_dir;
  if (s->subprocess_path && *s->subprocess_path)
    CefString(&settings.browser_subprocess_path) = s->subprocess_path;
  if (s->user_agent && *s->user_agent)
    CefString(&settings.user_agent) = s->user_agent;

  if (!CefInitialize(main_args, settings, g_app.get(), nullptr))
    return 0;

  g_initialized = true;
  return 1;
}

void dc_pump(void) {
  if (!g_initialized) return;
  // Never call CefDoMessageLoopWork() reentrantly (CEF forbids it) — a CEF
  // callback dispatched from here can pump the host loop and land back in dc_pump.
  static bool in_pump = false;
  if (in_pump) return;
  in_pump = true;
  CefDoMessageLoopWork();
  in_pump = false;
}

void dc_shutdown(void) {
  if (!g_initialized) return;
  for (auto& kv : g_views) {
    if (auto b = kv.second->browser())
      b->GetHost()->CloseBrowser(true);
  }
  // Drain close events.
  for (int i = 0; i < 50; ++i) CefDoMessageLoopWork();
  g_views.clear();
  g_contexts.clear();
  CefShutdown();
  g_app = nullptr;
  g_initialized = false;
}

const char* dc_context_create(int32_t persistent, const char* cache_path,
                              const char* proxy_uri) {
  if (!g_initialized) return nullptr;
  CefRequestContextSettings rc;
  if (persistent && cache_path && *cache_path)
    CefString(&rc.cache_path) = cache_path;
  // persistent=0 or empty cache_path => fully in-memory (the Sandbox partition).

  CefRefPtr<CefRequestContext> ctx =
      CefRequestContext::CreateContext(rc, nullptr);
  if (!ctx) return nullptr;

  // TODO(diaphane): honour proxy_uri via ctx->SetPreference("proxy", ...).
  (void)proxy_uri;

  auto id = NewId("ctx-");
  auto res = g_contexts.emplace(std::move(id), ctx);
  return res.first->first.c_str();
}

const char* dc_context_standard(void) {
  if (!g_initialized) return nullptr;
  static const char* kId = nullptr;
  if (!kId) {
    auto res = g_contexts.emplace("ctx-global", CefRequestContext::GetGlobalContext());
    kId = res.first->first.c_str();
  }
  return kId;
}

void dc_context_release(const char* ctx_id) {
  if (ctx_id && std::string(ctx_id) != "ctx-global")
    g_contexts.erase(ctx_id);
}

int32_t dc_context_clear_cookies(const char* ctx_id, int64_t /*since*/) {
  auto ctx = LookupContext(ctx_id);
  if (!ctx) return 0;
  auto cm = ctx->GetCookieManager(nullptr);
  return cm && cm->DeleteCookies(CefString(), CefString(), nullptr) ? 1 : 0;
}

int32_t dc_context_clear_storage(const char* ctx_id, int64_t /*since*/) {
  // localStorage / IndexedDB clearing needs the Chrome BrowsingDataRemover,
  // which CEF does not expose directly. Sandbox contexts are in-memory so this
  // only matters for the standard context. Tracked for a later milestone.
  return LookupContext(ctx_id) ? 1 : 0;
}

int32_t dc_context_clear_cache(const char* ctx_id) {
  auto ctx = LookupContext(ctx_id);
  if (!ctx) return 0;
  ctx->ClearHttpCache(nullptr);
  return 1;
}

int32_t dc_context_flush_dns(const char* ctx_id) {
  auto ctx = LookupContext(ctx_id);
  if (!ctx) return 0;
  ctx->ClearHttpAuthCredentials(nullptr);
  ctx->CloseAllConnections(nullptr);
  return 1;
}

const char* dc_view_create(const char* ctx_id, void* host_hwnd,
                           int32_t width, int32_t height,
                           const dc_view_callbacks* callbacks) {
  if (!g_initialized || !callbacks) return nullptr;
  CefRefPtr<CefRequestContext> ctx = LookupContext(ctx_id);
  if (!ctx) ctx = CefRequestContext::GetGlobalContext();

  auto id = NewId("view-");
  CefRefPtr<DcClient> client = new DcClient(id, *callbacks, width, height);

  CefWindowInfo window_info;
#if defined(_WIN32)
  if (g_windowless) {
    window_info.SetAsWindowless(reinterpret_cast<HWND>(host_hwnd));
  } else {
    HWND interpose = CreateInterposeWindow(reinterpret_cast<HWND>(host_hwnd), width, height);
    g_view_hosts[id] = interpose;
    CefRect rect(0, 0, width > 0 ? width : 1280, height > 0 ? height : 800);
    window_info.SetAsChild(interpose, rect);
  }
#endif

  CefBrowserSettings bs;
  // Async create: CreateBrowserSync would block this (the message-loop) thread
  // while the render-process handshake needs that same loop pumped, so the
  // browser-info IPC times out and the frame never renders. OnAfterCreated
  // flushes any navigation requested in the meantime.
  if (!CefBrowserHost::CreateBrowser(window_info, client, CefString("about:blank"),
                                     bs, nullptr, ctx))
    return nullptr;

  auto res = g_views.emplace(std::move(id), client);
  return res.first->first.c_str();
}

void dc_view_navigate(const char* view_id, const char* url) {
  auto c = LookupView(view_id);
  if (!c || !url) return;
  if (auto b = c->browser())
    b->GetMainFrame()->LoadURL(CefString(url));
  else
    c->set_pending_url(url);   // browser still being created; flushed in OnAfterCreated
}
void dc_view_reload(const char* view_id, int32_t ignore_cache) {
  auto b = LookupBrowser(view_id);
  if (!b) return;
  ignore_cache ? b->ReloadIgnoreCache() : b->Reload();
}
void dc_view_stop(const char* view_id) {
  if (auto b = LookupBrowser(view_id)) b->StopLoad();
}
void dc_view_back(const char* view_id) {
  if (auto b = LookupBrowser(view_id)) b->GoBack();
}
void dc_view_forward(const char* view_id) {
  if (auto b = LookupBrowser(view_id)) b->GoForward();
}
int32_t dc_view_can_back(const char* view_id) {
  auto b = LookupBrowser(view_id);
  return b && b->CanGoBack() ? 1 : 0;
}
int32_t dc_view_can_forward(const char* view_id) {
  auto b = LookupBrowser(view_id);
  return b && b->CanGoForward() ? 1 : 0;
}
void dc_view_set_bounds(const char* view_id, int32_t x, int32_t y,
                        int32_t w, int32_t h) {
  auto c = LookupView(view_id);
  if (!c) return;
  c->set_size(w, h);
  auto b = c->browser();
#if defined(_WIN32)
  if (!g_windowless) {
    auto hit = g_view_hosts.find(view_id);
    if (hit != g_view_hosts.end() && hit->second) {
      // Move/size the interpose window; CEF fills it and reflows on WM_SIZE.
      ::SetWindowPos(hit->second, HWND_TOP, x, y, w > 0 ? w : 1,
                     h > 0 ? h : 1, SWP_NOACTIVATE | SWP_SHOWWINDOW);
      if (b) {
        if (HWND cef = b->GetHost()->GetWindowHandle())
          ::SetWindowPos(cef, nullptr, 0, 0, w > 0 ? w : 1, h > 0 ? h : 1,
                         SWP_NOZORDER | SWP_NOACTIVATE);
      }
    }
    return;  // Chrome-runtime windowed browsers resize via the native window.
  }
#endif
  if (b) b->GetHost()->WasResized();
}
void dc_view_set_visible(const char* view_id, int32_t visible) {
#if defined(_WIN32)
  if (!g_windowless) {
    auto hit = g_view_hosts.find(view_id);
    if (hit != g_view_hosts.end() && hit->second)
      ::ShowWindow(hit->second, visible ? SW_SHOW : SW_HIDE);
    return;  // WasHidden() is windowless-only under the Chrome runtime.
  }
#endif
  if (auto b = LookupBrowser(view_id)) {
    b->GetHost()->WasHidden(!visible);
    if (visible) b->GetHost()->Invalidate(PET_VIEW);   // repaint the tab we just switched to
  }
}
void dc_view_set_focus(const char* view_id, int32_t focused) {
  if (auto b = LookupBrowser(view_id)) b->GetHost()->SetFocus(focused != 0);
}
void dc_view_close(const char* view_id) {
  auto c = LookupView(view_id);
  if (!c) return;
  if (auto b = c->browser()) b->GetHost()->CloseBrowser(true);
#if defined(_WIN32)
  auto hit = g_view_hosts.find(view_id);
  if (hit != g_view_hosts.end()) {
    if (hit->second) ::DestroyWindow(hit->second);
    g_view_hosts.erase(hit);
  }
#endif
  g_views.erase(view_id);
}

// ---- windowless input ----
static CefMouseEvent MakeMouse(int x, int y) {
  CefMouseEvent e;
  e.x = x;
  e.y = y;
  e.modifiers = 0;
  return e;
}

void dc_view_osr_size(const char* view_id, int32_t width, int32_t height) {
  auto c = LookupView(view_id);
  if (!c) return;
  c->set_size(width, height);
  if (auto b = c->browser()) b->GetHost()->WasResized();
}

void dc_view_mouse_move(const char* view_id, int32_t x, int32_t y, int32_t leaving) {
  if (auto b = LookupBrowser(view_id))
    b->GetHost()->SendMouseMoveEvent(MakeMouse(x, y), leaving != 0);
}

void dc_view_mouse_button(const char* view_id, int32_t x, int32_t y,
                          int32_t button, int32_t down, int32_t click_count) {
  auto b = LookupBrowser(view_id);
  if (!b) return;
  cef_mouse_button_type_t t = button == 2 ? MBT_RIGHT : button == 1 ? MBT_MIDDLE : MBT_LEFT;
  b->GetHost()->SendMouseClickEvent(MakeMouse(x, y), t, down == 0,
                                    click_count > 0 ? click_count : 1);
}

void dc_view_mouse_wheel(const char* view_id, int32_t x, int32_t y,
                         int32_t delta_x, int32_t delta_y) {
  if (auto b = LookupBrowser(view_id))
    b->GetHost()->SendMouseWheelEvent(MakeMouse(x, y), delta_x, delta_y);
}

void dc_view_key(const char* view_id, int32_t is_down, int32_t windows_key_code,
                 int32_t native_key_code, uint32_t modifiers, uint16_t character) {
  auto b = LookupBrowser(view_id);
  if (!b) return;
  CefKeyEvent ke;
  ke.modifiers = modifiers;
  ke.windows_key_code = windows_key_code;
  ke.native_key_code = native_key_code;
  ke.is_system_key = false;
  if (character != 0) {
    ke.type = KEYEVENT_CHAR;
    ke.character = character;
    ke.unmodified_character = character;
    b->GetHost()->SendKeyEvent(ke);
    return;
  }
  ke.type = is_down ? KEYEVENT_RAWKEYDOWN : KEYEVENT_KEYUP;
  b->GetHost()->SendKeyEvent(ke);
}

int32_t dc_devtools_port(void) {
  if (!g_initialized || g_root_cache.empty()) return 0;
  std::string path = g_root_cache + "\\DevToolsActivePort";
  std::ifstream f(path);
  if (!f) return 0;
  int port = 0;
  f >> port;                 // first line is the port; second line is the ws path
  return port > 0 ? port : 0;
}

int32_t dc_view_eval_js(const char* view_id, const char* script,
                        dc_eval_cb cb, void* user) {
  auto c = LookupView(view_id);
  auto b = c ? c->browser() : nullptr;
  if (!b || !script || !cb) return 0;

  c->EnsureEvalObserver(cb, user);

  auto params = CefDictionaryValue::Create();
  params->SetString("expression", CefString(script));
  params->SetBool("returnByValue", true);
  params->SetBool("awaitPromise", true);
  return b->GetHost()->ExecuteDevToolsMethod(0, "Runtime.evaluate", params);
}

void dc_view_invalidate(const char* view_id) {
  if (auto b = LookupBrowser(view_id))
    b->GetHost()->Invalidate(PET_VIEW);
}

const char* dc_version(void) {
  if (g_version_str.empty())
    g_version_str = std::string("CEF ") + CEF_VERSION;
  return g_version_str.c_str();
}

}  // extern "C"
