using CursorAgentsWindow.Probes;

var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 0;
using var cts = seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await ProbeLoop.RunAsync(line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"), cts.Token, TimeSpan.FromSeconds(10));
