namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

public class CursorAgentsWindowPlugin : Plugin
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<RecentChatCommand> recent = [];
    private readonly CursorStore store;
    private NewAgentCommand? newAgent;

    public CursorAgentsWindowPlugin()
    {
        CursorAgents.Log.Info = text => this.Log.Info(text);
        CursorAgents.Log.Warning = text => this.Log.Warning(text);
        CursorAgents.Log.Error = text => this.Log.Error(text);
        this.store = new CursorStore();
        this.Board = new SessionBoard(this.store, CursorApp.IsRunning);
        this.Actions = new AgentActions(this.store, Opener.Open, this.Send, CursorApp.IsFrontmost);
    }

    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    public SessionBoard Board { get; }

    public AgentActions Actions { get; }

    public override void Load()
    {
        for (var slot = 1; slot <= RecentChatCommand.SlotCount; slot++)
        {
            var command = new RecentChatCommand(this, slot);
            this.recent.Add(command);
            this.AddDynamicAction(command);
        }
        this.newAgent = new NewAgentCommand(this);
        this.AddDynamicAction(this.newAgent);
        this.Board.Changed += this.RedrawKeys;
        _ = Task.Run(() => this.Board.RunAsync(this.stop.Token));
        CursorAgents.Log.Info($"{this.recent.Count} recent-chat keys registered, reading copies of {this.store.SourceDir}");
    }

    public override void Unload()
    {
        this.stop.Cancel();
        this.Board.Changed -= this.RedrawKeys;
        this.store.Dispose();
    }

    public void OpenChat(string sessionId, string chatId) => _ = this.OpenChatAsync(sessionId, chatId);

    private async Task OpenChatAsync(string sessionId, string chatId)
    {
        try
        {
            await this.Actions.OpenChatAsync(sessionId, chatId, this.Board.DetourFor(sessionId), CancellationToken.None);
        }
        catch (Exception e)
        {
            CursorAgents.Log.Error($"could not open a chat: {e.Message}");
        }
    }

    private void Send(Shortcut shortcut)
    {
        var (key, modifiers) = shortcut switch
        {
            Shortcut.NewAgent => (VirtualKeyCode.KeyN, ModifierKey.Command),
            Shortcut.NewSideChat => (VirtualKeyCode.KeyS, ModifierKey.Command | ModifierKey.Shift),
            Shortcut.ArchiveAgent => (VirtualKeyCode.KeyE, ModifierKey.Command | ModifierKey.Shift),
            Shortcut.FocusMainChat => (VirtualKeyCode.KeyI, ModifierKey.Command),
            _ => throw new ArgumentOutOfRangeException(nameof(shortcut)),
        };
        // Cmd on macOS, Ctrl on Windows, as Cursor binds them.
        if (OperatingSystem.IsWindows()) modifiers = (modifiers & ~ModifierKey.Command) | ModifierKey.Control;
        this.KeyboardApi.SendShortcut(key, modifiers);
        CursorAgents.Log.Info($"sent the {shortcut} shortcut to Cursor");
    }

    private void RedrawKeys()
    {
        foreach (var command in this.recent)
        {
            command.Redraw();
        }
        this.newAgent?.Redraw();
    }
}
