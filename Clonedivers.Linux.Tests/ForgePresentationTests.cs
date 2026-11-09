using Clonedivers;

internal static class ForgePresentationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var status = new HellForgeStatus("/fixture", "0.4.0-beta", "25480438", true, true, false, true, true, null, null);
        var state = new ForgePlayerState(status, false, "raw internal diagnostic", new(false, true, "Imported"), null);
        check(ForgePresentation.Resolve().Action == ForgeAction.Setup, "a new Forge player gets one setup action");
        check(ForgePresentation.Resolve(state, connected: true).Action == ForgeAction.Prepare, "an incomplete mode offers setup");
        check(ForgePresentation.Resolve(state, connected: true, canLaunch: true).Action == ForgeAction.Launch, "ready Forge offers Launch");
        check(ForgePresentation.Resolve(state, connected: true, active: true, ownsSession: true, error: "broken", updateVersion: "999.0.0").Action == ForgeAction.EndSession,
            "End session takes priority over update offers and refresh failures");
        check(ForgePresentation.Resolve(state, connected: true, ownsSession: true, error: "broken").Action == ForgeAction.EndSession, "an owned session stays endable even with stale status");
        check(ForgePresentation.Resolve(state, connected: true, active: true).Action == ForgeAction.None, "a session owned by another launcher cannot be stopped here");
        check(ForgePresentation.Resolve(state, disconnected: true, active: true, ownsSession: true).Action == ForgeAction.Connect, "a lost bridge offers reconnect instead of an unusable stop");
        check(ForgePresentation.Resolve(state with { Status = status with { GameCopyReady = false } }, connected: true).Action == ForgeAction.Manager, "an unfinished game copy continues in official setup");
        check(ForgePresentation.Resolve(state with { Status = status with { CompatibilityProblem = "wrong version" } }, connected: true).Action == ForgeAction.Manager, "compatibility problems continue in official setup");
        check(ForgePresentation.Resolve(state with { Status = status with { RecoveryProblem = "Gameplay interrupted" } }, connected: true).Action == ForgeAction.Prepare, "an interrupted change recovers before account or mode setup");
        check(ForgePresentation.Resolve(state with { Account = new(true, false, "Captured") }, connected: true).Button == "Finish account sync", "a captured account offers finishing the verified import");
        check(ForgePresentation.Resolve(state, connected: true, syncing: true).Action == ForgeAction.Refresh, "pending capture checks progress without starting another capture");
        check(ForgePresentation.Resolve(state, connected: true, canLaunch: true, updateVersion: "999.0.0").Action == ForgeAction.Update, "a launcher update becomes the idle primary action");
        check(ForgePresentation.Resolve(updateVersion: "999.0.0", updateFailed: true).Button == "Try update again", "a failed release-page handoff has a retry path");
        check(ForgePresentation.Resolve(busy: true, updateVersion: "999.0.0").Action == ForgeAction.None, "in-progress operations cannot dispatch a second primary action");
        var failure = ForgePresentation.Resolve(error: "HttpRequestException: 504 /private/fixture");
        check(failure.Action == ForgeAction.Refresh && !failure.Detail.Contains("504") && !failure.Detail.Contains("/private"), "raw failures stay out of the status sentence");
        check(ForgePresentation.Resolve(error: "504", retryAction: ForgeAction.Setup).Action == ForgeAction.Setup, "a failed initial installer retries setup instead of connecting to a missing installation");
        check(ForgePresentation.FailureSummary("SHA-256 mismatch").Contains("verified") && ForgePresentation.FailureSummary("permission denied").Contains("permissions"), "integrity and folder failures have useful summaries");
        check(!DesktopProgram.UseTerminal(["--gui", "--forge-preview", "ready", "--forge-preview-mode", "empire", "--forge-render", "status"], true), "render option values never route previews into the terminal");
    }
}
