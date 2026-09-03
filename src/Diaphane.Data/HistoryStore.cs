using Microsoft.Data.Sqlite;

namespace Diaphane.Data;

public sealed record VisitEntry(string Url, string Title, DateTimeOffset VisitedAt);
public sealed record Suggestion(string Url, string Title, double Score, string Source);

/// <summary>
/// Local, single-file visit history. Never written to by Sandbox tabs. Honours a
/// global "don't record" switch. Suggestions are ranked locally (frecency) so the
/// address bar never needs a network call.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private readonly SqliteConnection _db;
    public bool RecordingEnabled { get; set; } = true;

    public HistoryStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS visits(
                id INTEGER PRIMARY KEY,
                url TEXT NOT NULL,
                title TEXT NOT NULL DEFAULT '',
                visited_at INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_visits_url ON visits(url);
            CREATE INDEX IF NOT EXISTS ix_visits_time ON visits(visited_at);
        """);
    }

    public void RecordVisit(string url, string title)
    {
        if (!RecordingEnabled) return;
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO visits(url,title,visited_at) VALUES($u,$t,$ts)";
        cmd.Parameters.AddWithValue("$u", url);
        cmd.Parameters.AddWithValue("$t", title ?? "");
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    /// <summary>Frecency: visit count weighted by recency buckets, matched on prefix/substring.</summary>
    public IReadOnlyList<Suggestion> Suggest(string typed, int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(typed)) return Array.Empty<Suggestion>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT url, MAX(title) AS title, COUNT(*) AS n,
                   SUM(
                     CASE
                       WHEN $now - visited_at <    86400 THEN 100
                       WHEN $now - visited_at <   604800 THEN 70
                       WHEN $now - visited_at <  2592000 THEN 30
                       ELSE 10
                     END) AS score
            FROM visits
            WHERE url LIKE $q OR title LIKE $q
            GROUP BY url
            ORDER BY score DESC
            LIMIT $lim;
        """;
        cmd.Parameters.AddWithValue("$q", "%" + typed + "%");
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$lim", limit);

        var list = new List<Suggestion>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Suggestion(r.GetString(0), r.GetString(1), r.GetDouble(3), "history"));
        return list;
    }

    /// <summary>Most-recent distinct-URL visits, newest first.</summary>
    public IReadOnlyList<VisitEntry> Recent(int limit = 200)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT url, MAX(title) AS title, MAX(visited_at) AS ts
            FROM visits GROUP BY url ORDER BY ts DESC LIMIT $lim;
        """;
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<VisitEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VisitEntry(r.GetString(0), r.GetString(1),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(2))));
        return list;
    }

    public void Clear(DateTimeOffset? since = null)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = since is null
            ? "DELETE FROM visits"
            : "DELETE FROM visits WHERE visited_at >= $s";
        if (since is { } s) cmd.Parameters.AddWithValue("$s", s.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
        Exec("VACUUM");
    }

    private void Exec(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
