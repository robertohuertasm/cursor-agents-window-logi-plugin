using CursorAgents;
using Microsoft.Data.Sqlite;

namespace CursorAgents.Core.Tests;

public class StateRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    private static SessionRow Row(
        string? status = "completed",
        bool unread = false,
        bool blocking = false,
        int generating = 0,
        string? tool = null,
        string? toolStatus = null,
        TimeSpan? sinceActivity = null,
        string? id = null,
        string? name = "Fix the build",
        bool draft = false,
        string? path = "/Users/me/src/app",
        string? parent = null,
        long updatedOffsetMs = 0) =>
        new(id ?? Guid.NewGuid().ToString(), name, path, Now.ToUnixTimeMilliseconds() + updatedOffsetMs, draft, unread, blocking, status, generating,
            tool, toolStatus, (Now - (sinceActivity ?? TimeSpan.Zero)).ToUnixTimeMilliseconds(), parent);

    [Fact]
    public void Blocking_approval_needs_input() =>
        Assert.Equal(KeyState.InputNeeded, StateRules.Infer(Row(status: "aborted", blocking: true, tool: "run_terminal_command_v2", toolStatus: "loading"), Now));

    [Fact]
    public void Open_question_card_needs_input() =>
        Assert.Equal(KeyState.InputNeeded, StateRules.Infer(Row(status: "completed", tool: "ask_question", toolStatus: "loading"), Now));

    [Fact]
    public void Answered_question_does_not_need_input() =>
        Assert.Equal(KeyState.Idle, StateRules.Infer(Row(tool: "ask_question", toolStatus: "completed"), Now));

    [Fact]
    public void Active_status_with_recent_writes_is_working() =>
        Assert.Equal(KeyState.Working, StateRules.Infer(Row(status: "aborted", sinceActivity: TimeSpan.FromSeconds(3)), Now));

    // Cursor leaves "aborted" behind when the user presses stop.
    [Fact]
    public void Active_status_without_writes_is_not_working() =>
        Assert.Equal(KeyState.Idle, StateRules.Infer(Row(status: "aborted", sinceActivity: TimeSpan.FromMinutes(2)), Now));

    [Fact]
    public void Long_running_tool_stays_working() =>
        Assert.Equal(KeyState.Working, StateRules.Infer(Row(status: "aborted", tool: "run_terminal_command_v2", toolStatus: "loading", sinceActivity: TimeSpan.FromMinutes(3)), Now));

    [Fact]
    public void Finished_unread_is_unread() =>
        Assert.Equal(KeyState.Unread, StateRules.Infer(Row(unread: true), Now));

    [Fact]
    public void Drafts_and_placeholders_are_hidden()
    {
        Assert.False(StateRules.IsVisible(Row(draft: true)));
        Assert.False(StateRules.IsVisible(Row(id: "empty-state-draft", draft: false)));
        Assert.False(StateRules.IsVisible(Row(status: "none", name: null)));
        Assert.True(StateRules.IsVisible(Row(status: "none", name: "Plan")));
    }

    [Fact]
    public void Session_takes_folder_name_and_default_title()
    {
        var s = StateRules.BuildSessions([Row(name: "  ", path: "/Users/me/src/app/")], Now).Single();
        Assert.Equal("app", s.WorkspaceName);
        Assert.Equal("app", s.Title);
        Assert.Equal("No folder", StateRules.BuildSessions([Row(path: null)], Now).Single().WorkspaceName);
    }

    [Fact]
    public void Side_chats_group_under_their_parent_and_color_it()
    {
        var main = Guid.NewGuid().ToString();
        var older = Row(name: "older side", parent: main, updatedOffsetMs: -1000);
        var waiting = Row(name: "waiting side", parent: main, blocking: true);
        var sessions = StateRules.BuildSessions([Row(id: main, name: "main"), older, waiting], Now);

        var s = Assert.Single(sessions);
        Assert.Equal(["main", "waiting side", "older side"], s.Chats.Select(c => c.Title));
        Assert.True(s.Chats[0].IsMain);
        Assert.False(s.Chats[1].CanArchive);
        Assert.Equal(KeyState.InputNeeded, s.State);
    }

    // Cursor hides side chats of an archived parent; the store does not return archived
    // parents, so their side chats have no parent row and are dropped.
    [Fact]
    public void Side_chats_without_a_visible_parent_are_hidden()
    {
        var sessions = StateRules.BuildSessions([Row(name: "orphan", parent: Guid.NewGuid().ToString(), unread: true)], Now);
        Assert.Empty(sessions);
    }
}

public class RankingTests
{
    private static Session S(string id, KeyState state, int minutesAgo, params Chat[] sides) =>
        new(id, id, "a", state, DateTimeOffset.UnixEpoch.AddDays(1).AddMinutes(-minutesAgo),
            [new Chat(id, id, state, IsMain: true), .. sides]);

    [Fact]
    public void Needs_the_user_first_then_recent()
    {
        var ranked = Ranking.Rank([S("idle-new", KeyState.Idle, 0), S("working", KeyState.Working, 5), S("unread", KeyState.Unread, 9), S("input", KeyState.InputNeeded, 30), S("idle-old", KeyState.Idle, 60)]);
        Assert.Equal(["input", "unread", "working", "idle-new", "idle-old"], ranked.Select(s => s.Id));
    }

    [Fact]
    public void A_waiting_side_chat_takes_a_key_before_idle_main_chats()
    {
        var ranked = Ranking.Rank([S("a", KeyState.Idle, 0), S("b", KeyState.Idle, 10, new Chat("b-side", "side", KeyState.InputNeeded, IsMain: false))]);
        var chats = Ranking.RankChats(ranked);
        Assert.Equal(["b-side", "a", "b"], chats.Select(c => c.Chat.Id));
        Assert.Equal("b", chats[0].Session.Id);
    }
}

public class LinksTests
{
    [Fact]
    public void Agent_link_escapes_id() =>
        Assert.Equal("cursor://anysphere.cursor-deeplink/agent?id=a%2Fb", Links.OpenAgent("a/b"));
}

public class KeyFaceTests
{
    [Fact]
    public void Label_truncates_by_code_point()
    {
        Assert.Equal(KeyFace.Blank, KeyFace.Label(" "));
        Assert.Equal("short", KeyFace.Label("short"));
        Assert.Equal("🚀abcdefghij…", KeyFace.Label("🚀abcdefghijklmno"));
    }
}

public class BoardTests
{
    private sealed class FakeStore(Func<StoreSnapshot> read) : ISessionStore
    {
        public StoreSnapshot Read() => read();

        public string? ReadSelectedAgent() => null;
    }

    [Fact]
    public void Working_expires_without_new_writes()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var id = Guid.NewGuid().ToString();
        var row = new SessionRow(id, "Run", "/a", now.ToUnixTimeMilliseconds(), false, false, false, "aborted", 0, null, null, now.ToUnixTimeMilliseconds());
        var clock = now;
        var board = new SessionBoard(new FakeStore(() => new([row], null)), () => true, () => clock);

        Assert.True(board.Apply(new([row], null)));
        Assert.Equal(KeyState.Working, board.Ranked[0].State);
        Assert.False(board.Apply(new([row], null)));

        clock = now + StateRules.ActivityWindow + TimeSpan.FromSeconds(1);
        Assert.True(board.Apply(new([row], null)));
        Assert.Equal(KeyState.Idle, board.Ranked[0].State);
    }

    [Fact]
    public void One_failed_read_keeps_last_state_then_goes_offline()
    {
        Assert.Equal(SessionBoard.FailureAction.KeepLast, SessionBoard.OnReadFailure(failures: 1, wasOnline: true));
        Assert.Equal(SessionBoard.FailureAction.KeepLast, SessionBoard.OnReadFailure(failures: 2, wasOnline: true));
        Assert.Equal(SessionBoard.FailureAction.Offline, SessionBoard.OnReadFailure(failures: 3, wasOnline: true));
        Assert.Equal(SessionBoard.FailureAction.Offline, SessionBoard.OnReadFailure(failures: 1, wasOnline: false));
    }

    [Fact]
    public void Offline_clears_keys()
    {
        var board = new SessionBoard(new FakeStore(() => throw new IOException()), () => false);
        Assert.True(board.Apply(null));
        Assert.False(board.Online);
        Assert.Empty(board.Ranked);
    }
}

// Builds a database with Cursor's table layout and reads it through a copy, as the plugin does.
public sealed class CursorStoreTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("cursor-store-test").FullName;

    [Fact]
    public void Reads_headers_state_and_newest_tool()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var waiting = Guid.NewGuid().ToString();
        var done = Guid.NewGuid().ToString();
        var archived = Guid.NewGuid().ToString();
        var side = Guid.NewGuid().ToString();
        var otherSubagent = Guid.NewGuid().ToString();
        var source = Path.Combine(this.dir, "src");
        Directory.CreateDirectory(source);
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(source, "state.vscdb")};Pooling=False"))
        {
            conn.Open();
            Exec(conn, """
                CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);
                CREATE TABLE cursorDiskKV (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB);
                CREATE TABLE composerHeaders (composerId TEXT PRIMARY KEY, workspaceId TEXT, createdAt INTEGER, lastUpdatedAt INTEGER, isArchived INTEGER, isSubagent INTEGER, recency INTEGER, checkpointAt INTEGER, value TEXT, subagentTypeName TEXT);
                """);
            Header(conn, waiting, now, 0, """{"name":"Deploy","hasBlockingPendingActions":true,"workspaceIdentifier":{"uri":{"path":"/w/app"}}}""");
            Header(conn, done, now - 1000, 0, """{"name":"Done","hasUnreadMessages":true,"workspaceIdentifier":{"uri":{"path":"/w/lib"}}}""");
            Header(conn, archived, now, 1, """{"name":"Old"}""");
            Header(conn, side, now, 0, $$$"""{"name":"Side","subagentInfo":{"parentComposerId":"{{{waiting}}}"}}""", subagent: true);
            Header(conn, otherSubagent, now, 0, """{"name":"Task"}""", subagent: true, type: "task");
            Kv(conn, $"composerData:{waiting}", $$$"""{"status":"aborted","generatingBubbleIds":[],"lastUpdatedAt":{{{now}}}}""");
            Kv(conn, $"composerData:{done}", """{"status":"completed"}""");
            Kv(conn, $"bubbleId:{waiting}:1", """{"createdAt":"2026-01-01T00:00:00Z","toolFormerData":{"name":"read_file_v2","status":"completed"}}""");
            Kv(conn, $"bubbleId:{waiting}:2", $$$"""{"createdAt":"2026-01-01T00:00:05Z","startedAtMs":{{{now}}},"toolFormerData":{"name":"run_terminal_command_v2","status":"loading"}}""");
            Kv(conn, "cursor/glass.selectedAgent", waiting, table: "ItemTable");
        }

        using var store = new CursorStore(source, Path.Combine(this.dir, "copy"));
        var snapshot = store.Read();

        Assert.Equal(waiting, snapshot.SelectedAgent);
        Assert.Equal(waiting, store.ReadSelectedAgent());
        Assert.Equal(3, snapshot.Rows.Count);
        Assert.Equal(waiting, snapshot.Rows.Single(r => r.Id == side).ParentId);
        Assert.DoesNotContain(snapshot.Rows, r => r.Id == otherSubagent);
        var w = snapshot.Rows.Single(r => r.Id == waiting);
        Assert.True(w.BlockingPending);
        Assert.Equal("run_terminal_command_v2", w.LastTool);
        Assert.Equal("loading", w.LastToolStatus);
        Assert.Equal("/w/app", w.WorkspacePath);
        var d = snapshot.Rows.Single(r => r.Id == done);
        Assert.True(d.Unread);
        Assert.Equal("completed", d.Status);
        Assert.Same(snapshot, store.Read());
    }

    private static void Header(SqliteConnection conn, string id, long updated, int archived, string json, bool subagent = false, string type = "side-chat")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO composerHeaders (composerId, createdAt, lastUpdatedAt, isArchived, isSubagent, subagentTypeName, value) VALUES ($id, $t, $t, $a, $s, $type, $v)";
        cmd.Parameters.AddWithValue("$s", subagent ? 1 : 0);
        cmd.Parameters.AddWithValue("$type", subagent ? type : DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$t", updated);
        cmd.Parameters.AddWithValue("$a", archived);
        cmd.Parameters.AddWithValue("$v", json);
        cmd.ExecuteNonQuery();
    }

    private static void Kv(SqliteConnection conn, string key, string value, string table = "cursorDiskKV")
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} (key, value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => Directory.Delete(this.dir, recursive: true);
}
