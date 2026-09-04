using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Diaphane.Shell.Extensions;

/// <summary>
/// The parts of an unpacked extension's <c>manifest.json</c> diaphane cares about,
/// plus a stable id derived from the folder path (same scheme Chromium uses for
/// unpacked extensions, so ids line up with what <c>--load-extension</c> assigns).
/// </summary>
public sealed record ExtensionManifest(
    string Id,
    string Path,
    string Name,
    string Version,
    IReadOnlyList<string> Permissions,
    int ManifestVersion,
    string? UpdateUrl)
{
    /// <summary>Read <paramref name="dir"/>/manifest.json. Throws on a missing/invalid manifest.</summary>
    public static ExtensionManifest FromDirectory(string dir)
    {
        var full = System.IO.Path.GetFullPath(dir);
        var manifestPath = System.IO.Path.Combine(full, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"No manifest.json in {full}");

        using var doc = JsonDocument.Parse(StripJsonComments(File.ReadAllText(manifestPath)));
        var root = doc.RootElement;

        string Str(string prop, string fallback = "") =>
            root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;

        var name = Str("name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("manifest.json has no \"name\"");

        var perms = new List<string>();
        foreach (var key in new[] { "permissions", "host_permissions", "optional_permissions" })
            if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var p in arr.EnumerateArray())
                    if (p.ValueKind == JsonValueKind.String)
                        perms.Add(p.GetString()!);

        string? updateUrl = null;
        if (root.TryGetProperty("update_url", out var u) && u.ValueKind == JsonValueKind.String)
            updateUrl = u.GetString();

        var mv = root.TryGetProperty("manifest_version", out var mvEl) && mvEl.TryGetInt32(out var i) ? i : 2;

        return new ExtensionManifest(
            Id: IdForPath(full),
            Path: full,
            Name: name,
            Version: Str("version", "0"),
            Permissions: perms,
            ManifestVersion: mv,
            UpdateUrl: updateUrl);
    }

    /// <summary>
    /// Chromium's unpacked-extension id: SHA-256 of the absolute path, first 16
    /// bytes, each nibble mapped 0-15 → 'a'-'p'.
    /// </summary>
    public static string IdForPath(string absolutePath)
    {
        // Chromium hashes the path as UTF-16LE on Windows.
        var bytes = Encoding.Unicode.GetBytes(absolutePath);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(32);
        for (int i = 0; i < 16; i++)
        {
            sb.Append((char)('a' + (hash[i] >> 4)));
            sb.Append((char)('a' + (hash[i] & 0x0f)));
        }
        return sb.ToString();
    }

    // Chrome tolerates // and /* */ comments in manifest.json; System.Text.Json does not.
    private static string StripJsonComments(string json)
    {
        var opts = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), opts);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject: w.WriteStartObject(); break;
                    case JsonTokenType.EndObject: w.WriteEndObject(); break;
                    case JsonTokenType.StartArray: w.WriteStartArray(); break;
                    case JsonTokenType.EndArray: w.WriteEndArray(); break;
                    case JsonTokenType.PropertyName: w.WritePropertyName(reader.GetString()!); break;
                    case JsonTokenType.String: w.WriteStringValue(reader.GetString()); break;
                    case JsonTokenType.Number: w.WriteRawValue(Encoding.UTF8.GetString(reader.ValueSpan), skipInputValidation: true); break;
                    case JsonTokenType.True: w.WriteBooleanValue(true); break;
                    case JsonTokenType.False: w.WriteBooleanValue(false); break;
                    case JsonTokenType.Null: w.WriteNullValue(); break;
                }
            }
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
