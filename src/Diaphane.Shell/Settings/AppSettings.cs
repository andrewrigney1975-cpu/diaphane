using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diaphane.Shell.Settings;

public enum StartupMode { Blank, Homepage, RestoreSession }
public enum AppTheme { System, Light, Dark }

/// <summary>User-facing preferences (diaphane://settings). Small, local JSON, no sync.</summary>
public sealed class AppSettings
{
    public StartupMode Startup { get; set; } = StartupMode.Blank;

    /// <summary>Used for the Home button and for <see cref="StartupMode.Homepage"/>.</summary>
    public string Homepage { get; set; } = "about:blank";

    /// <summary>Id of the default search engine (see <see cref="Omnibox.SearchEngines"/>).</summary>
    public string SearchEngineId { get; set; } = "duckduckgo";

    /// <summary>Query URL template for <c>SearchEngineId == "custom"</c>; <c>{q}</c> is the escaped query.</summary>
    public string CustomSearchUrl { get; set; } = "";

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool ShowBookmarksBar { get; set; } = true;

    /// <summary>
    /// Optional URL of a JSON update manifest ({"version": "...", "url": "..."}).
    /// Empty = the update check is disabled. Only ever fetched on an explicit
    /// "Check for updates" click — never in the background.
    /// </summary>
    public string UpdateManifestUrl { get; set; } = "";
}

/// <summary>Reads/writes <see cref="AppSettings"/> as one JSON file. Never throws.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path => path;

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
        }
        catch
        {
            // best effort
        }
    }
}
