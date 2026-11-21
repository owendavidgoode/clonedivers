using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        // 8. PreviewForgeHost covers all 13 presets cleanly without throwing
        foreach (var preset in PreviewForgeHost.AllPresets)
        {
            var host = new PreviewForgeHost(preset);
            var home = host.GetHomeState();
            var setup = host.GetSetupState();
            var diag = await host.GetDiagnosticsAsync();
            check(!string.IsNullOrWhiteSpace(home.Headline) && !string.IsNullOrWhiteSpace(setup.Headline) && diag.HealthChecks.Count > 0,
                $"PreviewForgeHost preset '{preset}' produces complete Home/Setup/Diagnostics state");
        }

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
}
