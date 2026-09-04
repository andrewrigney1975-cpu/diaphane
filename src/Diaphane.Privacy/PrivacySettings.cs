using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diaphane.Privacy;

/// <summary>Persisted privacy preferences. Small, local, hand-serialised JSON — no sync.</summary>
public sealed class PrivacySettings
{
    /// <summary>What to wipe automatically when the last window closes. <see cref="ClearScope.None"/> disables it.</summary>
    public ClearScope ClearOnExit { get; set; } = ClearScope.None;

    /// <summary>Pre-ticked scopes when the "Clear browsing data" panel opens.</summary>
    public ClearScope DefaultClearScope { get; set; } =
        ClearScope.Cookies | ClearScope.SiteStorage | ClearScope.HttpCache | ClearScope.History;

    public ClearTimeRange DefaultTimeRange { get; set; } = ClearTimeRange.Everything;
}

/// <summary>Reads/writes <see cref="PrivacySettings"/> as a single JSON file. Never throws.</summary>
public sealed class PrivacySettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path => path;

    public PrivacySettings Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<PrivacySettings>(File.ReadAllText(path), Json) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public void Save(PrivacySettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
        }
        catch
        {
            // Best-effort: a browser that can't persist a preference still runs.
        }
    }
}
