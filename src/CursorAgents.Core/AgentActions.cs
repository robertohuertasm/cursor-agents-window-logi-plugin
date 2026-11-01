namespace CursorAgents;

public enum Shortcut
{
    // Cmd+N in the Agents window: new agent.
    NewAgent,

    // Cmd+Shift+S in the Agents window: new side chat of the selected agent.
    NewSideChat,

    // Cmd+Shift+E in the Agents window: archive the selected agent.
    ArchiveAgent,

    // Cmd+I in the Agents window: focus the selected agent's main chat input.
    FocusMainChat,
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

    // Opens a chat. Tested 2026-11-01:
    // - Cursor ignores an agent link when that agent's group is already selected, so moving
    //   to a side chat of the selected group first selects another agent, then the side
    //   chat. The side chat tab comes to the front with no visible flicker.
    // - An agent link restores the tab that was last active in the group, which may be a
    //   side chat. So opening a main chat always ends with Cmd+I (Focus Chat Input), which
    //   moves the cursor to the main chat.
    public async Task OpenChatAsync(string sessionId, string chatId, string? detourId, CancellationToken cancel)
    {
        if (chatId == sessionId)
        {
            open(Links.OpenAgent(sessionId));
            if (await this.WaitSelectedAsync(sessionId, cancel))
            {
                await Task.Delay(Settle, cancel);
                if (store.ReadSelectedAgent() == sessionId && cursorFrontmost()) send(Shortcut.FocusMainChat);
            }
            return;
        }

        if (detourId is not null && store.ReadSelectedAgent() == sessionId)
        {
            open(Links.OpenAgent(detourId));
            var deadline = DateTime.UtcNow + SelectTimeout;
            while (DateTime.UtcNow < deadline && store.ReadSelectedAgent() != detourId)
            {
                await Task.Delay(PollStep, cancel);
            }
        }
        open(Links.OpenAgent(chatId));
    }

    private async Task<bool> WaitSelectedAsync(string agentId, CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + SelectTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (store.ReadSelectedAgent() == agentId && cursorFrontmost()) return true;
            await Task.Delay(PollStep, cancel);
        }
        Log.Warning("Cursor did not select the agent or come to the front in time; not focusing its main chat");
        return false;
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
        Log.Warning("Cursor did not come to the front in time; not sending its shortcut");
        return false;
    }
}
