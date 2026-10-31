namespace Loupedeck.CursorAgentsWindowPlugin;

// The Plugin Service will not load a plugin without a ClientApplication, even one
// not tied to any app.
public class CursorAgentsWindowApplication : ClientApplication
{
    protected override string GetProcessName() => "";

    protected override string GetBundleName() => "";

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}
