using Microsoft.Data.Sqlite;

namespace CursorAgents;

public interface ISessionStore
{
    // Throws when Cursor's database cannot be read.
    StoreSnapshot Read();

    // A fresh read of the Agents window's selected agent, bypassing the change check.
    string? ReadSelectedAgent();
}

// Reads Cursor's global state.vscdb from a private copy, never Cursor's own files, and
// only metadata: no message text, prompts, or account keys. The copy is refreshed only
// when the database or its WAL changed.
public sealed class CursorStore : ISessionStore, IDisposable
{
    private const int MaxSessions = 120;
    private const int CopyAttempts = 4;
    private static readonly TimeSpan CopyRetryDelay = TimeSpan.FromMilliseconds(150);
    private readonly string sourceDir;
    private readonly string copyDir;
    private readonly object gate = new();
    private (DateTime Db, DateTime Wal, long WalLength) lastStamp;
    private StoreSnapshot? last;

    public CursorStore(string? sourceDir = null, string? copyDir = null)
    {
        this.sourceDir = sourceDir ?? DefaultGlobalStorage();
        this.copyDir = copyDir ?? Path.Combine(Path.GetTempPath(), $"cursor-agents-window-{Environment.ProcessId}");
    }

    public string SourceDir => this.sourceDir;

    public static string DefaultGlobalStorage() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Cursor", "User", "globalStorage");

    public StoreSnapshot Read()
    {
        lock (this.gate)
        {
            var stamp = this.Stamp();
            if (this.last is not null && stamp == this.lastStamp) return this.last;
            var copy = this.Copy();
            using var conn = Open(copy);
            var snapshot = new StoreSnapshot(QueryRows(conn, DateTimeOffset.UtcNow), QuerySelected(conn));
            this.lastStamp = stamp;
            this.last = snapshot;
            return snapshot;
        }
    }

    public string? ReadSelectedAgent()
    {
        lock (this.gate)
        {
            var copy = this.Copy();
            using var conn = Open(copy);
            return QuerySelected(conn);
        }
    }

    private (DateTime, DateTime, long) Stamp()
    {
        var db = new FileInfo(Path.Combine(this.sourceDir, "state.vscdb"));
        if (!db.Exists) throw new FileNotFoundException("Cursor's state database is missing", db.FullName);
        var wal = new FileInfo(db.FullName + "-wal");
        return (db.LastWriteTimeUtc, wal.Exists ? wal.LastWriteTimeUtc : DateTime.MinValue, wal.Exists ? wal.Length : -1);
    }

    private string Copy()
    {
        Directory.CreateDirectory(this.copyDir);
        var source = Path.Combine(this.sourceDir, "state.vscdb");
        var copy = Path.Combine(this.copyDir, "state.vscdb");
        // Cursor may hold its files briefly while it writes; a short retry avoids a false "offline".
        Retry(() =>
        {
            File.Copy(source, copy, overwrite: true);
            if (File.Exists(source + "-wal")) File.Copy(source + "-wal", copy + "-wal", overwrite: true);
            else File.Delete(copy + "-wal");
        });
        // SQLite rebuilds the shared-memory index from the WAL; Cursor's live one could
        // describe a different WAL than the one copied.
        File.Delete(copy + "-shm");
        return copy;
    }

    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < CopyAttempts)
            {
                Thread.Sleep(CopyRetryDelay);
            }
        }
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        return conn;
    }

    private static string? QuerySelected(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CAST(value AS TEXT) FROM ItemTable WHERE key = 'cursor/glass.selectedAgent'";
        return cmd.ExecuteScalar() as string;
    }

    private static List<SessionRow> QueryRows(SqliteConnection conn, DateTimeOffset now)
    {
        var rows = new List<SessionRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.composerId,
                   json_extract(h.value, '$.name'),
                   json_extract(h.value, '$.workspaceIdentifier.uri.path'),
                   json_extract(h.value, '$.workspaceIdentifier.uri.fsPath'),
                   COALESCE(h.lastUpdatedAt, h.createdAt, 0),
                   json_extract(h.value, '$.isDraft'),
                   json_extract(h.value, '$.hasUnreadMessages'),
                   json_extract(h.value, '$.hasBlockingPendingActions'),
                   json_extract(d.value, '$.status'),
                   json_array_length(json_extract(d.value, '$.generatingBubbleIds')),
                   MAX(COALESCE(json_extract(d.value, '$.lastUpdatedAt'), 0),
                       COALESCE(json_extract(h.value, '$.conversationCheckpointLastUpdatedAt'), 0),
                       COALESCE(h.lastUpdatedAt, h.createdAt, 0)),
                   json_extract(h.value, '$.subagentInfo.parentComposerId')
            FROM composerHeaders h
            LEFT JOIN cursorDiskKV d ON d.key = 'composerData:' || h.composerId
            WHERE h.isArchived = 0
              AND (h.isSubagent = 0 OR (h.subagentTypeName = 'side-chat' AND json_extract(h.value, '$.subagentInfo.parentComposerId') IS NOT NULL))
            ORDER BY COALESCE(h.lastUpdatedAt, h.createdAt) DESC
            LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$n", MaxSessions);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            var status = Text(r, 8);
            var blocking = Flag(r, 7);
            var updated = r.GetInt64(4);
            var activity = r.GetInt64(10);
            string? tool = null, toolStatus = null, decision = null;
            if (StateRules.NeedsActivityScan(status, blocking, updated, now))
            {
                (tool, toolStatus, decision, var bubbleActivity) = QueryBubbles(conn, id);
                activity = Math.Max(activity, bubbleActivity);
            }

            rows.Add(new SessionRow(
                id,
                Text(r, 1),
                Text(r, 2) ?? Text(r, 3),
                updated,
                Flag(r, 5),
                Flag(r, 6),
                blocking,
                status,
                r.IsDBNull(9) ? 0 : r.GetInt32(9),
                tool,
                toolStatus,
                activity,
                Text(r, 11),
                decision));
        }
        return rows;
    }

    // The newest tool call and the newest message write. Keys share the composerId prefix,
    // so this is a range scan on the key index.
    private static (string? Tool, string? Status, string? Decision, long Activity) QueryBubbles(SqliteConnection conn, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT json_extract(value, '$.toolFormerData.name') || char(31) || COALESCE(json_extract(value, '$.toolFormerData.status'), '')
                           || char(31) || COALESCE(json_extract(value, '$.toolFormerData.userDecision'), '')
                    FROM cursorDiskKV
                    WHERE key >= $lo AND key < $hi AND json_extract(value, '$.toolFormerData.name') IS NOT NULL
                    ORDER BY json_extract(value, '$.createdAt') DESC LIMIT 1),
                   (SELECT MAX(MAX(COALESCE(json_extract(value, '$.startedAtMs'), 0), COALESCE(json_extract(value, '$.completedAtMs'), 0)))
                    FROM cursorDiskKV WHERE key >= $lo AND key < $hi)
            """;
        cmd.Parameters.AddWithValue("$lo", $"bubbleId:{id}:");
        cmd.Parameters.AddWithValue("$hi", $"bubbleId:{id};");
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null, null, 0);
        var activity = r.IsDBNull(1) ? 0 : r.GetInt64(1);
        if (r.IsDBNull(0)) return (null, null, null, activity);
        var parts = r.GetString(0).Split('\u001f');
        string? Part(int i) => parts.Length > i && parts[i].Length > 0 ? parts[i] : null;
        return (parts[0], Part(1), Part(2), activity);
    }

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();

    // json_extract returns 1/0 for JSON booleans.
    private static bool Flag(SqliteDataReader r, int i) => !r.IsDBNull(i) && Convert.ToInt64(r.GetValue(i)) != 0;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.copyDir)) Directory.Delete(this.copyDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
