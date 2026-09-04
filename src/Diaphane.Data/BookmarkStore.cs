using Microsoft.Data.Sqlite;

namespace Diaphane.Data;

public sealed record Bookmark(long Id, long? ParentId, bool IsFolder, string Title, string? Url, int Position);

/// <summary>Local bookmark tree. Netscape-HTML import/export planned (M5). No sync, ever.</summary>
public sealed class BookmarkStore : IDisposable
{
    private readonly SqliteConnection _db;

    public BookmarkStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS bookmarks(
                id INTEGER PRIMARY KEY,
                parent_id INTEGER REFERENCES bookmarks(id) ON DELETE CASCADE,
                is_folder INTEGER NOT NULL,
                title TEXT NOT NULL,
                url TEXT,
                position INTEGER NOT NULL DEFAULT 0);
        """);
    }

    public long Add(string title, string? url, long? parentId = null, bool isFolder = false)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bookmarks(parent_id,is_folder,title,url,position)
            VALUES($p,$f,$t,$u,
                   COALESCE((SELECT MAX(position)+1 FROM bookmarks WHERE parent_id IS $p),0));
            SELECT last_insert_rowid();
        """;
        cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", isFolder ? 1 : 0);
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$u", (object?)url ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public Bookmark? Get(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id,parent_id,is_folder,title,url,position FROM bookmarks WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Bookmark(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1),
            r.GetInt32(2) == 1, r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5));
    }

    /// <summary>Rename a group, or retitle/repoint a bookmark. Pass the existing value for whichever
    /// field isn't changing — <paramref name="url"/> is ignored for a group.</summary>
    public void Update(long id, string title, string? url)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE bookmarks SET title=$t, url=$u WHERE id=$id";
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$u", (object?)url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reparent a bookmark or group (drag-and-drop into a group, or to the root). Appends to the end.</summary>
    public void MoveTo(long id, long? newParentId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            UPDATE bookmarks SET parent_id=$p,
                position=COALESCE((SELECT MAX(position)+1 FROM bookmarks WHERE parent_id IS $p),0)
            WHERE id=$id;
        """;
        cmd.Parameters.AddWithValue("$p", (object?)newParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Remove(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; DELETE FROM bookmarks WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Bookmark> Children(long? parentId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id,parent_id,is_folder,title,url,position FROM bookmarks WHERE parent_id IS $p ORDER BY position";
        cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
        var list = new List<Bookmark>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Bookmark(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1),
                r.GetInt32(2) == 1, r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5)));
        return list;
    }

    public IReadOnlyList<Suggestion> Suggest(string typed, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(typed)) return Array.Empty<Suggestion>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT url,title FROM bookmarks WHERE is_folder=0 AND (url LIKE $q OR title LIKE $q) LIMIT $lim";
        cmd.Parameters.AddWithValue("$q", "%" + typed + "%");
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<Suggestion>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Suggestion(r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), 200, "bookmark"));
        return list;
    }

    private void Exec(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
