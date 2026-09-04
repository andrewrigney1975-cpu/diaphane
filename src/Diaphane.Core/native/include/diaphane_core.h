// diaphane_core.h — flat C ABI over libcef, consumed from C# via P/Invoke.
// This is the ONLY surface between the managed shell and CEF. Everything here is
// extern "C", uses primitives + UTF-8 char*, and marshals CEF callbacks to
// caller-supplied function pointers. All strings are UTF-8, owned by the callee
// for the duration of the callback only (copy if you need to keep them).
#ifndef DIAPHANE_CORE_H_
#define DIAPHANE_CORE_H_

#include <stdint.h>

#if defined(_WIN32)
#  if defined(DIAPHANE_CORE_IMPL)
#    define DC_API __declspec(dllexport)
#  else
#    define DC_API __declspec(dllimport)
#  endif
#else
#  define DC_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ---- callbacks (all invoked on the CEF UI thread == the thread that calls dc_pump) ----
typedef void (*dc_schedule_pump_cb)(int32_t delay_ms, void* user);
typedef void (*dc_nav_state_cb)(const char* view_id, const char* url, const char* title,
                                int32_t is_loading, int32_t can_back, int32_t can_fwd,
                                double progress, void* user);
typedef void (*dc_title_cb)(const char* view_id, const char* title, void* user);
typedef void (*dc_favicon_cb)(const char* view_id, const char* icon_url, void* user);
typedef void (*dc_load_end_cb)(const char* view_id, int32_t http_status_code, void* user);
typedef void (*dc_created_cb)(const char* view_id, void* user);
typedef void (*dc_closed_cb)(const char* view_id, void* user);
// Windowless (OSR) paint: |bgra| is width*height*4 BGRA32, top-down, valid only
// for the duration of the call. |dirty_*| bounds the changed region.
typedef void (*dc_paint_cb)(const char* view_id, const void* bgra,
                            int32_t width, int32_t height,
                            int32_t dirty_x, int32_t dirty_y,
                            int32_t dirty_w, int32_t dirty_h, void* user);

typedef struct dc_view_callbacks {
  dc_nav_state_cb on_nav_state;
  dc_title_cb     on_title;
  dc_favicon_cb   on_favicon;
  dc_load_end_cb  on_load_end;
  dc_created_cb   on_created;
  dc_closed_cb    on_closed;
  dc_paint_cb     on_paint;      // windowless only
  void*           user;
} dc_view_callbacks;

typedef struct dc_settings {
  const char* root_cache_dir;    // parent dir for persistent contexts
  const char* resources_dir;     // dir with *.pak, icudtl.dat
  const char* locales_dir;       // dir with locale .pak files
  const char* subprocess_path;   // path to diaphane_helper.exe
  const char* user_agent;        // optional; null = CEF default
  int32_t     windowless;        // 1 = off-screen rendering (headless), 0 = windowed
  int32_t     no_sandbox;        // 1 = disable the CEF sandbox (tests only)
  const char* extension_dirs;    // optional; ';'-separated unpacked-extension dirs to --load-extension
  int32_t     allow_widevine;    // 1 = permit the Widevine CDM if present; 0 (default) hardens it off
  int32_t     devtools;          // 1 = enable the loopback DevTools endpoint (--remote-debugging-port=0)
} dc_settings;

// ---- lifecycle ----
// Returns 1 on success. On failure returns 0 and the process should treat CEF as unusable.
DC_API int32_t dc_initialize(const dc_settings* settings,
                             dc_schedule_pump_cb pump_cb, void* pump_user);
DC_API void    dc_pump(void);        // one slice of CefDoMessageLoopWork()
DC_API void    dc_shutdown(void);

// ---- request contexts (storage partitions) ----
// Returns an opaque, null-terminated id string owned by the library (stable until
// dc_release_context). persistent=0 + cache_path=null => fully in-memory (Sandbox).
DC_API const char* dc_context_create(int32_t persistent, const char* cache_path,
                                     const char* proxy_uri);
DC_API const char* dc_context_standard(void);   // the shared on-disk context
DC_API void        dc_context_release(const char* ctx_id);
DC_API int32_t     dc_context_clear_cookies(const char* ctx_id, int64_t since_unix_seconds);
DC_API int32_t     dc_context_clear_storage(const char* ctx_id, int64_t since_unix_seconds);
DC_API int32_t     dc_context_clear_cache(const char* ctx_id);
DC_API int32_t     dc_context_flush_dns(const char* ctx_id);

// ---- browser views ----
// host_hwnd may be 0 for windowless. Returns an opaque view id (stable until dc_view_close).
DC_API const char* dc_view_create(const char* ctx_id, void* host_hwnd,
                                  int32_t width, int32_t height,
                                  const dc_view_callbacks* callbacks);
DC_API void    dc_view_navigate(const char* view_id, const char* url);
DC_API void    dc_view_reload(const char* view_id, int32_t ignore_cache);
DC_API void    dc_view_stop(const char* view_id);
DC_API void    dc_view_back(const char* view_id);
DC_API void    dc_view_forward(const char* view_id);
DC_API int32_t dc_view_can_back(const char* view_id);
DC_API int32_t dc_view_can_forward(const char* view_id);
DC_API void    dc_view_set_bounds(const char* view_id, int32_t x, int32_t y, int32_t w, int32_t h);
DC_API void    dc_view_set_visible(const char* view_id, int32_t visible);
DC_API void    dc_view_set_focus(const char* view_id, int32_t focused);
DC_API void    dc_view_close(const char* view_id);

// ---- windowless input (OSR) ----
// button: 0=left 1=middle 2=right ; type for mouse_button: 1=down 0=up
DC_API void dc_view_osr_size(const char* view_id, int32_t width, int32_t height);
DC_API void dc_view_mouse_move(const char* view_id, int32_t x, int32_t y, int32_t leaving);
DC_API void dc_view_mouse_button(const char* view_id, int32_t x, int32_t y,
                                 int32_t button, int32_t down, int32_t click_count);
DC_API void dc_view_mouse_wheel(const char* view_id, int32_t x, int32_t y,
                                int32_t delta_x, int32_t delta_y);
DC_API void dc_view_key(const char* view_id, int32_t is_down, int32_t windows_key_code,
                        int32_t native_key_code, uint32_t modifiers, uint16_t character);
// Force a full repaint (OSR) — call when a hidden tab becomes visible again.
DC_API void dc_view_invalidate(const char* view_id);

// ---- devtools ----
// The port the loopback DevTools HTTP endpoint bound to (from
// <cache>/DevToolsActivePort), or 0 if remote debugging is off / not ready yet.
DC_API int32_t dc_devtools_port(void);

// ---- script evaluation (via the DevTools protocol Runtime.evaluate) ----
// The result JSON (the CDP "result" object, or an error) is delivered to |cb|
// tagged with the returned request id. Returns 0 on failure.
typedef void (*dc_eval_cb)(const char* view_id, int32_t request_id,
                           int32_t ok, const char* result_json, void* user);
DC_API int32_t dc_view_eval_js(const char* view_id, const char* script,
                               dc_eval_cb cb, void* user);

// ---- downloads ----
// state: 0=in-progress 1=complete 2=cancelled 3=interrupted. |download_id| is CEF's own id,
// stable for the life of the download. Fired on every progress update (including the final one).
typedef void (*dc_download_cb)(const char* view_id, int64_t download_id, const char* url,
                               const char* file_name, const char* file_path,
                               int64_t received_bytes, int64_t total_bytes, int32_t state, void* user);
// Registers (or, passed null, clears) the download callback for one view. Separate from
// dc_view_callbacks so a build predating download support just lacks this export — never
// grow dc_view_callbacks itself for this, that would silently break the struct's ABI for
// callers built against an older diaphane_core.h.
DC_API void dc_view_set_download_cb(const char* view_id, dc_download_cb cb, void* user);
// Overrides where new downloads land; null/empty restores the engine's own default
// (typically the OS Downloads folder). Applies to downloads started after the call.
DC_API void dc_set_default_download_dir(const char* dir);

// ---- popups ----
// Fired when the page tries to open a new window/tab (target="_blank", window.open(), a
// middle-click, etc). The native side always cancels the popup itself — there's no second
// top-level window to host it in — so the shell is expected to open |target_url| in one of
// its own tabs. Same additive-registration pattern as dc_view_set_download_cb.
typedef void (*dc_popup_cb)(const char* view_id, const char* target_url, void* user);
DC_API void dc_view_set_popup_cb(const char* view_id, dc_popup_cb cb, void* user);

// ---- context menu ----
// Fired on right-click; the native side always suppresses CEF's own context menu (no
// native chrome to host it in — same reasoning as OnFileDialog/OnBeforePopup), so the
// shell is expected to show its own and act on the result via dc_view_start_download.
// kind: 0=none (plain right-click) 1=link 2=image 3=video 4=audio.
// |link_url| is set whenever the click was on/inside a hyperlink (independent of kind);
// |src_url| is the image/video/audio resource url for kind 2-4.
typedef void (*dc_context_menu_cb)(const char* view_id, int32_t kind,
                                   const char* link_url, const char* src_url,
                                   int32_t x, int32_t y, void* user);
DC_API void dc_view_set_context_menu_cb(const char* view_id, dc_context_menu_cb cb, void* user);

// Explicitly start a download of |url| (e.g. "Save link/image/video as…") — goes through
// the same OnBeforeDownload/dc_view_set_download_cb path as a page-initiated download.
DC_API void dc_view_start_download(const char* view_id, const char* url);

// Same as dc_view_start_download, but pins the exact destination path — e.g. from a
// real WinUI FileSavePicker the shell showed for "Save link/image/video as…" — instead
// of the default-download-dir + suggested-name path dc_view_start_download computes.
DC_API void dc_view_start_download_to(const char* view_id, const char* url, const char* save_path);

// ---- diagnostics ----
DC_API const char* dc_version(void);   // "CEF x.y.z / Chromium a.b.c.d"

#ifdef __cplusplus
}  // extern "C"
#endif

#endif  // DIAPHANE_CORE_H_
