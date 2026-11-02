namespace Clonedivers;

internal static partial class LinuxProgram
{
    static TerminalView Dashboard()
    {
        var ready = GameLocator.IsGameDir(gameDir); var running = false; var gameState = "UNKNOWN"; var pending = false; var recoverable = false; var universe = "not installed"; var profile = "not selected"; var version = "not recorded";
        InstallReceipt? receipt = null;
        try
        {
            running = Game.IsRunning();
            gameState = running ? "RUNNING" : "CLOSED";
            if (gameDir is not null)
            {
                pending = Pack.HasPendingUpdate(gameDir, PlanPath);
                recoverable = pending && RecoveryTarget.Read(gameDir, PlanPath) is not null;
                var state = ModFiles.GetState(gameDir); receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir), gameDir);
                universe = state is ModState.Off or ModState.NoModFiles ? "Helldivers (vanilla)" : receipt?.Options.GetValueOrDefault("empire") == true ? "EmpireDivers" : "Clonedivers";
                version = receipt?.PackVersion ?? "unrecorded — install/update to verify"; profile = receipt?.TextureProfile ?? "unknown";
            }
        }
        catch (Exception ex) { ready = false; terminal!.Notice = "Folder inspection failed: " + ex.Message; }
        var canChange = ready && !running; var canInstall = canChange && !recoverable; var canLaunch = canChange && !pending;
        var summary = new[]
        {
            $"CURRENT UNIVERSE  {universe}   |   GAME  {gameState}",
            $"PACK  {version}   |   INSTALLED TEXTURES  {profile}",
            $"NEXT OPTIONS  Droids {(settings.Options.GetValueOrDefault("droids", true) ? "ON" : "OFF")} · Covenant {(settings.Options.GetValueOrDefault("covenant") ? "ON" : "OFF")} · Potato {(settings.TextureProfile is not (null or "full") ? "ON" : "OFF")}",
            "FOLDER  " + (gameDir ?? "Not found — choose Change game folder"),
            pending ? recoverable ? "RECOVERY  Finish the interrupted update before launching or switching." : "RECOVERY  Unfinished files found. Install/update or repair to restore them." : "Choose an action below. Options apply on install/update.",
            terminal!.Notice.Length == 0 ? "Ready." : "LAST ACTION  " + terminal.Notice
        };
        var unavailable = running ? "Close Helldivers 2 before changing files." : !ready ? "Choose a valid game folder first." : "Finish the interrupted update first.";
        return new TerminalView("YOUR SQUAD LAUNCHER", summary, new[]
        {
            new TerminalItem("1", "Install / update Clonedivers", canInstall ? "Verify, reuse cached files and enable the Republic pack." : unavailable, canInstall),
            new TerminalItem("2", "Install / update EmpireDivers", canInstall ? "Verify, reuse cached files and enable the Imperial pack." : unavailable, canInstall),
            new TerminalItem("3", "Switch to vanilla Helldivers", canLaunch ? "Park mods safely before playing the original game." : unavailable, canLaunch),
            new TerminalItem("4", "Pack options", "Checkboxes for skins, Covenant and texture streaming; save or discard."),
            new TerminalItem("5", "Check & repair", canInstall && receipt is not null ? "Freshly verify the installed selection and preserve its active/vanilla state." : receipt is null ? "Install/update once to record an installed selection." : unavailable, canInstall && receipt is not null),
            new TerminalItem("6", "Finish interrupted update", recoverable ? "Resume the exact saved update target." : pending ? "Use install/update or repair to rebuild orphaned files." : "There is no interrupted update to finish.", canChange && recoverable),
            new TerminalItem("7", "Launch " + universe, canLaunch ? "Start the current installed universe through Steam." : unavailable, canLaunch),
            new TerminalItem("f", "Change game folder", "Paste a folder from Steam → Manage → Browse local files.", !running),
            new TerminalItem("8", "Save support report", "Save a local ZIP with system details and available logs. No upload."),
            new TerminalItem("9", "Session recording", "Setup, arm, cancel or inspect optional MangoHud/Proton recording.", ready),
            new TerminalItem("0", "Quit", "Return to your terminal.")
        });
    }

    static async Task Menu()
    {
        string? last = null;
        while (true)
        {
            string? choice;
            try { choice = await terminal!.SelectAsync(Dashboard, cancellation.Token, last); }
            catch (OperationCanceledException) { return; }
            if (choice is null) return;
            last = choice;
            try
            {
                switch (choice)
                {
                    case "1": await Command("install", "clone"); terminal.Notice = "Clonedivers is ready."; break;
                    case "2": await Command("install", "empire"); terminal.Notice = "EmpireDivers is ready."; break;
                    case "3": await Command("vanilla", null); terminal.Notice = "Vanilla selected; mod files are parked."; break;
                    case "4": await Options(); break;
                    case "5": await Command("repair", null); terminal.Notice = "Repair complete; installed mode preserved."; break;
                    case "6": await Command("finish", null); terminal.Notice = "Interrupted update finished."; break;
                    case "7": await Command("launch", null); terminal.Notice = "Launch request sent to Steam."; break;
                    case "f": await ChangeFolder(); break;
                    case "8":
                        var path = LinuxSupport.SaveReport(gameDir, settings);
                        await terminal.ShowAsync("SUPPORT REPORT SAVED", "Saved locally: " + path + "\nReview before sharing. No data was uploaded.", cancellation.Token);
                        terminal.Notice = "Support report saved: " + path; break;
                    case "9": await RecordingMenu(); break;
                }
            }
            catch (OperationCanceledException)
            {
                terminal!.Notice = choice is "1" or "2" or "5" or "6" ? "Cancelled. Partial downloads are retained; retry when ready." : "Returned to dashboard. Saved files and recording requests were retained.";
                ResetCancellation();
            }
            catch (Exception ex)
            {
                LinuxSupport.Record("error", ex.GetType().Name + ": " + ex.Message); terminal!.Notice = ex.Message;
                try { await terminal.ShowAsync("ACTION NEEDS ATTENTION", ex.Message, cancellation.Token); }
                catch (OperationCanceledException) { ResetCancellation(); }
            }
        }
    }

    static void ResetCancellation()
    {
        var previous = cancellation; cancellation = new CancellationTokenSource(); previous.Dispose();
    }

    static async Task ChangeFolder()
    {
        var input = await terminal!.ReadPathAsync(gameDir, cancellation.Token);
        if (string.IsNullOrWhiteSpace(input)) { terminal.Notice = "Folder selection cancelled."; return; }
        var path = TerminalUi.NormalizePathInput(input, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var normalized = GameLocator.NormalizeGameDir(path) ?? throw new DirectoryNotFoundException("That folder does not contain data and bin/helldivers2.exe. In Steam, use Manage → Browse local files.");
        gameDir = normalized; settings.ForgetInstallationForDifferentGame(gameDir); settings.GamePath = gameDir; settings.Save();
        terminal.Notice = "Game folder saved.";
    }

    static async Task Options()
    {
        var pack = await Feed();
        var pending = new Dictionary<string, bool>(settings.Options, StringComparer.OrdinalIgnoreCase);
        var visible = PackExtras.Visible(pack).ToArray();
        var potato = PackProfiles.Potato(pack);
        string profile;
        try { profile = settings.ProfileFor(pack); } catch (InvalidDataException) { profile = "full"; }
        string? selected = null;
        while (true)
        {
            var rows = visible.Select((o, i) => new TerminalItem((i + 1).ToString(), $"[{(pending.GetValueOrDefault(o.Id, o.Default) ? "x" : " ")}] {o.Name}", o.Description, Toggle: true)).ToList();
            if (potato is not null) rows.Add(new TerminalItem((visible.Length + 1).ToString(), $"[{(profile != "full" ? "x" : " ")}] {PackProfiles.PotatoName}", "Stream full-detail textures as needed; does not guarantee higher FPS.", Toggle: true));
            rows.Add(new TerminalItem("s", "Save options", "Save these choices for the next install/update. Installed files are unchanged."));
            rows.Add(new TerminalItem("0", "Back without saving", "Discard edits from this options screen."));
            selected = await terminal!.SelectAsync(() => new TerminalView("PACK OPTIONS", new[] { terminal.KeyboardNavigation ? "Space or Enter toggles a checkbox. Save commits your choices." : "Type an option number to toggle; s saves and 0 discards.", "Changes do not alter installed files until install/update.", "Texture profile: " + profile }, rows), cancellation.Token, selected);
            if (selected is null) { terminal.Notice = "Option edits discarded; saved choices retained."; return; }
            if (selected == "s")
            {
                settings.Options = pending; settings.TextureProfile = profile; settings.Save(); terminal.Notice = "Options saved. Install/update your universe to apply them."; return;
            }
            if (int.TryParse(selected, out var index))
            {
                if (index >= 1 && index <= visible.Length) { var option = visible[index - 1]; pending[option.Id] = !pending.GetValueOrDefault(option.Id, option.Default); }
                else if (index == visible.Length + 1 && potato is not null) profile = profile == "full" ? potato.Id : "full";
            }
        }
    }

    static async Task RecordingMenu()
    {
        var choice = await terminal!.SelectAsync(() => new TerminalView("SESSION RECORDING", new[] { "Optional MangoHud + Proton logs for one Steam launch.", "Desktop recording only; Snap Vulkan capture can be unavailable." }, new[]
        {
            new TerminalItem("1", "One-time Steam setup", "Print the per-game launch wrapper to copy into Steam."),
            new TerminalItem("2", "Record next session", "Arm one launch; normal launches afterward remain unchanged."),
            new TerminalItem("3", "Cancel pending recording", "Keep existing captured sessions."),
            new TerminalItem("4", "Show capture result", "Check the exit code and whether MangoHud produced a CSV."),
            new TerminalItem("0", "Back", "Return to the launcher.")
        }), cancellation.Token);
        var command = choice switch { "1" => "record-setup", "2" => "record", "3" => "record-cancel", "4" => "record-status", _ => null };
        if (command is null) return;
        using var capture = new StringWriter(); var original = Console.Out;
        try { Console.SetOut(capture); RecordingCommand(command); } finally { Console.SetOut(original); }
        await terminal.ShowAsync("RECORDING", capture.ToString(), cancellation.Token);
        terminal.Notice = "Recording action complete; use Show capture result after playing.";
    }
}
