namespace CursorAgentsWindow.Probes;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public enum InferredState
{
    Idle,
    Unread,
    Working,
    Waiting,
}

public sealed record SessionSnapshot(
    string Id,
    string Workspace,
    long LastUpdatedAt,
    bool Unread,
    bool BlockingPending,
    string? Status,
    int Generating,
    string? LastTool,
    string? LastToolStatus,
    string? LastToolDecision,
    InferredState State)
{
    // Raw fields only, so the log shows how Cursor writes state without any message content.
    public string Raw => $"status={Status ?? "-"} unread={Unread} blocking={BlockingPending} generating={Generating} tool={LastTool ?? "-"}:{LastToolStatus ?? "-"}:{LastToolDecision ?? "-"}";
}

public sealed record ReadResult(IReadOnlyList<SessionSnapshot> Sessions, string? HeadersVersion, long CopyMs, long QueryMs, bool Skipped);

/// <summary>
/// Reads Cursor's global state.vscdb from a private copy. Never opens Cursor's own files with SQLite
/// and never reads message text, prompts, or account keys.
/// </summary>
public sealed class CursorDbReader : IDisposable
{
    private static readonly HashSet<string> QuestionTools = ["ask_question", "create_plan"];

    private readonly string sourceDir;
    private readonly string copyDir;
    private readonly int maxSessions;
    private DateTime lastDbWrite;
    private DateTime lastWalWrite;
    private long lastWalLength = -1;
    private ReadResult? last;

    public CursorDbReader(string? sourceDir = null, int maxSessions = 9)
    {
        this.sourceDir = sourceDir ?? DefaultGlobalStorage();
        this.copyDir = Path.Combine(Path.GetTempPath(), $"cursor-db-probe-{Environment.ProcessId}");
        this.maxSessions = maxSessions;
        Directory.CreateDirectory(this.copyDir);
    }

    public string SourceDir => this.sourceDir;

    public static string DefaultGlobalStorage() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Cursor", "User", "globalStorage");

    public ReadResult Read()
    {
        var db = Path.Combine(this.sourceDir, "state.vscdb");
        var wal = db + "-wal";
        var dbWrite = File.GetLastWriteTimeUtc(db);
        var walInfo = new FileInfo(wal);
        var walWrite = walInfo.Exists ? walInfo.LastWriteTimeUtc : DateTime.MinValue;
        var walLength = walInfo.Exists ? walInfo.Length : -1;
        if (this.last is not null && dbWrite == this.lastDbWrite && walWrite == this.lastWalWrite && walLength == this.lastWalLength)
        {
            return this.last with { CopyMs = 0, QueryMs = 0, Skipped = true };
        }

        var sw = Stopwatch.StartNew();
        var copy = Path.Combine(this.copyDir, "state.vscdb");
        File.Copy(db, copy, overwrite: true);
        if (walInfo.Exists)
        {
            File.Copy(wal, copy + "-wal", overwrite: true);
        }
        else
        {
            File.Delete(copy + "-wal");
        }

        // The -shm index is rebuilt from the WAL; copying Cursor's live one could mismatch the WAL copy.
        File.Delete(copy + "-shm");
        var copyMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var (sessions, version) = this.Query(copy);
        var result = new ReadResult(sessions, version, copyMs, sw.ElapsedMilliseconds, false);
        this.lastDbWrite = dbWrite;
        this.lastWalWrite = walWrite;
        this.lastWalLength = walLength;
        this.last = result;
        return result;
    }

    private (List<SessionSnapshot> Sessions, string? Version) Query(string path)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        conn.Open();

        string? version;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT CAST(value AS TEXT) FROM ItemTable WHERE key = 'composer.composerHeaders.version'";
            version = cmd.ExecuteScalar() as string;
        }

        var heads = new List<(string Id, long Updated, string Json)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT composerId, COALESCE(lastUpdatedAt, createdAt), value FROM composerHeaders
                WHERE isArchived = 0 AND isSubagent = 0
                ORDER BY COALESCE(lastUpdatedAt, createdAt) DESC LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$n", this.maxSessions * 2);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                heads.Add((r.GetString(0), r.GetInt64(1), r.GetString(2)));
            }
        }

        var list = new List<SessionSnapshot>();
        foreach (var (id, updated, json) in heads)
        {
            using var header = JsonDocument.Parse(json);
            var h = header.RootElement;
            if (Bool(h, "isDraft"))
            {
                continue;
            }

            var workspace = h.TryGetProperty("workspaceIdentifier", out var wi) && wi.TryGetProperty("uri", out var u) && u.TryGetProperty("path", out var p)
                ? Path.GetFileName(p.GetString()?.TrimEnd('/') ?? "")
                : "";

            string? status = null;
            var generating = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT json_extract(value, '$.status'), json_array_length(json_extract(value, '$.generatingBubbleIds'))
                    FROM cursorDiskKV WHERE key = $k
                    """;
                cmd.Parameters.AddWithValue("$k", "composerData:" + id);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    status = r.IsDBNull(0) ? null : r.GetString(0);
                    generating = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                }
            }

            string? tool = null, toolStatus = null, decision = null;
            using (var cmd = conn.CreateCommand())
            {
                // Range scan on the unique key index; only tool-call metadata is extracted.
                cmd.CommandText = """
                    SELECT json_extract(value, '$.toolFormerData.name'), json_extract(value, '$.toolFormerData.status'), json_extract(value, '$.toolFormerData.userDecision')
                    FROM cursorDiskKV
                    WHERE key >= $lo AND key < $hi AND json_extract(value, '$.toolFormerData.name') IS NOT NULL
                    ORDER BY json_extract(value, '$.createdAt') DESC LIMIT 1
                    """;
                cmd.Parameters.AddWithValue("$lo", $"bubbleId:{id}:");
                cmd.Parameters.AddWithValue("$hi", $"bubbleId:{id};");
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    tool = r.IsDBNull(0) ? null : r.GetString(0);
                    toolStatus = r.IsDBNull(1) ? null : r.GetString(1);
                    decision = r.IsDBNull(2) ? null : r.GetString(2);
                }
            }

            var unread = Bool(h, "hasUnreadMessages");
            var blocking = Bool(h, "hasBlockingPendingActions");
            list.Add(new SessionSnapshot(id, workspace, updated, unread, blocking, status, generating, tool, toolStatus, decision,
                Infer(status, generating, unread, blocking, tool, toolStatus, decision)));
            if (list.Count == this.maxSessions)
            {
                break;
            }
        }

        return (list, version);
    }

    // Rules from labs/projects/agent-monitoring/research/cursor-agent-mode.md (hypotheses, 2026-10-31).
    public static InferredState Infer(string? status, int generating, bool unread, bool blocking, string? tool, string? toolStatus, string? decision)
    {
        var question = tool is not null && QuestionTools.Contains(tool);
        if (blocking || (question && (toolStatus == "loading" || decision is null)))
        {
            return InferredState.Waiting;
        }

        if (generating > 0 || toolStatus == "loading" || (status is not null && status != "completed" && status != "none"))
        {
            return InferredState.Working;
        }

        return unread ? InferredState.Unread : InferredState.Idle;
    }

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.copyDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
