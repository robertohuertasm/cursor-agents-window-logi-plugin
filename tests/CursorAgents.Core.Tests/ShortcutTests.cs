namespace CursorAgents.Core.Tests;

public class ShortcutTests
{
    [Fact]
    public void Uses_the_Cursor_Agents_window_bindings()
    {
        Assert.Equal(["⌘B", "⌥⌘B", "⌘G", "⌘J", "⇧⌘B", "⌘E", "⇧⌘F"], Shortcuts.All.Select(s => s.Glyph));
    }

    [Fact]
    public void Writes_Windows_shortcuts()
    {
        Assert.Equal(["Ctrl+B", "Ctrl+Alt+B", "Ctrl+G", "Ctrl+J", "Ctrl+Shift+B", "Ctrl+E", "Ctrl+Shift+F"], Shortcuts.All.Select(s => s.WindowsText));
    }

    [Fact]
    public void Ids_are_unique_and_keys_are_letters()
    {
        Assert.Equal(Shortcuts.All.Count, Shortcuts.All.Select(s => s.Id).Distinct().Count());
        Assert.All(Shortcuts.All, s => Assert.InRange(s.Key, 'A', 'Z'));
    }

    [Fact]
    public async Task Always_brings_up_the_Agents_window_then_sends()
    {
        var opened = 0;
        var sent = new List<string>();
        var sender = new ShortcutSender(() => true, () => opened++, s => sent.Add(s.Id));
        Assert.True(await sender.SendAsync(Shortcuts.All[0], CancellationToken.None));
        Assert.Equal(1, opened);
        Assert.Equal(["primary_sidebar"], sent);
    }

    [Fact]
    public async Task Waits_for_Cursor_to_come_to_the_front()
    {
        var front = false;
        var sent = new List<string>();
        var sender = new ShortcutSender(() => front, () => front = true, s => sent.Add(s.Id));
        Assert.True(await sender.SendAsync(Shortcuts.All[2], CancellationToken.None));
        Assert.Equal(["files"], sent);
    }

    [Fact]
    public async Task Does_not_send_when_Cursor_stays_behind()
    {
        var sent = new List<string>();
        var sender = new ShortcutSender(() => false, () => { }, s => sent.Add(s.Id));
        Assert.False(await sender.SendAsync(Shortcuts.All[0], CancellationToken.None));
        Assert.Empty(sent);
    }
}
