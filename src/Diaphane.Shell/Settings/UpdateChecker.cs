using System.Text.Json;

namespace Diaphane.Shell.Settings;

public sealed record UpdateInfo(string LatestVersion, string DownloadUrl, bool IsNewer);

/// <summary>
/// Compares the running version against a JSON manifest
/// (<c>{"version": "1.2.3", "url": "https://…"}</c>). Only ever runs when the
/// user clicks "Check for updates" — there is no background poll, and nothing
/// is downloaded or installed automatically.
/// </summary>
public sealed class UpdateChecker(string currentVersion, Func<string, Task<string>> fetch)
{
    public async Task<UpdateInfo?> CheckAsync(string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl)) return null;

        var json = await fetch(manifestUrl);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var latest = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        if (latest.Length == 0) return null;

        return new UpdateInfo(latest, url, IsNewer(latest, currentVersion));
    }

    public static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate, out var c) && Version.TryParse(current, out var cur) && c > cur;
}
