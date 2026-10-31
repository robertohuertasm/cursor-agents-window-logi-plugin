namespace Loupedeck.CursorDbProbePlugin;

using CursorAgentsWindow.Probes;

public class CursorDbProbeApplication : ClientApplication
{
    protected override string GetProcessName() => "";

    protected override string GetBundleName() => "";

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}

public class CursorDbProbePlugin : Plugin
{
    private readonly CancellationTokenSource cts = new();

    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    public override void Load()
    {
        this.Log.Info($"CursorDbProbe loaded. Runtime {Environment.Version}, Microsoft.Data.Sqlite {typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.GetName().Version}");
        _ = Task.Run(async () =>
        {
            try
            {
                await ProbeLoop.RunAsync(line => this.Log.Info($"CursorDbProbe {line}"), this.cts.Token);
            }
            catch (Exception ex)
            {
                this.Log.Error($"CursorDbProbe failed: {ex.GetType().FullName}: {ex.Message}");
            }
        });
    }

    public override void Unload() => this.cts.Cancel();
}
