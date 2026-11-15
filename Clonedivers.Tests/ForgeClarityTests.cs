using System.Reflection;

namespace Clonedivers.Tests;

static class ForgeClarityTests
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(HellForgeForm form, string name) => (T)typeof(HellForgeForm).GetField(name, Members)!.GetValue(form)!;
    static void Set(HellForgeForm form, string name, object? value) => typeof(HellForgeForm).GetField(name, Members)!.SetValue(form, value);
    static void View(HellForgeForm form) => typeof(HellForgeForm).GetMethod("UpdateView", Members)!.Invoke(form, null);
    static IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>().SelectMany(child => Children(child).Prepend(child));
    internal static void Run(string area, Action<bool, string> check)
    {
        var game = Game.IsRunningCheck; var steam = HellForgeForm.SteamReadyCheck; var open = HellForgeForm.OpenManager;
        var context = SynchronizationContext.Current;
        Game.IsRunningCheck = () => false; HellForgeForm.SteamReadyCheck = () => true;
        try
        {
            Instructions(check);
            foreach (var error in new[] { "Not enough disk space: need 512 MB", "disk full: HTTP 503", "Access denied: HTTP 500", "Permission denied: 502" })
                check(HellForgeForm.FailureSummary(error) == (error.Contains("denied", StringComparison.OrdinalIgnoreCase)
                    ? "Review folder permissions in More > Session details before trying again." : "Free some disk space before trying again."),
                    "local storage/permission error takes priority over server-like numbers: " + error);
            check(HellForgeForm.FailureSummary("HTTP 503 Service Unavailable").StartsWith("GitHub"), "actual server failures still offer a network retry");
            check(HellForgeWarbonds.WindowsProcessNames.SequenceEqual(new[] { "helldivers2", "HellForge", "HellForge.Launcher" }),
                "Warbond idle checks include both Windows Forge processes and the game");
            States(area, check);
        }
        finally { Game.IsRunningCheck = game; HellForgeForm.SteamReadyCheck = steam; HellForgeForm.OpenManager = open; SynchronizationContext.SetSynchronizationContext(context); }
    }
    static void Instructions(Action<bool, string> check)
    {
        using var setup = new HellForgeSetupForm(preview: true);
        var intro = Children(setup).OfType<Label>().Single(label => label.Text.Contains("Keep Helldivers 2"));
        foreach (var (name, text) in new[] { ("sync instructions", HellForgeForm.SyncInstructions), ("first setup screen", intro.Text) })
        {
            check(text.Contains("Keep Helldivers 2 installed in Steam"), name + " requires keeping the Steam game installed");
            check(text.Contains("Steam phone app") && text.Contains("Official setup (fallback)"), name + " explains Steam sign-in choices");
            check(text.Contains("administrator approval two or three times") && text.Contains("about 70 GB"), name + " explains Windows approvals and real storage needs");
        }
    }
    static void States(string area, Action<bool, string> check)
    {
        var root = Path.Combine(area, "forge-clarity"); Directory.CreateDirectory(Path.Combine(root, "app", "cli"));
        File.WriteAllText(Path.Combine(root, "hellforge-install.json"), "{\"schemaVersion\":1}");
        File.WriteAllText(Path.Combine(root, "app", "cli", "HellForge.Cli.exe"), "fixture, never executed");
        File.WriteAllText(Path.Combine(root, "HellForge.exe"), "fixture, never executed");
        using var form = new HellForgeForm(new HellForgePreferences { InstallRoot = root, Mode = "regular" }, migrationNotice: "Updated to the new launcher.");
        var message = Field<Label>(form, "message"); var detail = Field<Label>(form, "detail"); var state = Field<Label>(form, "state");
        // Detach this label so Visible reflects its own flag without showing the form (which starts live polling).
        message.Parent!.Controls.Remove(message);
        using var detached = message;
        var mods = HellForgePreset.Required.Select(item => new HellForgeMod(item.Id, item.Name, true, "fixture", [], item.Version,
            item.Id == HellForgePreset.GuardDogs ? [new("Smarter Guard Dogs and Sentries", true), new("Guard Dogs", true), new("Sentries", true), new("Intelligence", true), new("Safety", true), new("Sentry Safety", true), new("SEAF Safety", true), new("Targeting Marker", false)] : null)).ToList();
        mods.Add(new("skyeshade.vanilla-plus", "Vanilla Plus", true, "fixture", []));
        mods.Sort((a, b) => HellForgeGameplayCatalog.Pinned.Order.IndexOf(a.Id).CompareTo(HellForgeGameplayCatalog.Pinned.Order.IndexOf(b.Id)));
        var ready = new HellForgeStatus(root, HellForgeRelease.Version, HellForgePack.SupportedBuild, true, true, false, true, true, null, null);
        Set(form, "lastStatus", ready); Set(form, "lastMods", new HellForgeMods(mods, [], null));
        Set(form, "lastAccount", new HellForgeAccountState(true, true, "Imported")); View(form);
        check(state.Text == "Ready to launch" && message.Text == "" && !detail.Text.Contains("Updated to"), "startup update notice clears when the live headline changes");
        Set(form, "working", true); message.Text = "Preparing your pack"; View(form);
        check(message.Visible, "operation progress remains visible while working");
        Set(form, "working", false); message.Text = "Your Star Wars pack is installed."; View(form);
        check(detail.Text == message.Text && !message.Visible, "completed operation text appears once in the detail line");
        Set(form, "lastStatus", ready with { LastStatus = "failed", LastFailure = "The last game exited unexpectedly (code 42)" }); View(form);
        check(state.Text == "Last session needs attention" && message.Text == "" && detail.Text.Contains("Session details"), "a crash replaces stale success text with crash guidance");
        Set(form, "lastStatus", ready); View(form); message.Text = "Your session ended."; View(form);
        Set(form, "launcherRelease", new AppRelease { Version = "999.0.0" }); View(form);
        check(state.Text.Contains("Update available") && message.Text == "" && detail.Text.Contains("restart"), "a launcher update offer clears stale session text");
        message.Text = "Downloaded the launcher."; Set(form, "launcherReplaced", true); View(form);
        check(state.Text == "Update installed" && message.Text == "" && detail.Text.Contains("Reopen"), "an installed launcher update keeps its restart instruction");
        Set(form, "launcherRelease", null); Set(form, "captureBlock", "Account sync cleanup is pending"); View(form);
        var primary = Field<Button>(form, "launch");
        check(primary.Text == "OPEN HELLFORGE" && primary.Enabled && !Field<bool>(form, "detailsAction") && detail.Text.Contains("close it"),
            "blocked account capture offers HellForge and explains the close-window fallback");
        string? opened = null; HellForgeForm.OpenManager = path => opened = path;
        typeof(Button).GetMethod("OnClick", Members)!.Invoke(primary, [EventArgs.Empty]);
        check(opened == root, "the capture-block primary action actually opens or activates the selected Forge manager");
        HellForgeForm.OpenManager = _ => throw new InvalidOperationException("Cannot activate fixture window");
        typeof(Button).GetMethod("OnClick", Members)!.Invoke(primary, [EventArgs.Empty]);
        check(detail.Text == "Close the HellForge window, then try account sync again.", "failed manager activation gives an actionable close-window instruction");
    }
}
