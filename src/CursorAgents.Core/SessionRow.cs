namespace CursorAgents;

// The fields the plugin reads for one composer, all metadata. Times are Unix milliseconds.
// ParentId is set for side chats (subagentInfo.parentComposerId) and null for main agents.
public sealed record SessionRow(
    string Id,
    string? Name,
    string? WorkspacePath,
    long LastUpdatedAt,
    bool IsDraft,
    bool Unread,
    bool BlockingPending,
    string? Status,
    int Generating,
    string? LastTool,
    string? LastToolStatus,
    long LastActivityAt,
    string? ParentId = null);

public sealed record StoreSnapshot(IReadOnlyList<SessionRow> Rows, string? SelectedAgent);
