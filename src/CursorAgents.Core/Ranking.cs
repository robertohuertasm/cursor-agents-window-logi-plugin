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

    // Every chat of every ranked session, for the Recent keys: a session with several
    // active chats takes several keys. Same priority as sessions; equal states keep their
    // session's order and then the session's own chat order (main chat first).
    public static IReadOnlyList<RankedChat> RankChats(IReadOnlyList<Session> rankedSessions) =>
        rankedSessions
            .SelectMany((s, si) => s.Chats.Select((c, ci) => (Entry: new RankedChat(s, c), si, ci)))
            .OrderBy(x => Status.Priority(x.Entry.Chat.State))
            .ThenBy(x => x.si)
            .ThenBy(x => x.ci)
            .Select(x => x.Entry)
            .ToList();
}

public sealed record RankedChat(Session Session, Chat Chat)
{
    public bool IsMain => this.Chat.IsMain;

    public string Title => this.Chat.Title;
}
