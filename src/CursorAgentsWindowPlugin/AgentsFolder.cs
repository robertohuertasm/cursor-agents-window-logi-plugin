namespace Loupedeck.CursorAgentsWindowPlugin;

using CursorAgents;

// A Dynamic Folder with two levels, like the VS Code plugin's.
//
// Folders level: the host's own back key (top left, closes the folder), New, then one key
// per workspace folder, as the Agents window groups agents. A press opens the folder's
// agents; a folder with a single agent opens that agent directly.
//
// Agents level: Back (to folders), then the folder's agents. A press opens an agent; a long
// press asks "Archive?" and a second press within a few seconds archives it, because the
// Keypad has no undo and the key vanishes at once.
public sealed class AgentsFolder : PluginDynamicFolder
{
    private const string Back = "back";
    private const string New = "new";
    private const string WorkspacePrefix = "w:";
    private const string AgentPrefix = "a:";
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    // Folder key names carry what the key shows (state, count, a short hash of the title),
    // so any visible change renames the key and the host fetches a fresh face and label
    // instead of reusing cached ones.
    private const char StateSeparator = '|';

    // Armed keys get this in their name, because the host only reliably redraws a key when its name changes.
    private const char ArmedMarker = '!';

    // How a profile addresses this folder's own key; PluginDynamicFolder has no ButtonImageChanged.
    private const string FolderActionName = "#DynamicFolder";

    private string? openWorkspace;
    private bool longPressHandled;
    private string[] shownNames = [];
    private string? armed;
    private CancellationTokenSource? armTimer;

    public AgentsFolder()
    {
        this.DisplayName = "Agents window";
        this.Description = "Browse Cursor Agents window agents by folder";
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
    // is reset whenever the folder opens or closes.
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
        return Faces.Ring(this.Board.Online ? top : KeyState.Offline, imageSize);
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        if (this.openWorkspace is { } key && this.Board.FindWorkspace(key) is { } workspace)
        {
            yield return this.CreateCommandName(Back);
            foreach (var s in workspace.Sessions)
            {
                var marker = this.armed == s.Id ? ArmedMarker.ToString() : "";
                yield return this.CreateCommandName($"{AgentPrefix}{s.State}{TitleTag(s.Title)}{marker}{StateSeparator}{s.Id}");
            }
            yield break;
        }

        // Renamed when Cursor goes offline so the host redraws it as "Start Cursor".
        yield return this.CreateCommandName(this.Board.Online ? New : New + "-offline");
        foreach (var w in this.Board.Workspaces)
        {
            yield return this.CreateCommandName($"{WorkspacePrefix}{w.State}{w.Sessions.Count}{TitleTag(w.Name)}{StateSeparator}{w.Key}");
        }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (this.IsArmed(actionParameter)) return "Archive?";
        if (actionParameter == Back) return "Back";
        if (actionParameter.StartsWith(New, StringComparison.Ordinal)) return Faces.NewLabel(this.Board.Online);
        if (actionParameter.StartsWith(WorkspacePrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.Board.FindWorkspace(Payload(actionParameter, WorkspacePrefix))?.Name);
        }
        if (actionParameter.StartsWith(AgentPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.Board.Find(Payload(actionParameter, AgentPrefix))?.Title);
        }
        return KeyFace.Blank;
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (this.IsArmed(actionParameter)) return Faces.Glyph("?", imageSize, new BitmapColor(200, 30, 30));
        if (actionParameter == Back) return Faces.Glyph("‹", imageSize);
        if (actionParameter.StartsWith(New, StringComparison.Ordinal)) return Faces.NewGlyph(this.Board.Online, imageSize);
        if (actionParameter.StartsWith(WorkspacePrefix, StringComparison.Ordinal))
        {
            var w = this.Board.FindWorkspace(Payload(actionParameter, WorkspacePrefix));
            return Faces.Ring(w?.State ?? KeyState.None, imageSize, w?.Sessions.Count ?? 1);
        }
        if (actionParameter.StartsWith(AgentPrefix, StringComparison.Ordinal))
        {
            return Faces.Ring(this.Board.Find(Payload(actionParameter, AgentPrefix))?.State ?? KeyState.None, imageSize);
        }
        return Faces.Ring(KeyState.None, imageSize);
    }

    // Agent keys handle their own events so a hold can archive: a short press acts on
    // Release, a long press on LongPress. Other keys keep the default handling.
    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (!actionParameter.StartsWith(AgentPrefix, StringComparison.Ordinal))
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
                if (this.armed is null && this.Board.Find(Payload(actionParameter, AgentPrefix)) is { } held)
                {
                    this.Arm(held.Id);
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
        else if (actionParameter.StartsWith(New, StringComparison.Ordinal))
        {
            if (this.Board.Online) _ = this.NewAsync();
            else Opener.Open(Links.AgentsWindow);
        }
        else if (actionParameter.StartsWith(WorkspacePrefix, StringComparison.Ordinal) && this.Board.FindWorkspace(Payload(actionParameter, WorkspacePrefix)) is { } workspace)
        {
            this.OpenLevel(workspace.Key);
        }
        else if (actionParameter.StartsWith(AgentPrefix, StringComparison.Ordinal) && this.Board.Find(Payload(actionParameter, AgentPrefix)) is { } session)
        {
            if (this.armed == session.Id)
            {
                this.Disarm();
                _ = this.ArchiveAsync(session.Id);
            }
            else
            {
                Opener.Open(Links.OpenAgent(session.Id));
            }
        }
    }

    private async Task NewAsync()
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

    private async Task ArchiveAsync(string id)
    {
        try
        {
            if (await this.Owner.Actions.ArchiveAsync(id, CancellationToken.None))
            {
                this.Plugin.Log.Info("archived an agent from the folder");
            }
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not archive an agent: {e.Message}");
        }
    }

    private void OpenLevel(string workspaceKey)
    {
        this.Disarm();
        this.openWorkspace = workspaceKey;
        this.RefreshNames();
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
        this.armed is { } id && actionParameter.StartsWith(AgentPrefix, StringComparison.Ordinal)
        && actionParameter.Contains(ArmedMarker) && Payload(actionParameter, AgentPrefix) == id;

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

    // Everything after the first separator, so workspace paths may contain any character.
    private static string Payload(string actionParameter, string prefix)
    {
        var rest = actionParameter[prefix.Length..];
        var separator = rest.IndexOf(StateSeparator);
        return separator < 0 ? rest : rest[(separator + 1)..];
    }

    private void ResetLevel(bool force = false)
    {
        this.Disarm();
        if (this.openWorkspace is null && !force) return;
        this.openWorkspace = null;
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

        // A folder whose last agent was archived closes its level.
        if (this.openWorkspace is { } open && this.Board.FindWorkspace(open) is null)
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
