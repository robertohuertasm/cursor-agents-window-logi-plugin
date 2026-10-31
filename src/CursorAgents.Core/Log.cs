namespace CursorAgents;

// The plugin routes these to the Plugin Service log; elsewhere they go to the console.
public static class Log
{
    public static Action<string> Info { get; set; } = Console.WriteLine;
    public static Action<string> Warning { get; set; } = Console.Error.WriteLine;
    public static Action<string> Error { get; set; } = Console.Error.WriteLine;
}
