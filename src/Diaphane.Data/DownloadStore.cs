using Microsoft.Data.Sqlite;

namespace Diaphane.Data;

public enum DownloadState { InProgress, Complete, Cancelled, Interrupted }

public sealed record DownloadEntry(
    long Id, string Url, string FileName, string FilePath,
    long ReceivedBytes, long TotalBytes, DownloadState State, DateTimeOffset StartedAt);

/// <summary>Local record of file downloads. Never written to by Sandbox tabs.</summary>
public sealed class DownloadStore : IDisposable
{
    private readonly SqliteConnection _db;

    public DownloadStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS downloads(
                id INTEGER PRIMARY KEY,
                url TEXT NOT NULL,
                file_name TEXT NOT NULL,
                file_path TEXT NOT NULL,
                received_bytes INTEGER NOT NULL DEFAULT 0,
                total_bytes INTEGER NOT NULL DEFAULT 0,
                state INTEGER NOT NULL,
                started_at INTEGER NOT NULL);
        """);
    }

    public long Start(string url, string fileName, string filePath, long totalBytes)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO downloads(url,file_name,file_path,received_bytes,total_bytes,state,started_at)
            VALUES($u,$f,$p,0,$tot,$st,$ts);
            SELECT last_insert_rowid();
        """;
        cmd.Parameters.AddWithValue("$u", url);
        cmd.Parameters.AddWithValue("$f", fileName);
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.Parameters.AddWithValue("$tot", totalBytes);
        cmd.Parameters.AddWithValue("$st", (int)DownloadState.InProgress);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void UpdateProgress(long id, string fileName, string filePath, long receivedBytes, long totalBytes, DownloadState state)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            UPDATE downloads SET file_name=$f, file_path=$p, received_bytes=$r, total_bytes=$tot, state=$st
            WHERE id=$id
        """;
        cmd.Parameters.AddWithValue("$f", fileName);
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.Parameters.AddWithValue("$r", receivedBytes);
        cmd.Parameters.AddWithValue("$tot", totalBytes);
        cmd.Parameters.AddWithValue("$st", (int)state);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Remove(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM downloads WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Clear() => Exec("DELETE FROM downloads");

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<DownloadEntry> Recent(int n = 200)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT id,url,file_name,file_path,received_bytes,total_bytes,state,started_at
            FROM downloads ORDER BY started_at DESC, id DESC LIMIT $n
        """;
        cmd.Parameters.AddWithValue("$n", n);
        var list = new List<DownloadEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DownloadEntry(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetInt64(4), r.GetInt64(5), (DownloadState)r.GetInt32(6),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(7))));
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
