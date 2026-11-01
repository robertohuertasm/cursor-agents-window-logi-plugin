# Cursor Agents window Logi plugin

A Logitech MX Creative Keypad plugin that shows Cursor Agents window agents and their state on the keys, and opens an agent with a press. It is the Cursor counterpart of [vscode-agents-window-logi-plugin](https://github.com/robertohuertasm/vscode-agents-window-logi-plugin) and uses the same key colors.

Status: 0.1.0, first version. Loads in Logi Plugin Service on macOS; not yet tried on the Keypad. Windows paths are written but untested.

## Actions

Drag these onto keys in Logi Options+ (group **Cursor Agents Window**):

| Action | What it does |
|---|---|
| **Recent chat 1…9** | The Nth chat across all agents, side chats included, so an agent with several active side chats takes several keys: waiting for input first, then finished but unread, then working, then idle. Press opens it. A long press on a main chat asks **Archive?** and a press within 3 seconds archives the agent (Cursor then hides its side chats). A long press on a side chat does nothing for now (see below). |
| **New agent** | Brings up the Agents window and presses its New Agent shortcut (Cmd+N). While Cursor is not running the key reads **Start Cursor** (▶) and a press starts it. |
| **Sidebar, Right Sidebar, Files, Terminal, Browser, Changes, Search** (group **Shortcuts**) | Send the Cursor Agents window's keybinding for that view. macOS: ⌘B, ⌥⌘B, ⌘G, ⌘J, ⇧⌘B, ⌘E, ⇧⌘F. Windows (untested): Ctrl+B, Ctrl+Alt+B, Ctrl+G, Ctrl+J, Ctrl+Shift+B, Ctrl+E, Ctrl+Shift+F. The Agents window is brought to the front first; the keys are only sent once Cursor is frontmost. The key shows the view's codicon and name; the description in Options+ gives the shortcut. |
| **Cursor shortcuts** (folder, group **Shortcuts**) | Opens a folder with **New** (as the New agent key) and the seven shortcut keys, after the back key the Plugin Service draws top left: one Keypad page. |
| **Agents window** (folder) | **New**, then one key per agent, with a white badge for the number of chats when it has side chats. A press opens the agent. A long press opens its menu: **Back**, **New chat** (a side chat of that agent, Cmd+Shift+S), **Archive** (the agent and, with it, its side chats), and one key per chat, main chat first. In the menu, a press opens a chat and a long press on the main chat archives the agent; a long press on a side chat does nothing for now. Archiving asks for a second press within 3 seconds. |

An agent with no name yet (its first message has not been sent) shows its folder's name, since Cursor sets the name with the first message.

Keys show Cursor's logo in black on a disc colored by state, as the VS Code plugin does: red waiting for input, amber working, blue finished but not looked at yet, green idle, dark red Cursor not running, dark grey empty.

## How it works

- **State:** Cursor has no local API for agent sessions. Once a second, the plugin copies Cursor's global `state.vscdb` and its WAL (`~/Library/Application Support/Cursor/User/globalStorage/` on macOS) to a temp folder, only if they changed, and reads the copy. Agents and their side chats come from the `composerHeaders` table; a side chat (`subagentTypeName = 'side-chat'`) names its parent in `subagentInfo.parentComposerId`, and side chats of an archived agent are hidden, as Cursor hides them. Waiting is `hasBlockingPendingActions` (command approvals) or an open `ask_question` card; unread is `hasUnreadMessages`; working is an unfinished `composerData` status while the agent's messages keep changing (Cursor leaves `status: aborted` behind when an agent is stopped, so a status alone is not enough). It never writes to Cursor's storage and never reads message text.
- **Focus:** a press opens `cursor://anysphere.cursor-deeplink/agent?id=<composerId>`, which brings up the Agents window on that agent. Cursor ignores the link when the agent's group is already selected, and restores the group's last active tab, which may be a side chat. So:
  - opening a side chat of the selected group first selects another agent, then the side chat. The window visibly flickers, which the user found acceptable;
  - opening a main chat ends with Cmd+I (Focus Chat Input), which moves the cursor to the main chat. Coming from another group, the side chat may show for a moment first.
- **Side chats cannot be archived from the Keypad yet.** Cursor archives with Cmd+Shift+E on the selected agent, and opening a side chat selects its parent, so the key would archive the whole group. Cursor's tab menu can archive a side chat on its own; a safe way to trigger it from the plugin is still being looked for.
- **New, New chat and Archive:** Cursor has no link for these, so the plugin uses the Agents window's own shortcuts: Cmd+N, Cmd+Shift+S (new side chat of the selected agent), and Cmd+Shift+E (archive the selected agent). Before New chat or Archive it opens the agent, waits until Cursor's database reports it selected and Cursor is frontmost, and checks both again right before sending the keys.

- **Shortcuts:** the Agents window link brings that window forward (Cursor's IDE window shares the app, so being frontmost is not enough), `lsappinfo` confirms Cursor is frontmost, then the Plugin Service's `KeyboardApi.SendShortcut` presses the keys. The bindings were read from Cursor 3.27's Agents window and checked on the Keypad; Files (⌘G) and Changes (⌘E) differ from VS Code, and Right Sidebar is Cursor's "Open Apps Panel".

Research notes and decisions live in [robertohuertasm/agent-skills, `labs/projects/agent-monitoring`](https://github.com/robertohuertasm/agent-skills/tree/main/labs/projects/agent-monitoring): `research/cursor-agent-mode.md` and decision 0003.

## Layout

- `src/CursorAgents.Core`: everything that does not need the Logi API (database reader, state rules, ranking, links, shortcut sequencing). Tested directly.
- `src/CursorAgentsWindowPlugin`: the Logi plugin (actions, folders, key faces). The Plugin Service only loads it when the plugin class is named after the manifest's `name` (`CursorAgentsWindow` → `CursorAgentsWindowPlugin`).
- `src/CursorAgentsWindowPlugin/Icons`: [codicons](https://github.com/microsoft/vscode-codicons) (CC BY 4.0) for the shortcut keys, and `cursor.svg`, Cursor's logo as Cursor 3.27 draws it in its own layout icons.
- `package/metadata/Icon256x256.png`: the plugin icon, Cursor's app icon.
- `tests/CursorAgents.Core.Tests`: xUnit tests for the core, including a reader test against a database with Cursor's table layout.
- `package/metadata/LoupedeckPackage.yaml`: plugin manifest and the only version number.
- `probes/CursorDbCli`: prints the ranked agents (short ids and folder names only) as they change.

## Development

Requires the .NET 10 SDK, Logi Options+ with Logi Plugin Service 6.4 or later, and Cursor 3.27 or later.

```sh
dotnet test tests/CursorAgents.Core.Tests
dotnet build src/CursorAgentsWindowPlugin   # Debug: links build/Debug into the Plugin Service and reloads it
dotnet clean src/CursorAgentsWindowPlugin   # removes the link
dotnet run --project probes/CursorDbCli -- 60
```

The log is at `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/CursorAgentsWindow.log`. A warning that the service "Cannot create dynamic action" for `RecentAgentCommand` is expected: the plugin adds those actions itself.
