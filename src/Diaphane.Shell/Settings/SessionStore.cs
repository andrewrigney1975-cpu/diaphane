using System.Text.Json;

namespace Diaphane.Shell.Settings;

public sealed record SavedSession(IReadOnlyList<string> Urls, int ActiveIndex);

/// <summary>
/// Remembers the standard tabs' URLs between runs for
/// <see cref="StartupMode.RestoreSession"/>. Sandbox tabs are never written.
/// </summary>
public sealed class SessionStore(string path)
{
    public SavedSession Load()
    {
        try
        {
            if (!File.Exists(path)) return new(Array.Empty<string>(), 0);
            var s = JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(path));
            return s is null ? new(Array.Empty<string>(), 0)
                             : s with { Urls = s.Urls.Where(IsRestorable).ToArray() };
        }
        catch
        {
            return new(Array.Empty<string>(), 0);
        }
    }

    public void Save(SavedSession session)
    {
        try
        {
            var urls = session.Urls.Where(IsRestorable).ToArray();
            if (urls.Length == 0) { Clear(); return; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(
                new SavedSession(urls, Math.Clamp(session.ActiveIndex, 0, urls.Length - 1))));
        }
        catch
        {
            // best effort
        }
    }

    public void Clear()
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool IsRestorable(string url) =>
        !string.IsNullOrWhiteSpace(url)
        && !url.StartsWith("about:")
        && !url.StartsWith("data:")
        && !url.StartsWith("diaphane://")
        && !url.StartsWith("chrome://");
}
