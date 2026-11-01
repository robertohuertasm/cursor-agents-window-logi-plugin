namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// "Recent chat N": the Nth chat across all sessions by priority, dragged onto a key in
// Options+. The name stays cursoragents_recent_N, as in 0.1.0 when keys were agents,
// because profiles refer to keys by it.
//
// A short press opens the chat. A long press on a main chat asks "Archive?" and a short
// press within a few seconds archives it, which also hides its side chats. A side chat
// cannot be archived from the Keypad yet: opening it selects its parent, so the archive
// shortcut would archive the parent. A long press on a side chat does nothing.
public sealed class RecentChatCommand : PluginDynamicCommand
{
    public const int SlotCount = 9;
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly CursorAgentsWindowPlugin owner;
    private readonly int slot;
    private bool longPressHandled;
    private RankedChat? armed;
    private CancellationTokenSource? armTimer;

    public RecentChatCommand(CursorAgentsWindowPlugin owner, int slot)
        : base($"Recent chat {slot}", $"Status of the {Ordinal(slot)} chat in the Cursor Agents window, active ones first; press to open it, hold to archive it", "Recent chats", DeviceType.All)
    {
        this.owner = owner;
        this.slot = slot;
        this.Name = $"cursoragents_recent_{slot}";
    }

    private SessionBoard Board => this.owner.Board;

    private RankedChat? Current => this.armed ?? this.Board.ChatAt(this.slot - 1);

    public void Redraw()
    {
        try
        {
            this.ActionImageChanged();
        }
        catch (Exception e)
        {
            this.Log.Error(e, $"could not redraw '{this.Name}'");
        }
    }

    protected override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press:
                this.longPressHandled = false;
                return true;
            case DeviceButtonEventType.LongPress:
                this.longPressHandled = true;
                // Side chats cannot be archived from the Keypad yet, so a long press on one does nothing.
                if (this.armed is null && this.Board.ChatAt(this.slot - 1) is { Chat.CanArchive: true } held)
                {
                    this.Arm(held);
                }
                return true;
            case DeviceButtonEventType.RepeatPress:
                return true;
            case DeviceButtonEventType.Release:
                if (!this.longPressHandled)
                {
                    this.Tap();
                }
                this.longPressHandled = false;
                return true;
            default:
                return false;
        }
    }

    protected override void RunCommand(string actionParameter) => this.Tap();

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (this.armed is not null) return Faces.Glyph("?", imageSize, new BitmapColor(200, 30, 30));
        if (!this.Board.Online) return Faces.Ring(KeyState.Offline, imageSize);
        return Faces.Ring(this.Current?.Chat.State ?? KeyState.None, imageSize);
    }

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (this.armed is not null) return "Archive?";
        return KeyFace.Label(this.Board.Online ? this.Current?.Title : "offline");
    }

    private void Tap()
    {
        if (this.armed is { } target)
        {
            this.Disarm();
            _ = this.ArchiveAsync(target);
        }
        else if (this.Board.ChatAt(this.slot - 1) is { } chat)
        {
            this.owner.OpenChat(chat.Session.Id, chat.Chat.Id);
        }
    }

    private async Task ArchiveAsync(RankedChat target)
    {
        try
        {
            if (await this.owner.Actions.ArchiveAsync(target.Session.Id, CancellationToken.None))
            {
                this.Log.Info("archived a session from a Recent chat key");
            }
        }
        catch (Exception e)
        {
            this.Log.Error($"could not archive from a Recent chat key: {e.Message}");
        }
    }

    private void Arm(RankedChat target)
    {
        this.armTimer?.Cancel();
        this.armed = target;
        var timer = this.armTimer = new CancellationTokenSource();
        this.Redraw();
        _ = Task.Delay(ConfirmWindow, timer.Token).ContinueWith(
            _ => { if (ReferenceEquals(this.armed, target)) this.Disarm(); },
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void Disarm()
    {
        this.armTimer?.Cancel();
        this.armTimer = null;
        if (this.armed is null) return;
        this.armed = null;
        this.Redraw();
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th",
    };
}
