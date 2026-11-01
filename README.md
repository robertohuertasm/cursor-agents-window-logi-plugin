# Cursor Agents window Logi plugin

A Logitech MX Creative Keypad plugin that shows Cursor Agents window agents and their state on the keys, and opens an agent with a press. It is the Cursor counterpart of [vscode-agents-window-logi-plugin](https://github.com/robertohuertasm/vscode-agents-window-logi-plugin) and uses the same key colors.

Status: 0.1.0, first version. Loads in Logi Plugin Service on macOS; not yet tried on the Keypad. Windows paths are written but untested.

## Actions

Drag these onto keys in Logi Options+ (group **Cursor Agents Window**):

| Action | What it does |
|---|---|
| **Recent agent 1…9** | The Nth agent across all folders: waiting for input first, then finished but unread, then working, then idle, most recent first within each. Press opens it in the Agents window. A long press asks **Archive?** and a press within 3 seconds archives it. |
| **New agent** | Brings up the Agents window and presses its New Agent shortcut (Cmd+N). While Cursor is not running the key reads **Start Cursor** (▶) and a press starts it. |
| **Agents window** (folder) | **New** and one key per project folder, as the Agents window groups agents, with a badge for the number of agents. A press opens the folder's agents. Inside, **Back**, then one key per agent: a press opens it, a long press asks **Archive?**. |

An agent with no name yet (its first message has not been sent) shows its folder's name, since Cursor sets the name with the first message.

Key colors: red waiting for input, amber working, blue finished but not looked at yet, green outline idle, grey dotted empty, dark red dotted Cursor not running.

## How it works

- **State:** Cursor has no local API for agent sessions. Once a second, the plugin copies Cursor's global `state.vscdb` and its WAL (`~/Library/Application Support/Cursor/User/globalStorage/` on macOS) to a temp folder, only if they changed, and reads the copy. Agents come from the `composerHeaders` table. Waiting is `hasBlockingPendingActions` (command approvals) or an open `ask_question` card; unread is `hasUnreadMessages`; working is an unfinished `composerData` status while the agent's messages keep changing (Cursor leaves `status: aborted` behind when an agent is stopped, so a status alone is not enough). It never writes to Cursor's storage and never reads message text.
- **Focus:** a press opens `cursor://anysphere.cursor-deeplink/agent?id=<composerId>`, which brings up the Agents window on that agent.
- **New and Archive:** Cursor has no link for either, so the plugin uses the Agents window's own shortcuts: Cmd+N, and Cmd+Shift+E on the selected agent. Before archiving it opens the agent, waits until Cursor's database reports it selected and Cursor is frontmost, and checks both again right before sending the keys.

Research notes and decisions live in [robertohuertasm/agent-skills, `labs/projects/agent-monitoring`](https://github.com/robertohuertasm/agent-skills/tree/main/labs/projects/agent-monitoring): `research/cursor-agent-mode.md` and decision 0003.

## Layout

- `src/CursorAgents.Core`: everything that does not need the Logi API (database reader, state rules, ranking, links, shortcut sequencing). Tested directly.
- `src/CursorAgentsWindowPlugin`: the Logi plugin (actions, folder, key faces). The Plugin Service only loads it when the plugin class is named after the manifest's `name` (`CursorAgentsWindow` → `CursorAgentsWindowPlugin`).
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
