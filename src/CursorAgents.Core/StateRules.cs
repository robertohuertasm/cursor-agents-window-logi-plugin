namespace CursorAgents;

// Turns Cursor's stored fields into a key state. Cursor has no single "running" field, so
// these rules come from sampling the database while agents ran, waited, and were stopped
// (agent-skills labs/projects/agent-monitoring/research/cursor-agent-mode.md, 2026-10-31).
public static class StateRules
{
    // Tools that stop the turn until the user answers a card.
    private static readonly HashSet<string> QuestionTools = ["ask_question", "create_plan"];

    // While an agent runs, its messages are rewritten every few seconds. Cursor writes
    // status "aborted" both while running and after the user presses stop, so an active
    // status only counts as Working while messages keep changing.
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromSeconds(30);

    // A single tool call (a long terminal command) can run for minutes without writes.
    public static readonly TimeSpan ToolWindow = TimeSpan.FromMinutes(10);

    public static KeyState Infer(SessionRow row, DateTimeOffset now)
    {
        var toolLoading = row.LastToolStatus == "loading";
        // Cursor marks a question "completed" as soon as the card shows, before the user
        // answers; only userDecision tells that it was answered (seen 2026-11-01).
        var question = row.LastTool is { } tool && QuestionTools.Contains(tool);
        if (row.BlockingPending || (question && (toolLoading || row.LastToolDecision is null)))
        {
            return KeyState.InputNeeded;
        }

        var sinceActivity = now - DateTimeOffset.FromUnixTimeMilliseconds(row.LastActivityAt);
        var activeStatus = row.Status is not null and not "completed" and not "none";
        if ((row.Generating > 0 || activeStatus) && sinceActivity <= ActivityWindow)
        {
            return KeyState.Working;
        }

        if (toolLoading && sinceActivity <= ToolWindow)
        {
            return KeyState.Working;
        }

        return row.Unread ? KeyState.Unread : KeyState.Idle;
    }

    // Drafts and placeholders ("empty-state-draft", "draft-…") are not sessions; nor are
    // composers that never got a name or a run.
    public static bool IsVisible(SessionRow row) =>
        !row.IsDraft
        && Guid.TryParse(row.Id, out _)
        && (!string.IsNullOrWhiteSpace(row.Name) || row.Status is not (null or "none"));

    // Only recent or unfinished sessions can be working or waiting, so only those are
    // worth scanning messages for.
    public static bool NeedsActivityScan(string? status, bool blocking, long lastUpdatedAt, DateTimeOffset now) =>
        blocking
        || status is not (null or "completed" or "none")
        || now - DateTimeOffset.FromUnixTimeMilliseconds(lastUpdatedAt) <= ToolWindow;

    // Groups main agents with their side chats. Side chats whose parent is archived or not
    // visible are dropped, as Cursor hides them too, even though their own isArchived is 0.
    public static IReadOnlyList<Session> BuildSessions(IEnumerable<SessionRow> rows, DateTimeOffset now)
    {
        var visible = rows.Where(IsVisible).ToList();
        var sides = visible
            .Where(r => r.ParentId is not null)
            .GroupBy(r => r.ParentId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.LastUpdatedAt).ThenBy(r => r.Id, StringComparer.Ordinal).ToList());
        var sessions = new List<Session>();
        foreach (var main in visible.Where(r => r.ParentId is null))
        {
            var folder = FolderName(main.WorkspacePath);
            var title = string.IsNullOrWhiteSpace(main.Name) ? folder : main.Name.Trim();
            var chats = new List<Chat> { new(main.Id, title, Infer(main, now), IsMain: true) };
            var updated = main.LastUpdatedAt;
            if (sides.TryGetValue(main.Id, out var children))
            {
                foreach (var side in children)
                {
                    chats.Add(new Chat(side.Id, string.IsNullOrWhiteSpace(side.Name) ? "Side chat" : side.Name.Trim(), Infer(side, now), IsMain: false));
                    updated = Math.Max(updated, side.LastUpdatedAt);
                }
            }
            sessions.Add(new Session(main.Id, title, folder, Status.MostUrgent(chats.Select(c => c.State)), DateTimeOffset.FromUnixTimeMilliseconds(updated), chats));
        }
        return sessions;
    }

    private static string FolderName(string? workspacePath)
    {
        var path = workspacePath?.TrimEnd('/', '\\');
        return string.IsNullOrEmpty(path) ? "No folder" : Path.GetFileName(path.Replace('\\', '/'));
    }
}
