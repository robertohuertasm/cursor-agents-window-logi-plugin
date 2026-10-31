# Cursor Agents window Logi plugin

A Logitech MX Creative Keypad plugin that will show Cursor Agents window sessions and their state on the keys, and open a session with a press. It is the Cursor counterpart of [vscode-agents-window-logi-plugin](https://github.com/robertohuertasm/vscode-agents-window-logi-plugin).

Status: research probes only. No plugin yet.

## How it will work

- **State:** Cursor has no local API for agent sessions. The plugin reads a private copy of Cursor's global `state.vscdb` (`~/Library/Application Support/Cursor/User/globalStorage/` on macOS) once a second, and only when the file changed. Sessions come from the `composerHeaders` table; state comes from `hasUnreadMessages`, `hasBlockingPendingActions`, `composerData:<id>.status`, and the newest tool call's status. It never writes to Cursor's storage and never reads message text.
- **Focus:** a press opens `cursor://anysphere.cursor-deeplink/agent?id=<composerId>`, which switches Cursor's Agents window to that session.

Research notes and decisions live in [robertohuertasm/agent-skills, `labs/projects/agent-monitoring`](https://github.com/robertohuertasm/agent-skills/tree/main/labs/projects/agent-monitoring), see `research/cursor-agent-mode.md` and decision 0003.

## Probes

Requires the .NET 10 SDK. The plugin probe also needs Logi Options+ with Logi Plugin Service 6.4 or later.

- [`probes/CursorDbCli`](probes/CursorDbCli): console version. Prints state changes per session and timing stats.
- [`probes/CursorDbProbe`](probes/CursorDbProbe): a throwaway Logi plugin with no actions that runs the same reader inside Logi Plugin Service and logs to `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/CursorDbProbe.log`.
- [`probes/Shared`](probes/Shared): the reader and the polling loop both probes use.

```sh
dotnet run --project probes/CursorDbCli -- 60        # run for 60 seconds
dotnet build probes/CursorDbProbe -p:Link=true       # link into Logi Plugin Service and reload
rm ~/Library/Application\ Support/Logi/LogiPluginService/Plugins/CursorDbProbe.link
```

The logs show the first 8 characters of each session ID and its workspace folder name, nothing else.
