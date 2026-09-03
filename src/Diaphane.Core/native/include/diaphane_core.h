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

// ---- diagnostics ----
DC_API const char* dc_version(void);   // "CEF x.y.z / Chromium a.b.c.d"

#ifdef __cplusplus
}  // extern "C"
#endif

#endif  // DIAPHANE_CORE_H_
