namespace CursorAgents;

public enum Shortcut
{
    // Cmd+N in the Agents window: new agent.
    NewAgent,

    // Cmd+Shift+S in the Agents window: new side chat of the selected agent.
    NewSideChat,

    // Cmd+Shift+E in the Agents window: archive the selected agent.
    ArchiveAgent,
}

// Actions Cursor has no link for. They go through the Agents window's own shortcuts, so
// they first bring the window up (and select the agent) with a link, wait until Cursor
// reports that, and only send keys while Cursor is frontmost.
public sealed class AgentActions(ISessionStore store, Action<string> open, Action<Shortcut> send, Func<bool> cursorFrontmost)
{
    private static readonly TimeSpan SelectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    public async Task<bool> NewAgentAsync(CancellationToken cancel)
    {
        open(Links.AgentsWindow);
        if (!await this.WaitFrontmostAsync(cancel)) return false;
        await Task.Delay(Settle, cancel);
        send(Shortcut.NewAgent);
        return true;
    }

    public Task<bool> NewSideChatAsync(string mainId, CancellationToken cancel) =>
        this.SendToSelectedAsync(mainId, Shortcut.NewSideChat, "new side chat", cancel);

    // Archives a main agent. Cursor then hides its side chats, as it does in its own UI.
    public Task<bool> ArchiveAsync(string mainId, CancellationToken cancel) =>
        this.SendToSelectedAsync(mainId, Shortcut.ArchiveAgent, "archive", cancel);

    private async Task<bool> SendToSelectedAsync(string mainId, Shortcut shortcut, string what, CancellationToken cancel)
    {
        open(Links.OpenAgent(mainId));
        var deadline = DateTime.UtcNow + SelectTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (store.ReadSelectedAgent() == mainId && cursorFrontmost())
            {
                await Task.Delay(Settle, cancel);
                // Re-check right before the keys go out: the shortcut acts on whatever is selected.
                if (store.ReadSelectedAgent() != mainId || !cursorFrontmost()) break;
                send(shortcut);
                return true;
            }
            await Task.Delay(PollStep, cancel);
        }

        Log.Warning($"did not {what}: Cursor did not select the agent or was not frontmost in time");
        return false;
    }

    private async Task<bool> WaitFrontmostAsync(CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + SelectTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (cursorFrontmost()) return true;
            await Task.Delay(PollStep, cancel);
        }
        Log.Warning("Cursor did not come to the front in time; not sending the New agent shortcut");
        return false;
    }
}
