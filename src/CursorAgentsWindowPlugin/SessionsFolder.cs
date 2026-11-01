namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// A Dynamic Folder with two levels, like the VS Code plugin's.
//
// Sessions level: the host's own back key (top left, closes the folder), New, then one
// key per main agent, with a badge when it has side chats. A short press opens the agent;
// a long press opens its menu.
//
// Session menu: Back (to sessions), New chat (a side chat of this agent), Archive (the
// agent, which also hides its side chats), then the main chat and its side chats. A press
// on a chat opens it; a long press on the main chat archives the session. Side chats cannot
// be archived from the Keypad yet, because opening one selects its parent, so a long press
// on one does nothing. Archive actions ask for a second press within a few seconds.
public sealed class SessionsFolder : PluginDynamicFolder
{
    private const string Back = "back";
    private const string New = "new";
    private const string NewChat = "newchat";
    private const string Archive = "archive";
    private const string SessionPrefix = "s:";
    private const string ChatPrefix = "c:";
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    // Key names carry what the key shows (state, chat count, a short hash of the title), so
    // any visible change renames the key and the host fetches a fresh face and label instead
    // of reusing cached ones.
    private const char StateSeparator = '|';

    // Armed keys get this in their name, because the host only reliably redraws a key when its name changes.
    private const char ArmedMarker = '!';

    // How a profile addresses this folder's own key; PluginDynamicFolder has no ButtonImageChanged.
    private const string FolderActionName = "#DynamicFolder";

    private string? openSession;
    private bool longPressHandled;
    private string[] shownNames = [];

    // The key waiting for a confirming press: Archive, or a main chat id after a long press.
    private string? armed;
    private CancellationTokenSource? armTimer;

    public SessionsFolder()
    {
        this.DisplayName = "Agents window";
        this.Description = "Browse Cursor Agents window agents and their side chats";
        this.GroupName = "Agents window";
    }

    private CursorAgentsWindowPlugin Owner => (CursorAgentsWindowPlugin)this.Plugin;

    private SessionBoard Board => this.Owner.Board;

    public override bool Load()
    {
        this.Board.Changed += this.OnBoardChanged;
        return true;
    }

    public override bool Unload()
    {
        this.Board.Changed -= this.OnBoardChanged;
        return true;
    }

    // The host's own back key closes the folder without telling RunCommand, so the level
    // is reset whenever the folder opens or closes. Otherwise it reopens on the last menu.
    public override bool Activate()
    {
        this.ResetLevel();
        return true;
    }

    public override bool Deactivate()
    {
        this.ResetLevel();
        return true;
    }

    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.None;

    public override string GetButtonDisplayName(PluginImageSize imageSize) => "Cursor";

    public override BitmapImage GetButtonImage(PluginImageSize imageSize)
    {
        var top = this.Board.Ranked.FirstOrDefault()?.State ?? KeyState.None;
        return Faces.State(this.Board.Online ? top : KeyState.Offline, imageSize);
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        if (this.openSession is { } id && this.Board.Find(id) is { } session)
        {
            yield return this.CreateCommandName(Back);
            yield return this.CreateCommandName(NewChat);
            yield return this.CreateCommandName(this.armed == Archive ? Archive + ArmedMarker : Archive);
            foreach (var chat in session.Chats)
            {
                var marker = this.armed == chat.Id ? ArmedMarker.ToString() : "";
                yield return this.CreateCommandName($"{ChatPrefix}{chat.State}{TitleTag(chat.Title)}{marker}{StateSeparator}{chat.Id}");
            }
            yield break;
        }

        // Renamed when Cursor goes offline so the host redraws it as "Start Cursor".
        yield return this.CreateCommandName(this.Board.Online ? New : New + "-offline");
        foreach (var s in this.Board.Ranked)
        {
            yield return this.CreateCommandName($"{SessionPrefix}{s.State}{s.Chats.Count}{TitleTag(s.Title)}{StateSeparator}{s.Id}");
        }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (this.IsArmed(actionParameter)) return "Archive?";
        if (actionParameter == Back) return "Back";
        if (actionParameter == NewChat) return "New chat";
        if (actionParameter.StartsWith(New, StringComparison.Ordinal)) return Faces.NewLabel(this.Board.Online);
        if (actionParameter.StartsWith(Archive, StringComparison.Ordinal)) return "Archive";
        if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.Board.Find(Id(actionParameter, SessionPrefix))?.Title);
        }
        if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.Board.FindChat(Id(actionParameter, ChatPrefix))?.Chat.Title);
        }
        return KeyFace.Blank;
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (this.IsArmed(actionParameter)) return Faces.Glyph("?", imageSize, new BitmapColor(200, 30, 30));
        if (actionParameter == Back) return Faces.Glyph("‹", imageSize);
        if (actionParameter == NewChat) return Faces.Glyph("+", imageSize);
        if (actionParameter.StartsWith(New, StringComparison.Ordinal)) return Faces.NewGlyph(this.Board.Online, imageSize);
        if (actionParameter.StartsWith(Archive, StringComparison.Ordinal)) return Faces.Glyph("✓", imageSize);
        if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            var session = this.Board.Find(Id(actionParameter, SessionPrefix));
            return Faces.State(session?.State ?? KeyState.None, imageSize, session?.Chats.Count ?? 1);
        }
        if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return Faces.State(this.Board.FindChat(Id(actionParameter, ChatPrefix))?.Chat.State ?? KeyState.None, imageSize);
        }
        return Faces.State(KeyState.None, imageSize);
    }

    // Session and chat keys act on Release, so a hold can do something other than a tap,
    // and so a press that opens a menu does not hand its Release to the new key under the
    // finger. Other keys keep the default handling, which calls RunCommand.
    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        var isSession = actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal);
        var isChat = actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal);
        if (!isSession && !isChat)
        {
            return base.ProcessButtonEvent2(actionParameter, buttonEvent);
        }

        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press:
                this.longPressHandled = false;
                return true;
            case DeviceButtonEventType.LongPress:
                this.longPressHandled = true;
                if (isSession)
                {
                    this.OpenMenu(Id(actionParameter, SessionPrefix));
                }
                // Side chats cannot be archived from the Keypad yet, so a long press on one does nothing.
                else if (this.Board.FindChat(Id(actionParameter, ChatPrefix)) is { Chat.CanArchive: true } held)
                {
                    this.Arm(held.Chat.Id);
                }
                return true;
            case DeviceButtonEventType.RepeatPress:
                return true;
            case DeviceButtonEventType.Release:
                if (!this.longPressHandled)
                {
                    this.Tap(actionParameter);
                }
                this.longPressHandled = false;
                return true;
            default:
                return false;
        }
    }

    public override void RunCommand(string actionParameter) => this.Tap(actionParameter);

    private void Tap(string actionParameter)
    {
        if (this.armed is not null && !this.IsArmed(actionParameter))
        {
            this.Disarm();
        }

        if (actionParameter == Back)
        {
            this.ResetLevel(force: true);
        }
        else if (actionParameter == NewChat && this.openSession is { } session)
        {
            _ = this.NewChatAsync(session);
        }
        else if (actionParameter.StartsWith(New, StringComparison.Ordinal))
        {
            if (this.Board.Online) _ = this.NewAgentAsync();
            else Opener.Open(Links.AgentsWindow);
        }
        else if (actionParameter.StartsWith(Archive, StringComparison.Ordinal) && this.openSession is { } toArchive)
        {
            if (this.armed == Archive)
            {
                this.Disarm();
                _ = this.ArchiveAsync(toArchive);
            }
            else
            {
                this.Arm(Archive);
            }
        }
        else if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            var sessionId = Id(actionParameter, SessionPrefix);
            this.Owner.OpenChat(sessionId, sessionId);
        }
        else if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal) && this.Board.FindChat(Id(actionParameter, ChatPrefix)) is { } found)
        {
            if (this.armed == found.Chat.Id)
            {
                this.Disarm();
                _ = this.ArchiveAsync(found.Session.Id);
            }
            else
            {
                this.Owner.OpenChat(found.Session.Id, found.Chat.Id);
            }
        }
    }

    private void OpenMenu(string sessionId)
    {
        this.Disarm();
        this.openSession = sessionId;
        this.RefreshNames();
    }

    private async Task NewAgentAsync()
    {
        try
        {
            await this.Owner.Actions.NewAgentAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not open a new agent: {e.Message}");
        }
    }

    private async Task NewChatAsync(string sessionId)
    {
        try
        {
            if (await this.Owner.Actions.NewSideChatAsync(sessionId, CancellationToken.None))
            {
                this.Plugin.Log.Info("asked Cursor for a new side chat from the Keypad");
            }
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not create a side chat: {e.Message}");
        }
    }

    private async Task ArchiveAsync(string sessionId)
    {
        try
        {
            if (await this.Owner.Actions.ArchiveAsync(sessionId, CancellationToken.None))
            {
                this.Plugin.Log.Info("archived a session from the Keypad");
                this.ResetLevel(force: true);
            }
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not archive a session: {e.Message}");
        }
    }

    private void Arm(string id)
    {
        this.armTimer?.Cancel();
        this.armed = id;
        var timer = this.armTimer = new CancellationTokenSource();
        this.RefreshNames();
        _ = Task.Delay(ConfirmWindow, timer.Token).ContinueWith(
            _ => { if (this.armed == id) this.Disarm(); },
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void Disarm()
    {
        this.armTimer?.Cancel();
        this.armTimer = null;
        if (this.armed is null) return;
        this.armed = null;
        this.RefreshNames();
    }

    private bool IsArmed(string actionParameter) =>
        this.armed is { } id && actionParameter.Contains(ArmedMarker) &&
        (id == Archive ? actionParameter.StartsWith(Archive, StringComparison.Ordinal) : Id(actionParameter, ChatPrefix) == id);

    private void RefreshNames()
    {
        try
        {
            this.shownNames = [];
            this.ButtonActionNamesChanged();
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not refresh the Cursor agents folder");
        }
    }

    // Stable across runs, unlike string.GetHashCode. Hex only, so it never contains the
    // separator or the armed marker.
    private static string TitleTag(string title)
    {
        var hash = 2166136261u;
        foreach (var c in title)
        {
            hash = (hash ^ c) * 16777619u;
        }
        return "t" + hash.ToString("x8");
    }

    private static string Id(string actionParameter, string prefix)
    {
        var rest = actionParameter[prefix.Length..];
        var separator = rest.IndexOf(StateSeparator);
        return separator < 0 ? rest : rest[(separator + 1)..];
    }

    private void ResetLevel(bool force = false)
    {
        this.Disarm();
        if (this.openSession is null && !force) return;
        this.openSession = null;
        this.RefreshNames();
    }

    private void OnBoardChanged()
    {
        // The host caches the folder key's face and only re-reads it when asked.
        try
        {
            this.Plugin.OnActionImageChanged(FolderActionName, this.Name, true);
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not redraw the Cursor agents folder key");
        }

        // A session archived elsewhere closes its menu.
        if (this.openSession is { } open && this.Board.Find(open) is null)
        {
            this.ResetLevel();
            return;
        }

        // ButtonActionNamesChanged alone does not redraw keys whose names stay the same, so
        // each visible key is invalidated too.
        try
        {
            var names = this.GetButtonPressActionNames(DeviceType.All).ToArray();
            if (!names.SequenceEqual(this.shownNames))
            {
                this.shownNames = names;
                this.ButtonActionNamesChanged();
            }
            foreach (var name in names)
            {
                this.CommandImageChanged(name);
            }
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not refresh the Cursor agents folder");
        }
    }
}
