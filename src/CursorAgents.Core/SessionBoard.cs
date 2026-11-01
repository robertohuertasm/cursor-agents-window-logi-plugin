namespace CursorAgents;

// Polls Cursor's database and keeps the ranked session list current. `Changed` fires
// only when something a key shows (order, title, state, online) changed. States are
// re-derived every tick, because "Working" expires with time even when nothing is written.
public sealed class SessionBoard(ISessionStore store, Func<bool> cursorRunning, Func<DateTimeOffset>? clock = null)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private string signature = "";
    private Dictionary<string, KeyState> lastStates = [];
    private StoreSnapshot? lastSnapshot;
    private int failures;

    public IReadOnlyList<Session> Ranked { get; private set; } = [];

    public IReadOnlyList<RankedChat> RankedChats { get; private set; } = [];

    public bool Online { get; private set; }

    public event Action? Changed;

    public ISessionStore Store => store;

    public Session? At(int index) => index >= 0 && index < this.Ranked.Count ? this.Ranked[index] : null;

    public RankedChat? ChatAt(int index) => index >= 0 && index < this.RankedChats.Count ? this.RankedChats[index] : null;

    public Session? Find(string id) => this.Ranked.FirstOrDefault(s => s.Id == id);

    // Any other visible agent, to select briefly before switching chats inside one group.
    public string? DetourFor(string sessionId) => this.Ranked.FirstOrDefault(s => s.Id != sessionId)?.Id;

    public RankedChat? FindChat(string chatId)
    {
        foreach (var s in this.Ranked)
        {
            if (s.Chats.FirstOrDefault(c => c.Id == chatId) is { } chat) return new RankedChat(s, chat);
        }
        return null;
    }

    // Null means Cursor is not running or its database is unreadable. Returns whether anything visible changed.
    public bool Apply(StoreSnapshot? snapshot)
    {
        var at = this.now();
        var ranked = snapshot is null
            ? []
            : Ranking.Rank(StateRules.BuildSessions(snapshot.Rows, at));
        var online = snapshot is not null;
        if (online) this.LogTransitions(ranked);
        var next = Signature(ranked, online);
        this.Ranked = ranked;
        this.RankedChats = Ranking.RankChats(ranked);
        this.Online = online;
        if (next == this.signature) return false;
        this.signature = next;
        return true;
    }

    public enum FailureAction
    {
        KeepLast,
        Offline,
    }

    // One unreadable tick is usually Cursor writing its files, so the last state is kept
    // for a couple of ticks before the keys say offline.
    public static FailureAction OnReadFailure(int failures, bool wasOnline) =>
        wasOnline && failures < 3 ? FailureAction.KeepLast : FailureAction.Offline;

    public async Task RunAsync(CancellationToken cancel)
    {
        var first = true;
        while (!cancel.IsCancellationRequested)
        {
            StoreSnapshot? snapshot = null;
            try
            {
                if (cursorRunning())
                {
                    snapshot = store.Read();
                    failures = 0;
                    if (!this.Online && !first) Log.Info("Cursor is running and its database is readable again");
                }
                else if (this.Online || first)
                {
                    Log.Info("Cursor is not running, keys show offline");
                }
            }
            catch (Exception e)
            {
                failures++;
                if (OnReadFailure(failures, this.Online) == FailureAction.KeepLast)
                {
                    snapshot = this.lastSnapshot;
                }
                else if (this.Online || first)
                {
                    Log.Warning($"could not read Cursor's database, keys show offline: {e.GetType().Name}: {e.Message}");
                }
            }
            this.lastSnapshot = snapshot;

            // The service keeps faces from an earlier run, so the first poll always redraws.
            if (this.Apply(snapshot) || first) this.Changed?.Invoke();
            first = false;

            try
            {
                await Task.Delay(PollInterval, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // One line per state change, with a short id and no title, to compare with what the keys show.
    private void LogTransitions(IReadOnlyList<Session> ranked)
    {
        var states = ranked.SelectMany(s => s.Chats).ToDictionary(c => c.Id, c => c.State);
        if (this.lastStates.Count > 0)
        {
            foreach (var (id, state) in states)
            {
                if (this.lastStates.TryGetValue(id, out var before) && before != state)
                {
                    var rank = ranked.ToList().FindIndex(s => s.Chats.Any(c => c.Id == id)) + 1;
                    Log.Info($"chat {id[..Math.Min(8, id.Length)]} {before} -> {state} (session #{rank})");
                }
            }
        }
        this.lastStates = states;
    }

    private static string Signature(IReadOnlyList<Session> ranked, bool online) =>
        (online ? "on" : "off") + "\n" + string.Join("\n", ranked.Select(s =>
            $"{s.Id}\t{s.Title}\t{s.State}\t" + string.Join(",", s.Chats.Select(c => $"{c.Id}|{c.Title}|{c.State}"))));
}
