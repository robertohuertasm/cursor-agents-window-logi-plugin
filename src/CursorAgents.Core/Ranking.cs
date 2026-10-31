namespace CursorAgents;

// Orders sessions for the keys: active states first, then most recently modified.
// A session only moves when its state or modification time changes, never on focus,
// so a key does not reorder under the finger that pressed it.
public static class Ranking
{
    public static IReadOnlyList<Session> Rank(IEnumerable<Session> sessions) =>
        sessions
            .OrderBy(s => Status.Priority(s.State))
            .ThenByDescending(s => s.ModifiedAt)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    // Workspaces ordered by their most urgent session, then their most recent one; each
    // keeps its sessions in ranked order.
    public static IReadOnlyList<Workspace> Group(IReadOnlyList<Session> ranked) =>
        ranked
            .GroupBy(s => s.WorkspaceKey)
            .Select(g => new Workspace(g.Key, g.First().WorkspaceName, Status.MostUrgent(g.Select(s => s.State)), g.ToList()))
            .OrderBy(w => Status.Priority(w.State))
            .ThenByDescending(w => w.Sessions.Max(s => s.ModifiedAt))
            .ThenBy(w => w.Key, StringComparer.Ordinal)
            .ToList();
}
