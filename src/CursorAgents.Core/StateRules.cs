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
        if (row.BlockingPending || (toolLoading && row.LastTool is { } tool && QuestionTools.Contains(tool)))
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

    public static Session ToSession(SessionRow row, DateTimeOffset now)
    {
        var path = row.WorkspacePath?.TrimEnd('/', '\\');
        var name = string.IsNullOrEmpty(path) ? "No folder" : Path.GetFileName(path.Replace('\\', '/'));
        return new Session(
            row.Id,
            string.IsNullOrWhiteSpace(row.Name) ? name : row.Name.Trim(),
            path ?? "",
            name,
            Infer(row, now),
            DateTimeOffset.FromUnixTimeMilliseconds(row.LastUpdatedAt));
    }
}
