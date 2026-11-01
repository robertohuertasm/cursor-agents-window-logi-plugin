namespace CursorAgents;

[Flags]
public enum Mods
{
    None = 0,
    Command = 1,
    Option = 2,
    Control = 4,
    Shift = 8,
}

// A Cursor Agents window keybinding sent from a key. Key is a letter A–Z. Mods are the macOS
// modifiers; ForWindows gives the Windows binding (Cmd → Ctrl; Option is Alt there).
// Icon names a codicon embedded in the plugin.
public sealed record ViewShortcut(string Id, string Label, char Key, Mods Mods, string Icon)
{
    public Mods ForWindows => Mods.HasFlag(Mods.Command)
        ? (Mods & ~Mods.Command) | Mods.Control
        : Mods;

    // As macOS menus write it: ⌃⌥⇧⌘ then the key.
    public string Glyph =>
        (Mods.HasFlag(Mods.Control) ? "⌃" : "")
        + (Mods.HasFlag(Mods.Option) ? "⌥" : "")
        + (Mods.HasFlag(Mods.Shift) ? "⇧" : "")
        + (Mods.HasFlag(Mods.Command) ? "⌘" : "")
        + char.ToUpperInvariant(Key);

    // As Windows menus write it: Ctrl+Alt+Shift+B.
    public string WindowsText =>
        string.Join("+", new[]
        {
            ForWindows.HasFlag(Mods.Control) ? "Ctrl" : null,
            ForWindows.HasFlag(Mods.Option) ? "Alt" : null,
            ForWindows.HasFlag(Mods.Shift) ? "Shift" : null,
            char.ToUpperInvariant(Key).ToString(),
        }.Where(part => part is not null));

    public string Text(bool windows) => windows ? WindowsText : Glyph;
}

// Bindings as Cursor 3.27 registers them in its Agents window (workbench.glass.main.js),
// checked on 2026-11-01: Files and Changes differ from VS Code (⌘G and ⌘E), and Right
// Sidebar is Cursor's "Open Apps Panel" (the right-hand panel). Cursor binds no separate
// macOS keys for these, so on Windows Cmd becomes Ctrl.
public static class Shortcuts
{
    // Ids name the actions in profiles; do not rename them.
    public static readonly IReadOnlyList<ViewShortcut> All =
    [
        new("primary_sidebar", "Sidebar", 'B', Mods.Command, "layout-sidebar-left"),
        new("secondary_sidebar", "Right Sidebar", 'B', Mods.Command | Mods.Option, "layout-sidebar-right"),
        new("files", "Files", 'G', Mods.Command, "files"),
        new("terminal", "Terminal", 'J', Mods.Command, "terminal"),
        new("browser", "Browser", 'B', Mods.Command | Mods.Shift, "globe"),
        new("changes", "Changes", 'E', Mods.Command, "diff"),
        new("search", "Search", 'F', Mods.Command | Mods.Shift, "search"),
    ];
}

// Keys go to the frontmost app. Cursor's IDE window and its Agents window share one app,
// so being frontmost is not enough: the Agents window is always brought up first (its link
// is a no-op when it is already in front), and the shortcut is only sent once Cursor is frontmost.
public sealed class ShortcutSender(Func<bool> cursorFrontmost, Action openAgentsWindow, Action<ViewShortcut> send)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    public async Task<bool> SendAsync(ViewShortcut shortcut, CancellationToken cancel)
    {
        openAgentsWindow();
        var deadline = DateTime.UtcNow + Timeout;
        while (!cursorFrontmost())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Log.Warning($"Cursor did not come to the front in time; not sending {shortcut.Id}");
                return false;
            }
            await Task.Delay(PollStep, cancel);
        }
        await Task.Delay(Settle, cancel);
        send(shortcut);
        return true;
    }
}
