namespace CursorAgentsWindow.Probes;

/// <summary>Polls the reader once a second and logs state changes and timing stats.</summary>
public static class ProbeLoop
{
    public static async Task RunAsync(Action<string> log, CancellationToken ct, TimeSpan? statsEvery = null)
    {
        using var reader = new CursorDbReader();
        log($"Reading copies of {reader.SourceDir}");
        var known = new Dictionary<string, string>();
        var reads = 0;
        var skipped = 0;
        var failures = 0;
        long maxCopy = 0, maxQuery = 0, sumCopy = 0, sumQuery = 0;
        var every = statsEvery ?? TimeSpan.FromSeconds(30);
        var nextStats = DateTime.UtcNow + every;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = reader.Read();
                if (r.Skipped)
                {
                    skipped++;
                }
                else
                {
                    reads++;
                    sumCopy += r.CopyMs;
                    sumQuery += r.QueryMs;
                    maxCopy = Math.Max(maxCopy, r.CopyMs);
                    maxQuery = Math.Max(maxQuery, r.QueryMs);
                    foreach (var s in r.Sessions)
                    {
                        var line = $"{s.State,-7} {s.Raw}";
                        if (!known.TryGetValue(s.Id, out var prev) || prev != line)
                        {
                            known[s.Id] = line;
                            log($"{s.Id[..8]} {s.Workspace,-24} {line}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failures++;
                log($"Read failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (DateTime.UtcNow >= nextStats)
            {
                var n = Math.Max(reads, 1);
                log($"Stats: {reads} reads, {skipped} skipped (unchanged), {failures} failures, copy avg {sumCopy / n} ms max {maxCopy} ms, query avg {sumQuery / n} ms max {maxQuery} ms, managed heap {GC.GetTotalMemory(false) / 1024 / 1024} MB");
                nextStats = DateTime.UtcNow + every;
            }

            try
            {
                await Task.Delay(1000, ct);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}
