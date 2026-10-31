namespace CursorAgents;

// Links handled by Cursor's built-in cursor-deeplink extension (Cursor 3.27).
public static class Links
{
    private const string Base = "cursor://anysphere.cursor-deeplink";

    // Opens or focuses the Agents window and selects the agent. Local composer ids work:
    // the handler (glass.openCloudAgentById) looks the id up among known agents first.
    public static string OpenAgent(string composerId) => $"{Base}/agent?id={Uri.EscapeDataString(composerId)}";

    // Opens or focuses the Agents window without changing its selection; starts Cursor if needed.
    public const string AgentsWindow = Base + "/glass";
}
