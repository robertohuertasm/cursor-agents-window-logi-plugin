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

// One chat of a session: the main agent or one of its side chats. Id is the composerId
// used in open links. Side chats cannot be archived on their own from the Keypad: opening
// one selects its parent, so the archive shortcut would archive the parent instead.
public sealed record Chat(string Id, string Title, KeyState State, bool IsMain)
{
    public bool CanArchive => this.IsMain;
}

// A main Cursor agent with its side chats. State is the most urgent of its chats, so a
// session whose side chat waits or is unread is not shown as idle. Chats lists the main
// chat first, then the side chats, newest first.
public sealed record Session(
    string Id,
    string Title,
    string WorkspaceName,
    KeyState State,
    DateTimeOffset ModifiedAt,
    IReadOnlyList<Chat> Chats);

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
