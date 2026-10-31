namespace CursorAgents;

// What a key shows.
public enum KeyState
{
    None,
    Offline,
    InputNeeded,
    Working,
    // Finished but not looked at since: Cursor's hasUnreadMessages.
    Unread,
    Idle,
}

// One Cursor agent ("composer"). Id is the composerId used in open links.
public sealed record Session(
    string Id,
    string Title,
    string WorkspaceKey,
    string WorkspaceName,
    KeyState State,
    DateTimeOffset ModifiedAt);

// The sessions of one folder, as the Agents window groups them.
public sealed record Workspace(string Key, string Name, KeyState State, IReadOnlyList<Session> Sessions);

public static class Status
{
    public static KeyState MostUrgent(IEnumerable<KeyState> states) =>
        states.DefaultIfEmpty(KeyState.None).OrderBy(Priority).First();

    // Lower ranks first: anything that needs the user beats anything that does not.
    // Unread (finished, not looked at) needs the user; Working does not yet.
    public static int Priority(KeyState state) => state switch
    {
        KeyState.InputNeeded => 0,
        KeyState.Unread => 1,
        KeyState.Working => 2,
        KeyState.Idle => 3,
        _ => 4,
    };
}
