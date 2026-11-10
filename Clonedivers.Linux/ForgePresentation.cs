namespace Clonedivers;

internal enum ForgeAction { None, Setup, Connect, EndSession, Launch, Manager, Prepare, Sync, Refresh, Update }
internal sealed record ForgePresentation(string Headline, string Detail, string Button, ForgeAction Action)
{
    internal static ForgePresentation Resolve(ForgePlayerState? state = null, bool connected = false, bool disconnected = false,
        bool active = false, bool ownsSession = false, bool canLaunch = false, bool busy = false, string? error = null,
        string? updateVersion = null, bool updateFailed = false, bool syncing = false, ForgeAction retryAction = ForgeAction.Refresh)
    {
        if (busy) return new("Working…", "Keep this launcher open while the current step finishes.", "Working…", ForgeAction.None);
        if (disconnected) return new("Forge connection lost", "Reconnect to review the session and its cleanup before continuing.", "Reconnect", ForgeAction.Connect);
        if (active || ownsSession) return new("Forge session running", ownsSession ? "End the session here when you finish playing." : "End this session in the launcher that started it.", "End session", ownsSession ? ForgeAction.EndSession : ForgeAction.None);
        if (updateVersion != null) return new(updateFailed ? "Update couldn't open" : "Update available", updateFailed ? "Try again or review Session details in More options." : $"Launcher v{updateVersion} is ready. Open its release page to download the Linux package.", updateFailed ? "Try update again" : "View update", ForgeAction.Update);
        if (error != null) return new("Couldn't finish this step", FailureSummary(error), "Try again", retryAction);
        if (!connected) return new("Let’s get you ready", "Install HellForge, or connect an existing installation in More options.", "Set up Forge", ForgeAction.Setup);
        if (state?.Status is { GameCopyReady: false } or { RunnerReady: false } or { CompatibilityProblem: not null })
            return new("Finish Forge setup", "Continue in HellForge to prepare the game and its dedicated Linux environment.", "Continue setup", ForgeAction.Manager);
        if (state?.Status.RecoveryProblem != null) return new("Restore the previous setup", "Recover the interrupted change before preparing your selected mode.", "Recover setup", ForgeAction.Prepare);
        if (canLaunch) return new("Ready to play", "Your selected appearance and required gameplay mods are ready.", "Launch", ForgeAction.Launch);
        if (syncing) return new("Finish account sync", "Follow the account instructions in Session details, then close HD2 to finish the verified import.", "Check account sync", ForgeAction.Refresh);
        if (state?.Account is { Imported: false }) return new("Bring your HD2 account", "Sync your inventory through HellForge. Forge rewards stay local.", state.Account.Captured ? "Finish account sync" : "Sync account", ForgeAction.Sync);
        return new("Prepare your selected mode", "Install the verified appearance and required gameplay mods before playing.", "Set up mode", ForgeAction.Prepare);
    }

    internal static string FailureSummary(string diagnostic)
    {
        if (diagnostic.Contains("ownership", StringComparison.OrdinalIgnoreCase)) return "Sign in to Steam with the account that owns HD2, then try again.";
        if (diagnostic.Contains("SHA", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("integrity", StringComparison.OrdinalIgnoreCase)) return "Download again because the file could not be verified.";
        if (diagnostic.Contains("space", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("disk full", StringComparison.OrdinalIgnoreCase)) return "Free some disk space before trying again.";
        if (diagnostic.Contains("denied", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("permission", StringComparison.OrdinalIgnoreCase)) return "Review folder permissions in More options > Session details before trying again.";
        if (diagnostic.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return "Review Session details in More options before retrying the timed-out step.";
        if (System.Text.RegularExpressions.Regex.IsMatch(diagnostic, @"\b(5\d\d|408|429)\b") || diagnostic.Contains("HttpRequestException") || diagnostic.Contains("No such host", StringComparison.OrdinalIgnoreCase)) return "The download service didn't respond. Try again in a few minutes.";
        return "Open Session details in More options to review why this step couldn't finish.";
    }
}
