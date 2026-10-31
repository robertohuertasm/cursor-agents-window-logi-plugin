namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// Opens the Agents window and presses its New Agent shortcut (Cmd+N): Cursor has no link
// that opens an empty agent. While Cursor is not running the key reads "Start Cursor" and
// a press only starts it; the shortcut is not sent until Cursor is frontmost.
public sealed class NewAgentCommand : PluginDynamicCommand
{
    private readonly CursorAgentsWindowPlugin owner;

    public NewAgentCommand(CursorAgentsWindowPlugin owner)
        : base("New agent", "Open a new agent in the Cursor Agents window, starting Cursor if needed", "Agents window", DeviceType.All)
    {
        this.owner = owner;
        this.Name = "cursoragents_new_agent";
    }

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

    protected override void RunCommand(string actionParameter)
    {
        if (!this.owner.Board.Online)
        {
            Opener.Open(Links.AgentsWindow);
            return;
        }
        _ = this.NewAsync();
    }

    private async Task NewAsync()
    {
        try
        {
            await this.owner.Actions.NewAgentAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            this.Log.Error($"could not open a new agent: {e.Message}");
        }
    }

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) => Faces.NewGlyph(this.owner.Board.Online, imageSize);

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => Faces.NewLabel(this.owner.Board.Online);
}
