using System.Reflection;
using System.Runtime.InteropServices;

namespace Diaphane.Core;

/// <summary>
/// Points the P/Invoke loader at the folder that holds DiaphaneCore.dll + libcef.dll
/// + the CEF resources. Call <see cref="Use"/> once before touching <see cref="CefEngine"/>.
/// </summary>
public static class NativeLoader
{
    private static string? _dir;

    /// <summary>Directory containing DiaphaneCore.dll, libcef.dll, diaphane_helper.exe, *.pak, icudtl.dat.</summary>
    public static void Use(string cefBinDir)
    {
        _dir = Path.GetFullPath(cefBinDir);
        NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);
    }

    public static string BinDir =>
        _dir ?? throw new InvalidOperationException("NativeLoader.Use(cefBinDir) was not called.");

    public static string SubprocessPath => Path.Combine(BinDir, "diaphane_helper.exe");
    public static string ResourcesDir => BinDir;                     // *.pak + icudtl.dat staged flat
    public static string LocalesDir => Path.Combine(BinDir, "locales");

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (_dir is null) return IntPtr.Zero;
        var candidate = Path.Combine(_dir, libraryName.EndsWith(".dll") ? libraryName : libraryName + ".dll");
        return File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var h) ? h : IntPtr.Zero;
    }
}
