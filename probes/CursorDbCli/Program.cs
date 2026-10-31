using CursorAgents;

// Prints the ranked agents whenever something a key shows changes. Ids are shortened; titles are not printed.
var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 0;
using var cts = seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var store = new CursorStore();
var board = new SessionBoard(store, CursorApp.IsRunning);
board.Changed += () =>
{
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} online={board.Online}");
    var rank = 1;
    foreach (var a in board.Ranked)
    {
        Console.WriteLine($"  #{rank++} {a.Id[..8]} {a.WorkspaceName,-24} {a.State}");
    }
};
await board.RunAsync(cts.Token);
