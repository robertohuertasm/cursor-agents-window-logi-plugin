namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// One key per Cursor Agents window keybinding (sidebars, files, terminal, browser, changes, search).
// Brings Cursor to the front first, since keys go to the frontmost app.
public sealed class ShortcutCommand : PluginDynamicCommand
{
    private readonly CursorAgentsWindowPlugin owner;
    private readonly ViewShortcut shortcut;

    public ShortcutCommand(CursorAgentsWindowPlugin owner, ViewShortcut shortcut)
        : base(shortcut.Label, $"Press {shortcut.Text(OperatingSystem.IsWindows())} in the Cursor Agents window", "Shortcuts", DeviceType.All)
    {
        this.owner = owner;
        this.shortcut = shortcut;
        this.Name = $"cursoragents_shortcut_{shortcut.Id}";
    }

    protected override void RunCommand(string actionParameter) => _ = this.SendAsync();

    private async Task SendAsync()
    {
        try
        {
            await this.owner.Shortcuts.SendAsync(this.shortcut, CancellationToken.None);
        }
        catch (Exception e)
        {
            this.Log.Error($"could not send {this.shortcut.Id}: {e.Message}");
        }
    }

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) => Faces.Icon(this.shortcut.Icon, imageSize);

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => this.shortcut.Label;
}
