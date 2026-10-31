namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// "Recent agent N": the Nth agent across all folders by priority, dragged onto a key in
// Options+. Profiles refer to keys by Name, so it must not change.
//
// A short press opens the agent. A long press asks "Archive?" and a short press within a
// few seconds confirms. While waiting, the key stays on the agent it is about to archive
// even if the ranking changes.
public sealed class RecentAgentCommand : PluginDynamicCommand
{
    public const int SlotCount = 9;
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly CursorAgentsWindowPlugin owner;
    private readonly int slot;
    private bool longPressHandled;
    private Session? armed;
    private CancellationTokenSource? armTimer;

    public RecentAgentCommand(CursorAgentsWindowPlugin owner, int slot)
        : base($"Recent agent {slot}", $"Status of the {Ordinal(slot)} agent in the Cursor Agents window, active ones first; press to open it, hold to archive it", "Recent agents", DeviceType.All)
    {
        this.owner = owner;
        this.slot = slot;
        this.Name = $"cursoragents_recent_{slot}";
    }

    private SessionBoard Board => this.owner.Board;

    private Session? Current => this.armed ?? this.Board.At(this.slot - 1);

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
                if (this.armed is null && this.Board.At(this.slot - 1) is { } held)
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
        return Faces.Ring(this.Current?.State ?? KeyState.None, imageSize);
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
        else if (this.Board.At(this.slot - 1) is { } session)
        {
            Opener.Open(Links.OpenAgent(session.Id));
        }
    }

    private async Task ArchiveAsync(Session target)
    {
        try
        {
            if (await this.owner.Actions.ArchiveAsync(target.Id, CancellationToken.None))
            {
                this.Log.Info("archived an agent from a Recent agent key");
            }
        }
        catch (Exception e)
        {
            this.Log.Error($"could not archive from a Recent agent key: {e.Message}");
        }
    }

    private void Arm(Session target)
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
