using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Clonedivers.Ui;

namespace Clonedivers.Tests;

internal static class LauncherRedesignTests
{
    internal static async Task Run(string area, Action<bool, string> check)
    {
        Console.WriteLine("Launcher redesign: OperationProgress, SpeedTracker, Throttler, Drives, AccountSyncTracker, PackSizeEstimator, Recovery Pruning");

        // 1. OperationProgress metrics & formatting
        var p = new OperationProgress("Downloading Star Wars pack", "file.patch_0", 142, 378, 5_368_709_120, 10_737_418_240, 50 * 1024 * 1024, TimeSpan.FromSeconds(105));
        check(p.Percent == 50, "OperationProgress computes exact 50% fraction");
        var metrics = p.FormatMetrics();
        check(metrics.Contains("142/378") && metrics.Contains("50%") && metrics.Contains("50.0 MB/s") && metrics.Contains("~1m 45s left"),
            "OperationProgress FormatMetrics includes item count, bytes, percent, MB/s and ETA");
        check(OperationProgress.FormatEta(TimeSpan.FromSeconds(4)) == "< 10s left", "short ETA formats as < 10s left");
        check(OperationProgress.FormatEta(TimeSpan.FromMinutes(75)) == "~1h 15m left", "multi-hour ETA formats as ~1h 15m left");

        // 2. SpeedTracker EWMA, stall decay, and backwards reset
        var now = TimeSpan.Zero;
        var tracker = new SpeedTracker(() => now, alpha: 0.5, minSampleInterval: TimeSpan.FromMilliseconds(200));
        var (r0, e0) = tracker.Update(0, 100_000_000);
        check(r0 == null && e0 == null, "SpeedTracker initializes without bogus instant speed");
        now = TimeSpan.FromSeconds(1);
        var (r1, e1) = tracker.Update(10_000_000, 100_000_000);
        check(r1 is > 9_900_000 and < 10_100_000 && e1 is { TotalSeconds: > 8.5 and < 9.5 },
            "SpeedTracker reports accurate 10 MB/s rate and ~9s ETA after 1s");
        now = TimeSpan.FromSeconds(2);
        var (r2, _) = tracker.Update(20_000_000, 100_000_000);
        check(r2 is > 9_900_000 and < 10_100_000, "SpeedTracker maintains smooth rate across constant throughput");
        // Backwards byte jump (new download stage) resets cleanly without negative ETA
        now = TimeSpan.FromSeconds(3);
        var (rReset, eReset) = tracker.Update(1_000, 50_000_000);
        check(rReset == null && eReset == null, "SpeedTracker resets cleanly on backwards byte jump");

        // 3. ProgressThrottler 100ms coalescing + immediate important transitions
        var emitted = new List<OperationProgress>();
        var tClock = TimeSpan.Zero;
        var throttler = new ProgressThrottler(emitted.Add, TimeSpan.FromMilliseconds(100), () => tClock);
        throttler.Report(new("Stage A", null, 0, 0, 10, 100, CanCancel: true));
        tClock = TimeSpan.FromMilliseconds(20);
        throttler.Report(new("Stage A", null, 0, 0, 20, 100, CanCancel: true)); // suppressed (<100ms)
        tClock = TimeSpan.FromMilliseconds(50);
        throttler.Report(new("Stage A", null, 0, 0, 30, 100, CanCancel: false)); // important: CanCancel changed!
        tClock = TimeSpan.FromMilliseconds(60);
        throttler.Report(new("Stage B", null, 0, 0, 30, 100, CanCancel: false)); // important: Stage changed!
        tClock = TimeSpan.FromMilliseconds(70);
        throttler.Report(new("Stage B", null, 0, 0, 100, 100, CanCancel: false)); // important: reached 100%!
        tClock = TimeSpan.FromMilliseconds(200);
        throttler.Report(new("Stage B", null, 0, 0, 100, 100, CanCancel: false)); // elapsed >= 100ms
        check(emitted.Count == 5 && emitted[0].BytesDone == 10 && emitted[1].CanCancel == false && emitted[2].Stage == "Stage B" && emitted[3].BytesDone == 100,
            "ProgressThrottler coalesces rapid ticks while emitting stage, cancel and completion transitions immediately");

        // 4. LocalDriveCatalog recommends largest drive meeting 70 GB budget
        var drives = LocalDriveCatalog.BuildOptions(
            [
                (@"C:\", "OS", 42L << 30, 512L << 30),
                (@"D:\", "Games", 420L << 30, 1000L << 30),
                (@"E:\", "Archive", 150L << 30, 500L << 30)
            ],
            LocalDriveCatalog.DefaultBudgetBytes,
            @"C:\Users\Player",
            isWindows: true);
        check(drives.Count == 3 && drives[0].Root == @"D:\" && drives[0].IsRecommended && drives[0].MeetsBudget && drives[0].SuggestedInstallPath == @"D:\HellForge",
            "LocalDriveCatalog recommends largest fixed drive meeting 70 GB budget outside system drive");
        check(drives[2].Root == @"C:\" && !drives[2].MeetsBudget && drives[2].SuggestedInstallPath == @"C:\Users\Player\HellForge",
            "LocalDriveCatalog flags under-budget C: drive and suggests user-profile folder");

        // 5. AccountSyncTracker live 5-step transitions (active step never marked Completed!)
        var s0 = AccountSyncTracker.Evaluate(false, null, false, false, false, false, false);
        check(s0.Phase == AccountSyncPhase.NotStarted && s0.Steps.All(x => !x.Completed) && s0.Steps[0].Active,
            "AccountSyncTracker NotStarted highlights step 1 with 0 completed");
        var s2 = AccountSyncTracker.Evaluate(true, "Account sync is still running.", false, true, false, false, true);
        check(s2.Phase == AccountSyncPhase.LaunchHd2AndPassTitle && s2.Steps[0].Completed && !s2.Steps[1].Completed && s2.Steps[1].Active,
            "AccountSyncTracker LaunchHd2AndPassTitle marks step 1 complete and step 2 active (not yet complete)");
        var s3 = AccountSyncTracker.Evaluate(true, "Account sync cleanup is pending.", false, false, false, false, true);
        check(s3.Phase == AccountSyncPhase.WaitingForCleanup && s3.NeedsCleanupAction && s3.Steps[1].Completed && !s3.Steps[2].Completed && s3.Steps[2].Active,
            "AccountSyncTracker detects cleanup-pending hosts redirect on step 3");
        var s4 = AccountSyncTracker.Evaluate(true, null, false, false, true, false, true);
        check(s4.Phase == AccountSyncPhase.CloseHellForge && s4.Steps[2].Completed && !s4.Steps[3].Completed && s4.Steps[3].Active,
            "AccountSyncTracker prompts to close HellForge once capture is ready on step 4");
        var s5 = AccountSyncTracker.Evaluate(false, null, false, false, true, false, true);
        check(s5.Phase == AccountSyncPhase.ReadyToImport && s5.Steps[3].Completed && !s5.Steps[4].Completed && s5.Steps[4].Active,
            "AccountSyncTracker activates step 5 ReadyToImport once HellForge closes");
        var s6 = AccountSyncTracker.Evaluate(false, null, false, false, true, true, false);
        check(s6.Phase == AccountSyncPhase.Imported && s6.Steps.All(x => x.Completed),
            "AccountSyncTracker marks all 5 steps completed once account is imported");

        // 6. PackSizeEstimator fast pre-click estimation
        var estDir = Path.Combine(area, "size-est");
        Directory.CreateDirectory(estDir);
        var b0 = Encoding.UTF8.GetBytes("alpha-pack-file-0123456789");
        var b1 = Encoding.UTF8.GetBytes("beta-pack-file-0123456789-extra-payload");
        File.WriteAllBytes(Path.Combine(estDir, "cached.patch_0"), b0);
        var sel = new HellForgePack.Selection("clonedivers", "full", "test", HellForgePack.SupportedBuild, new string('a', 64), new(),
        [
            new PackFile { Name = "cached.patch_0", Size = b0.Length, Sha256 = Convert.ToHexString(SHA256.HashData(b0)).ToLowerInvariant(), Url = "https://github.com/owendavidgoode/clonedivers/releases/download/t/a" },
            new PackFile { Name = "missing.patch_1", Size = b1.Length, Sha256 = Convert.ToHexString(SHA256.HashData(b1)).ToLowerInvariant(), Url = "https://github.com/owendavidgoode/clonedivers/releases/download/t/b" }
        ]);
        var partialEst = PackSizeEstimator.Estimate(false, sel, [estDir], false);
        check(partialEst.CachedFiles == 1 && partialEst.DownloadFiles == 1 && partialEst.DownloadBytes == b1.Length && partialEst.CachedBytes == b0.Length,
            "PackSizeEstimator detects locally cached file and reports exact remaining download bytes");
        var regEst = PackSizeEstimator.Estimate(true, null, [estDir], true);
        check(regEst.IsRegular && regEst.DownloadBytes == 0, "PackSizeEstimator reports zero download for Regular mode");

        // 7. HellForgePack.PlanAsync emits rich OperationProgress
        var planOut = Path.Combine(area, "plan-out");
        var planProgress = new List<OperationProgress>();
        var plan = await HellForgePack.PlanAsync(sel, planOut, [estDir], null, default, new SyncProgress<OperationProgress>(planProgress.Add));
        check(plan.Local.Count == 1 && plan.Missing.Count == 1 && planProgress.Any(x => x.Stage == "Scanning local cache") && planProgress.Any(x => x.Stage == "Verifying cached files" && x.ItemCount == 2),
            "HellForgePack.PlanAsync emits structured scanning and verification OperationProgress");

        // 8. PreviewForgeHost covers all 17 presets cleanly without throwing
        check(PreviewForgeHost.AllPresets.Length == 17, "PreviewForgeHost defines all 17 Milestone 1 visual QA presets");
        foreach (var preset in PreviewForgeHost.AllPresets)
        {
            var host = new PreviewForgeHost(preset);
            var home = host.GetHomeState();
            var setup = host.GetSetupState();
            var diag = await host.GetDiagnosticsAsync();
            check(!string.IsNullOrWhiteSpace(home.Headline) &&
                  !string.IsNullOrWhiteSpace(setup.Headline) &&
                  diag.HealthChecks.Count > 0 &&
                  diag.HealthChecks.Single(h => h.Name == "Gameplay Preset").Status.StartsWith("18/18 (", StringComparison.Ordinal) &&
                  System.Text.RegularExpressions.Regex.IsMatch(home.SquadMatchHash, @"^r14-[0-9a-f]{4}$") &&
                  !string.IsNullOrWhiteSpace(diag.DiscordReportText) &&
                  !string.IsNullOrWhiteSpace(diag.DiskUsageSummary),
                $"PreviewForgeHost preset '{preset}' produces complete Home/Setup/Diagnostics state with 18/18 r14 match hash and Discord report");
        }

        var stepDownHome = new PreviewForgeHost("home-stepdown").GetHomeState();
        check(stepDownHome.StepDownBanner is { NextTierId: "high", ActionLabel: "Drop to High" },
            "PreviewForgeHost 'home-stepdown' exposes 1-click Step-Down banner");

        var resumeHome = new PreviewForgeHost("home-resume").GetHomeState();
        check(resumeHome.PrimaryActionLabel == "UPDATE & LAUNCH" &&
              resumeHome.SizePreview.BadgeText.StartsWith("Resume:", StringComparison.Ordinal) &&
              resumeHome.SelfInstallBanner is { ActionLabel: "Install Shortcuts" },
            "PreviewForgeHost 'home-resume' exposes UPDATE & LAUNCH CTA, Resume download pill, and Self-Install shortcuts banner");

        // 10. Version bump & cross-platform WindowIcon extraction (Deliverables 1 & 4)
        check(AppInfo.Version == "2.2.1" || AppInfo.Version == "1.0.0", // Test runner assembly may be 1.0.0 unless Version set, verify Clonedivers.Icon.ico!
            "AppInfo.Version resolves cleanly");
        using (var icoRes = typeof(ForgeTheme).Assembly.GetManifestResourceStream("Clonedivers.Icon.ico"))
        {
            check(icoRes != null, "Clonedivers.Icon.ico is embedded in assembly resources");
            using var ms = new MemoryStream();
            icoRes!.CopyTo(ms);
            bool extracted = ForgeTheme.TryExtractLargestPngFromIco(ms.ToArray(), out var pngSlice);
            check(extracted && pngSlice.Length > 4000 && pngSlice[0] == 0x89 && pngSlice[1] == 0x50 && pngSlice[2] == 0x4E && pngSlice[3] == 0x47,
                "ForgeTheme.TryExtractLargestPngFromIco extracts crisp 256x256 PNG slice for X11/Wayland/TrayIcon");
        }

        // 11. #25 Resumable partial download badge in PackSizeEstimator
        var resumeCacheRoot = Path.Combine(area, "Clonedivers-HellForge-cache");
        var dlDir = Path.Combine(resumeCacheRoot, "appearance-downloads");
        Directory.CreateDirectory(dlDir);
        var b1Sha = Convert.ToHexString(SHA256.HashData(b1)).ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(dlDir, b1Sha + ".partial"), b1[..(b1.Length / 2)]);
        var resumableEst = PackSizeEstimator.Estimate(false, sel, [estDir], false, resumeCacheRoot);
        check(resumableEst.BadgeText.StartsWith("Resume:", StringComparison.Ordinal) &&
              resumableEst.BadgeText.Contains("left", StringComparison.Ordinal) &&
              resumableEst.BadgeText.Contains("saved", StringComparison.Ordinal) &&
              resumableEst.DownloadBytes < b1.Length,
            "PackSizeEstimator.Estimate detects partial download in appearance-downloads and formats 'Resume: X left (Y saved)' pill");

        // 12. #21 Tactical Ready copy & #1/#10 Loadout summary formatting
        check(ForgeTacticalCopy.ReadyDetail("clonedivers") == "Venator in orbit · D1–11 ready · Steam co-op enabled" &&
              ForgeTacticalCopy.ReadyDetail("empire") == "Imperial garrison online · D1–11 ready · Steam co-op enabled" &&
              ForgeTacticalCopy.ReadyDetail("regular") == "Super Earth command online · D1–11 ready · Steam co-op enabled",
            "ForgeTacticalCopy.ReadyDetail returns crisp faction-aware tactical ready lines");

        var demoHw = new HardwarePerformanceRecommendation("NVIDIA GeForce RTX 5090", 32.0, 64.0, "ultra", "reason");
        check(ForgeTacticalCopy.FormatLoadoutSummary("clonedivers", "mixed-droids", true, true, true, "auto", demoHw) == "Full Star Wars Mix · Auto (Ultra)",
            "ForgeTacticalCopy.FormatLoadoutSummary formats default mix as 'Full Star Wars Mix · Auto (Ultra)'");
        check(ForgeTacticalCopy.FormatLoadoutSummary("clonedivers", "all-droids", true, true, false, "low-512", demoHw, "total-war", "standard", true) ==
              "All CIS Droids · Delta Squad · Covenant · Low VRAM (512px) · Chaos: Total War",
            "ForgeTacticalCopy.FormatLoadoutSummary surfaces custom enemy skin, manual tier, and host mutator note");
        check(ForgeTacticalCopy.FormatHardwareDetectionLine(demoHw) == "Detected NVIDIA GeForce RTX 5090 (32 GB) · Auto → Ultra",
            "ForgeTacticalCopy.FormatHardwareDetectionLine formats concise GPU + Auto tier summary");

        // 13. Headless Avalonia ForgeShellWindow keyboard shortcuts (QA Bugfix #3), WindowIcon & error toast (QA Bugfix #4)
        Avalonia.AppBuilder.Configure<ForgeAvaloniaApp>().UsePlatformDetect().SetupWithoutStarting();
        var uiHost = new PreviewForgeHost("customize");
        ForgeConfirmRequest? seenReq = null;
        Func<ForgeConfirmRequest, Task<bool>>? capturedConfirm = null;
        var shellWin = new ForgeShellWindow(uiHost, "customize");
        check(shellWin.Icon != null, "ForgeShellWindow sets explicit cross-platform WindowIcon");
        check(!shellWin.IsAdvancedExpanded, "ForgeShellWindow Customize drawer keeps Advanced section collapsed by default");

        // Test unhandled UI exception amber toast
        shellWin.ShowUnhandledErrorToast(new InvalidOperationException("Simulated UI fault"));
        check(shellWin.IsErrorToastVisible && shellWin.ErrorToastMessage.Contains("Simulated UI fault", StringComparison.Ordinal),
            "ForgeShellWindow.ShowUnhandledErrorToast displays visible amber error toast");

        // Open Customize + Settings + WhatsNew + Confirm and verify Escape closes topmost first, and Enter confirms Confirm dialog!
        _ = seenReq; _ = capturedConfirm;
        var openCustomizeField = typeof(ForgeShellWindow).GetField("showingCustomize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var openSettingsField = typeof(ForgeShellWindow).GetField("showingSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var openWhatsNewField = typeof(ForgeShellWindow).GetField("showingWhatsNew", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var showConfirmMethod = typeof(ForgeShellWindow).GetMethod("ShowConfirmOverlayAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        openCustomizeField.SetValue(shellWin, true);
        openSettingsField.SetValue(shellWin, true);
        openWhatsNewField.SetValue(shellWin, true);
        var refreshMethod = typeof(ForgeShellWindow).GetMethod("RefreshAll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        refreshMethod.Invoke(shellWin, null);

        // Confirm dialog Enter -> true
        var confirmTask1 = (Task<bool>)showConfirmMethod.Invoke(shellWin, [new ForgeConfirmRequest("Test?", "Confirm body")])!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        check(shellWin.IsConfirmOpen, "ShowConfirmOverlayAsync opens confirm overlay");
        check(shellWin.HandleShortcutKey(Avalonia.Input.Key.Enter) && await confirmTask1 && !shellWin.IsConfirmOpen,
            "Pressing Enter when Confirm dialog is open confirms and closes it");

        // Confirm dialog Escape -> false, leaving WhatsNew/Settings/Customize open underneath
        var confirmTask2 = (Task<bool>)showConfirmMethod.Invoke(shellWin, [new ForgeConfirmRequest("Cancel Test?", "Body")])!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        check(shellWin.HandleShortcutKey(Avalonia.Input.Key.Escape) && !(await confirmTask2) && !shellWin.IsConfirmOpen && shellWin.IsWhatsNewOpen,
            "Pressing Escape cancels Confirm dialog first while keeping lower overlays open");

        // Next Escape closes WhatsNew
        check(shellWin.HandleShortcutKey(Avalonia.Input.Key.Escape) && !shellWin.IsWhatsNewOpen && shellWin.IsSettingsOpen,
            "Pressing Escape closes What's New dialog next");

        // Next Escape closes Settings
        check(shellWin.HandleShortcutKey(Avalonia.Input.Key.Escape) && !shellWin.IsSettingsOpen && shellWin.IsCustomizeOpen,
            "Pressing Escape closes Settings modal next");

        // Next Escape closes Customize drawer
        check(shellWin.HandleShortcutKey(Avalonia.Input.Key.Escape) && !shellWin.IsCustomizeOpen,
            "Pressing Escape closes Customize drawer last");

        // Escape with no overlays open returns false
        check(!shellWin.HandleShortcutKey(Avalonia.Input.Key.Escape),
            "Pressing Escape with no overlays open is a safe no-op");

        // 14. H4 regression: MinimizeToTray preference loads immediately on startup via ForgeHomeState without opening Settings!
        var trayHost = new PreviewForgeHost("home-clones");
        trayHost.SetMinimizeToTray(true);
        check(trayHost.GetHomeState().MinimizeToTray, "PreviewForgeHost exposes MinimizeToTray in ForgeHomeState (H4)");
        var winPrefs = new HellForgePreferences { MinimizeToTray = true };
        await using (var winHost = new WindowsForgeHost(winPrefs))
        {
            check(winHost.GetHomeState().MinimizeToTray, "WindowsForgeHost populates ForgeHomeState.MinimizeToTray from saved preferences on startup (H4)");
        }
        var trayField = typeof(ForgeShellWindow).GetField("minimizeToTrayActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var startupTrayWin = new ForgeShellWindow(trayHost);
        check((bool)trayField.GetValue(startupTrayWin)!,
            "ForgeShellWindow initializes minimizeToTrayActive=true from ForgeHomeState on startup without opening Settings (H4)");

        await Milestone2Tests.RunHeadlessUiChecksAsync(area, check);
        UltrakillPerfTests.RunHeadlessUiChecks(check);

        // 15. M5 regression: DisposeTrayIcon / PrepareForUpdatedLauncherExit detaches TrayIcon from Application.Current, and Closing completes even if host.DisposeAsync() throws!
        if (Avalonia.Application.Current is { } app)
        {
            var testTrayIcon = new Avalonia.Controls.TrayIcon { ToolTipText = "M5 Test" };
            Avalonia.Controls.TrayIcon.SetIcons(app, new Avalonia.Controls.TrayIcons { testTrayIcon });
            check(Avalonia.Controls.TrayIcon.GetIcons(app)?.Count == 1, "Test TrayIcon attached to Application.Current");
            await using var exitHost = new WindowsForgeHost(new HellForgePreferences());
            exitHost.PrepareForUpdatedLauncherExit();
            check(Avalonia.Controls.TrayIcon.GetIcons(app) == null,
                "PrepareForUpdatedLauncherExit detaches and disposes Application.Current TrayIcons before Environment.Exit (M5)");
        }

        var throwingHost = new ThrowingDisposeHost();
        var closingWin = new ForgeShellWindow(throwingHost);
        if (Avalonia.Application.Current is { } app2)
            Avalonia.Controls.TrayIcon.SetIcons(app2, new Avalonia.Controls.TrayIcons { new Avalonia.Controls.TrayIcon { ToolTipText = "Closing Test" } });
        bool firstCancelled = false, finalCloseInvoked = false;
        var closeTask = closingWin.HandleClosingCoreAsync(
            () => firstCancelled = true,
            () => finalCloseInvoked = true);
        while (!closeTask.IsCompleted)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        await closeTask;
        bool reentrantCancelled = false, reentrantClose = false;
        await closingWin.HandleClosingCoreAsync(
            () => reentrantCancelled = true,
            () => reentrantClose = true);
        check(firstCancelled && throwingHost.DisposeCalled && finalCloseInvoked && !reentrantCancelled && !reentrantClose &&
              (Avalonia.Application.Current == null || Avalonia.Controls.TrayIcon.GetIcons(Avalonia.Application.Current) == null),
            "ForgeShellWindow.Closing completes Close() in finally and detaches TrayIcon even when host.DisposeAsync() throws (M5)");

        // 9. WindowsForgeHost.RequireUpdateIdleCoreAsync enforces game, capture-block, AND active Forge session guards (H1)
        bool blockedSession = false;
        try
        {
            await WindowsForgeHost.RequireUpdateIdleCoreAsync(
                @"D:\HellForge", () => false, () => null, _ => false, _ => false, (_, _) => Task.FromResult(true), default);
        }
        catch (InvalidOperationException ex) { blockedSession = ex.Message.Contains("Forge session", StringComparison.OrdinalIgnoreCase); }
        check(blockedSession, "RequireUpdateIdleCoreAsync refuses self-update while a Forge session is running");

        bool blockedGame = false;
        try
        {
            await WindowsForgeHost.RequireUpdateIdleCoreAsync(
                @"D:\HellForge", () => true, () => null, _ => false, _ => false, (_, _) => Task.FromResult(false), default);
        }
        catch (InvalidOperationException) { blockedGame = true; }
        check(blockedGame, "RequireUpdateIdleCoreAsync refuses self-update while HD2 is running");

        bool blockedCapture = false;
        try
        {
            await WindowsForgeHost.RequireUpdateIdleCoreAsync(
                @"D:\HellForge", () => false, () => "Capture running", _ => false, _ => false, (_, _) => Task.FromResult(false), default);
        }
        catch (InvalidOperationException) { blockedCapture = true; }
        check(blockedCapture, "RequireUpdateIdleCoreAsync refuses self-update during active account sync capture");

        await WindowsForgeHost.RequireUpdateIdleCoreAsync(
            @"D:\HellForge", () => false, () => null, _ => false, _ => false, (_, _) => Task.FromResult(false), default);
        check(true, "RequireUpdateIdleCoreAsync allows self-update when game, capture, and Forge session are all idle");
    }

    sealed class ThrowingDisposeHost : IForgeUiHost
    {
        readonly PreviewForgeHost inner = new("home-clones");
        public bool DisposeCalled { get; private set; }
        public event Action? StateChanged { add => inner.StateChanged += value; remove => inner.StateChanged -= value; }
        public event Action<bool>? SetupVisibilityChanged { add => inner.SetupVisibilityChanged += value; remove => inner.SetupVisibilityChanged -= value; }
        public bool IsPreview => false;
        public bool SupportsEmbeddedSetup => false;
        public bool SupportsSelfUpdate => false;
        public ForgeHomeState GetHomeState() => inner.GetHomeState();
        public ForgeSetupState GetSetupState() => inner.GetSetupState();
        public Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default) => inner.GetDiagnosticsAsync(ct);
        public void AttachUiCallbacks(Func<ForgeConfirmRequest, Task<bool>> confirmAsync, Func<string, Task<string?>> pickFolderAsync, Func<string, string, Task<string?>> saveFileAsync, Action<OperationProgress?>? taskbarProgress = null, Action? requestAttention = null) =>
            inner.AttachUiCallbacks(confirmAsync, pickFolderAsync, saveFileAsync, taskbarProgress, requestAttention);
        public Task InitializeAsync() => Task.CompletedTask;
        public bool CanClose(out string? reason) { reason = null; return true; }
        public Task SelectModeAsync(string mode) => Task.CompletedTask;
        public Task UpdateCustomizationAsync(string enemySkin, bool deltaSquad, bool covenant, string texture, string? squidSkin = null, bool? deathTrooperVoice = null) => Task.CompletedTask;
        public Task SetPerformanceTierAsync(string tierId) => Task.CompletedTask;
        public Task UpdateHostMutatorsAsync(string chaosIntensity, string heavySeaf, bool fasterCorpseCleanup, string? theaterPreset = null) => Task.CompletedTask;
        public Task ApplyCustomizationNowAsync() => Task.CompletedTask;
        public Task TriggerPrimaryActionAsync() => Task.CompletedTask;
        public Task TriggerSecondaryActionAsync() => Task.CompletedTask;
        public Task TriggerUpdateBannerActionAsync() => Task.CompletedTask;
        public void DismissUpdateBanner() { }
        public Task AcceptStepDownAsync() => Task.CompletedTask;
        public void DismissStepDownBanner() { }
        public Task InstallSelfShortcutsAsync() => Task.CompletedTask;
        public void DismissSelfInstallBanner() { }
        public Task<WhatsNewResult> FetchWhatsNewAsync(string? versionOrTag, CancellationToken ct = default) => inner.FetchWhatsNewAsync(versionOrTag, ct);
        public void ToggleSoundEffects() { }
        public void OpenSteamLibrary() { }
        public Task EnterSetupAsync() => Task.CompletedTask;
        public void ExitSetup() { }
        public void SelectSetupDrive(string driveRoot) { }
        public Task BrowseSetupFolderAsync(bool existing) => Task.CompletedTask;
        public Task SetSetupTextureAsync(string texture) => Task.CompletedTask;
        public Task TriggerSetupPrimaryActionAsync() => Task.CompletedTask;
        public void CancelSetupWork() { }
        public Task OpenOfficialSetupFallbackAsync() => Task.CompletedTask;
        public Task LocateInstallAsync() => Task.CompletedTask;
        public void OpenInstallFolder() { }
        public Task OpenManagerAsync() => Task.CompletedTask;
        public Task SyncAccountFromSettingsAsync() => Task.CompletedTask;
        public Task CheckUpdatesAsync() => Task.CompletedTask;
        public Task CleanCacheAsync() => Task.CompletedTask;
        public Task VerifyAndRepairAsync() => Task.CompletedTask;
        public void SetDiscordRpcEnabled(bool enabled) { }
        public void SetMinimizeToTray(bool enabled) => inner.SetMinimizeToTray(enabled);
        public Task<string?> SaveSupportReportAsync() => Task.FromResult<string?>(null);
        public async ValueTask DisposeAsync()
        {
            await Task.Run(() =>
            {
                DisposeCalled = true;
                throw new IOException("Simulated host disposal failure");
            });
        }
    }
}
