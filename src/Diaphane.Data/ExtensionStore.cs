using Microsoft.Data.Sqlite;

namespace Diaphane.Data;

/// <summary>An unpacked extension the user has loaded, as remembered on disk.</summary>
public sealed record InstalledExtension(
    string Id,
    string Path,
    string Name,
    string Version,
    bool Enabled,
    IReadOnlyList<string> Permissions,
    DateTimeOffset AddedAt);

/// <summary>
/// Local record of loaded unpacked extensions. Just paths + metadata + an
/// enabled flag — the engine loads the enabled ones at startup via
/// <c>--load-extension</c>. No store, no sync, no update pings.
/// </summary>
public sealed class ExtensionStore : IDisposable
{
    private readonly SqliteConnection _db;

    public ExtensionStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS extensions(
                id          TEXT PRIMARY KEY,
                path        TEXT NOT NULL,
                name        TEXT NOT NULL,
                version     TEXT NOT NULL,
                enabled     INTEGER NOT NULL DEFAULT 1,
                permissions TEXT NOT NULL DEFAULT '',
                added_at    INTEGER NOT NULL);
        """);
    }

    public void Upsert(InstalledExtension e)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO extensions(id, path, name, version, enabled, permissions, added_at)
            VALUES($id, $path, $name, $ver, $en, $perm, $ts)
            ON CONFLICT(id) DO UPDATE SET
                path=$path, name=$name, version=$ver, permissions=$perm;
        """;
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.Parameters.AddWithValue("$path", e.Path);
        cmd.Parameters.AddWithValue("$name", e.Name);
        cmd.Parameters.AddWithValue("$ver", e.Version);
        cmd.Parameters.AddWithValue("$en", e.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$perm", string.Join('\n', e.Permissions));
        cmd.Parameters.AddWithValue("$ts", e.AddedAt.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    public void SetEnabled(string id, bool enabled)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE extensions SET enabled=$en WHERE id=$id";
        cmd.Parameters.AddWithValue("$en", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Remove(string id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM extensions WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<InstalledExtension> All()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT id, path, name, version, enabled, permissions, added_at
            FROM extensions ORDER BY added_at;
        """;
        var list = new List<InstalledExtension>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new InstalledExtension(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetInt64(4) != 0,
                r.GetString(5) is { Length: > 0 } p ? p.Split('\n') : Array.Empty<string>(),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6))));
        return list;
    }

    public IReadOnlyList<string> EnabledPaths() =>
        All().Where(e => e.Enabled).Select(e => e.Path).ToList();

    private void Exec(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
