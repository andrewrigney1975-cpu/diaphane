using System.Runtime.InteropServices;

namespace Diaphane.Core;

/// <summary>P/Invoke surface for DiaphaneCore.dll (the flat C ABI in diaphane_core.h).</summary>
internal static partial class NativeMethods
{
    private const string Dll = "DiaphaneCore";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SchedulePumpCb(int delayMs, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void NavStateCb(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string viewId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        int isLoading, int canBack, int canFwd, double progress, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StringCb(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string viewId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void LoadEndCb(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string viewId, int httpStatus, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ViewLifecycleCb(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string viewId, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PaintCb(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string viewId, IntPtr bgra,
        int width, int height, int dx, int dy, int dw, int dh, IntPtr user);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ViewCallbacks
    {
        public NavStateCb OnNavState;
        public StringCb OnTitle;
        public StringCb OnFavicon;
        public LoadEndCb OnLoadEnd;
        public ViewLifecycleCb OnCreated;
        public ViewLifecycleCb OnClosed;
        public PaintCb OnPaint;
        public IntPtr User;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Settings
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? RootCacheDir;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? ResourcesDir;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? LocalesDir;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? SubprocessPath;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? UserAgent;
        public int Windowless;
        public int NoSandbox;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string? ExtensionDirs;
    }

    // Structs with string / delegate fields aren't supported by [LibraryImport] source-gen;
    // these two use the classic marshaller.
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int dc_initialize(in Settings settings, SchedulePumpCb pumpCb, IntPtr pumpUser);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr dc_view_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string ctxId, IntPtr hostHwnd, int width, int height,
        in ViewCallbacks callbacks);

    [LibraryImport(Dll)]
    internal static partial void dc_pump();

    [LibraryImport(Dll)]
    internal static partial void dc_shutdown();

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr dc_context_create(int persistent, string? cachePath, string? proxyUri);

    [LibraryImport(Dll)]
    internal static partial IntPtr dc_context_standard();

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_context_release(string ctxId);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dc_context_clear_cookies(string ctxId, long sinceUnixSeconds);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dc_context_clear_storage(string ctxId, long sinceUnixSeconds);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dc_context_clear_cache(string ctxId);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dc_context_flush_dns(string ctxId);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_navigate(string viewId, string url);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_reload(string viewId, int ignoreCache);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_stop(string viewId);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_back(string viewId);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_forward(string viewId);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dc_view_can_back(string viewId);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int dc_view_can_forward(string viewId);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_set_bounds(string viewId, int x, int y, int w, int h);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_set_visible(string viewId, int visible);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_set_focus(string viewId, int focused);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_close(string viewId);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_osr_size(string viewId, int width, int height);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_mouse_move(string viewId, int x, int y, int leaving);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_mouse_button(string viewId, int x, int y, int button, int down, int clickCount);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_mouse_wheel(string viewId, int x, int y, int deltaX, int deltaY);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_key(string viewId, int isDown, int windowsKeyCode, int nativeKeyCode, uint modifiers, ushort character);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void dc_view_invalidate(string viewId);

    [LibraryImport(Dll)]
    internal static partial IntPtr dc_version();

    internal static string PtrToUtf8(IntPtr p) => Marshal.PtrToStringUTF8(p) ?? string.Empty;
}
