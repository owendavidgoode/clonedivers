using System.Text.Json;

namespace Clonedivers;

public sealed record ForgeVerifyStepResult(
    int StepNumber,
    string Name,
    bool Passed,
    bool Repaired,
    string Detail);

public sealed record ForgeVerifyReport(
    bool Healthy,
    IReadOnlyList<ForgeVerifyStepResult> Steps,
    int GameplayModsRepaired,
    bool PackVerified,
    bool PackRepaired,
    long BytesReclaimed,
    string Summary);

/// <summary>
/// Deterministic 5-step 1-Click Verify &amp; Repair service:
/// (1) Forge install &amp; build 25480438 check,
/// (2) Hosts / capture cleanup check,
/// (3) 18-mod r9 gameplay preset hash &amp; order verification/repair,
/// (4) Active pack integrity check &amp; repair,
/// (5) Stale cache cleanup — never touching saves, profiles, or account capture data.
/// </summary>
public static class ForgeVerifier
{
    public static Task<ForgeVerifyReport> VerifyAndRepairAsync(
        string installRoot,
        HellForgePreferences prefs,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default) =>
        VerifyAndRepairCoreAsync(installRoot, prefs, progress, ct);

    public static async Task<ForgeVerifyReport> VerifyAndRepairCoreAsync(
        string installRoot,
        HellForgePreferences prefs,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default,
        IHellForgeCommands? commands = null,
        Func<string?>? captureBlockCheck = null,
        Func<string, IEnumerable<GameplayArtifact>, IProgress<OperationProgress>?, CancellationToken, Task>? stageGameplayOverride = null,
        Func<HellForgePack.Plan, bool, IProgress<OperationProgress>?, CancellationToken, Task>? exportPackOverride = null,
        Func<HellForgePack.Selection>? selectionOverride = null,
        IEnumerable<string>? extraSearchFolders = null)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        prefs.Validate();
        ct.ThrowIfCancellationRequested();

        if (Game.IsRunning())
            throw new InvalidOperationException("Close Helldivers 2 before running Verify & Repair.");

        var steps = new List<ForgeVerifyStepResult>(5);
        int gameplayRepairedCount = 0;
        bool packVerified = false;
        bool packRepaired = false;
        long bytesReclaimed = 0;

        // ── Step 1/5: Forge install & build 25480438 check ──
        progress?.Report(new("Verifying Forge installation", "Checking HellForge 0.4.0-beta and game build 25480438…", 1, 5, CanCancel: true));
        string validatedRoot;
        HellForgeClient client;
        HellForgeStatus status;
        bool step1Repaired = false;

        try
        {
            validatedRoot = ValidateForgeInstallRoot(installRoot, requireWindowsBinaries: commands == null && OperatingSystem.IsWindows());
            client = commands != null
                ? new HellForgeClient(validatedRoot, commands) { RequireCliQuiescent = () => { } }
                : new HellForgeClient(validatedRoot);
            status = await client.StatusAsync(ct);

            if (status.Running)
            {
                steps.Add(new(1, "Forge install & game build", false, false, "A HellForge session is currently running. End the session before repairing."));
                return FailEarly(steps, "End the running Forge session before running Verify & Repair.");
            }
            if (status.Build != HellForgePack.SupportedBuild)
            {
                steps.Add(new(1, "Forge install & game build", false, false, $"Found game build '{status.Build}'; Clonedivers requires build {HellForgePack.SupportedBuild}."));
                return FailEarly(steps, $"Game build mismatch ({status.Build} vs {HellForgePack.SupportedBuild}). Update HellForge's game copy first.");
            }
            if (status.CompatibilityProblem != null)
            {
                steps.Add(new(1, "Forge install & game build", false, false, status.CompatibilityProblem));
                return FailEarly(steps, status.CompatibilityProblem);
            }
            if (!status.GameCopyReady || !status.RunnerReady)
            {
                steps.Add(new(1, "Forge install & game build", false, false, "HellForge game copy or session runner is not ready yet."));
                return FailEarly(steps, "Finish initial game copy setup before running Verify & Repair.");
            }

            if (client.InterruptedGameplayProblem() != null)
            {
                await client.RecoverGameplayAsync(ct);
                step1Repaired = true;
            }
            if (client.InterruptedPackProblem() != null)
            {
                await client.RecoverInterruptedPackAsync(ct);
                step1Repaired = true;
            }

            steps.Add(new(1, "Forge install & game build", true, step1Repaired,
                step1Repaired
                    ? $"Recovered interrupted operation; Forge {status.Version} & build {HellForgePack.SupportedBuild} ready."
                    : $"HellForge {status.Version} & game build {HellForgePack.SupportedBuild} verified."));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            steps.Add(new(1, "Forge install & game build", false, false, ex.Message));
            return FailEarly(steps, "Forge installation check failed: " + ex.Message);
        }

        // ── Step 2/5: Hosts / capture cleanup check (read-only — never touches profiles/ or account-snapshot/) ──
        ct.ThrowIfCancellationRequested();
        progress?.Report(new("Checking network & sync state", "Inspecting hosts file and capture cleanup…", 2, 5, CanCancel: true));
        string? captureIssue;
        try
        {
            captureIssue = (captureBlockCheck ?? HellForgeAccount.CaptureBlock)();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            captureIssue = "Could not read system hosts file to confirm capture cleanup: " + ex.Message;
        }

        if (captureIssue != null)
        {
            steps.Add(new(2, "Hosts & capture cleanup", false, false, captureIssue));
            return FailEarly(steps, captureIssue);
        }

        if (HellForgeAccount.SessionShimsActive(validatedRoot))
        {
            const string shimMsg = "Session SDK shims are still active in temp/sdk-shim-originals. Wait a moment for session cleanup to finish.";
            steps.Add(new(2, "Hosts & capture cleanup", false, false, shimMsg));
            return FailEarly(steps, shimMsg);
        }

        steps.Add(new(2, "Hosts & capture cleanup", true, false, "Hosts file and capture proxy state are clean (saves & account untouched)."));

        // ── Step 3/5: 18-mod r11 gameplay preset hash & order verification/repair ──
        ct.ThrowIfCancellationRequested();
        progress?.Report(new("Verifying gameplay preset", "Checking 18-mod r11 hashes, options, and load order…", 3, 5, CanCancel: true));
        try
        {
            var modsBefore = await client.ModsAsync(ct);
            var activeMode = prefs.IsRegular ? "regular" : prefs.PackMode;
            var missing = await client.MissingGameplayAsync(activeMode, ct);
            var presetProblem = HellForgePreset.Problem(modsBefore, checkOrder: true) ?? modsBefore.HostingProblem;

            if (missing.Count > 0 || presetProblem != null)
            {
                if (missing.Count > 0)
                {
                    if (stageGameplayOverride != null)
                        await stageGameplayOverride(client.GameplayCache, missing, progress, ct);
                    else
                        await HellForgeGameplayCatalog.StageAsync(client.GameplayCache, missing, null, ct, richProgress: progress);
                }
                else
                {
                    Directory.CreateDirectory(client.GameplayCache);
                }

                await client.InstallGameplayAsync(client.GameplayCache, null, progress, ct, activeMode);
                gameplayRepairedCount = Math.Max(missing.Count, 1);

                var modsAfter = await client.ModsAsync(ct);
                var afterProblem = HellForgePreset.Problem(modsAfter, checkOrder: true) ?? modsAfter.HostingProblem;
                if (afterProblem != null)
                {
                    steps.Add(new(3, "Gameplay preset (r8)", false, false, afterProblem));
                    return FailEarly(steps, afterProblem, gameplayRepairedCount);
                }

                steps.Add(new(3, "Gameplay preset (r8)", true, true,
                    missing.Count > 0
                        ? $"Repaired {missing.Count} gameplay archive(s) and restored pinned r8 order & options."
                        : "Restored pinned r8 gameplay load order, Guard Dogs options, and sharing settings."));
            }
            else
            {
                steps.Add(new(3, "Gameplay preset (r8)", true, false, "All r8 gameplay mods, hashes, options, and load order verified."));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            steps.Add(new(3, "Gameplay preset (r8)", false, false, ex.Message));
            return FailEarly(steps, "Gameplay preset repair failed: " + ex.Message, gameplayRepairedCount);
        }

        // ── Step 4/5: Active pack integrity check & repair ──
        ct.ThrowIfCancellationRequested();
        progress?.Report(new("Verifying active pack", prefs.IsRegular ? "Checking Regular mode configuration…" : "Hashing Star Wars pack archives…", 4, 5, CanCancel: true));
        try
        {
            var catalog = await client.ModsAsync(ct);
            if (prefs.IsRegular)
            {
                var currentStatus = await client.StatusAsync(ct);
                bool regularClean = !catalog.Mods.Any(m => m.Enabled && HellForgePack.IsStarWarsOverhaulMod(m)) &&
                                    HellForgePreset.ModeCompanionProblem(catalog, "regular") == null &&
                                    catalog.Mods.Any(m => m.Id == HellForgeClient.DifficultyId && m.Enabled) &&
                                    currentStatus.Multiplayer && currentStatus.LocalBackend;
                if (!regularClean)
                {
                    await client.PrepareRegularAsync(ct);
                    packRepaired = true;
                }
                HellForgeClient.RequireRegular(await client.ModsAsync(ct));
                packVerified = true;
                steps.Add(new(4, "Active pack integrity", true, packRepaired,
                    packRepaired ? "Disabled Star Wars packs and restored Regular co-op configuration." : "Regular mode verified cleanly."));
            }
            else
            {
                var expectedSelection = selectionOverride != null ? selectionOverride() : prefs.Selection();
                var expectedPackId = HellForgePack.PackId(expectedSelection.Mode);
                var activePack = catalog.Mods.SingleOrDefault(m => m.Enabled && m.Id == expectedPackId);
                bool needsRepair = activePack == null || catalog.Mods.Count(m => m.Enabled && HellForgePack.IsStarWarsOverhaulMod(m)) != 1 ||
                    HellForgePreset.ModeCompanionProblem(catalog, expectedSelection.Mode) != null;

                if (!needsRepair && activePack != null)
                {
                    try
                    {
                        var verifySpeed = new SpeedTracker();
                        await HellForgePack.VerifyAsync(activePack.Folder, expectedSelection, new SyncProgress<(long Done, long Total)>(p =>
                        {
                            var (rate, eta) = verifySpeed.Update(p.Done, p.Total);
                            progress?.Report(new("Verifying active pack", null, 4, 5, p.Done, p.Total, rate, eta, CanCancel: true));
                        }), ct);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
                    {
                        needsRepair = true;
                    }
                }

                if (needsRepair)
                {
                    var exportsRoot = Path.Combine(client.CacheRoot, "exports");
                    Directory.CreateDirectory(exportsRoot);
                    string? reusedExport = null;
                    if (exportPackOverride == null)
                    {
                        var candidates = new List<string>();
                        if (prefs.PreparedPack is { } saved && Directory.Exists(saved)) candidates.Add(saved);
                        FileSafety.RejectLink(exportsRoot);
                        candidates.AddRange(new DirectoryInfo(exportsRoot).EnumerateDirectories()
                            .OrderByDescending(d => d.LastWriteTimeUtc).Take(32).Select(d => d.FullName));
                        foreach (var cand in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            try { await HellForgePack.VerifyAsync(cand, expectedSelection, ct); reusedExport = cand; break; }
                            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or KeyNotFoundException) { }
                        }
                    }

                    if (reusedExport != null)
                    {
                        await client.InstallPackAsync(reusedExport, expectedSelection, progress, ct);
                    }
                    else
                    {
                        var outputFolder = Path.Combine(exportsRoot, $"repair-{Guid.NewGuid():N}");
                        var sources = new List<string>();
                        var modsPath = Path.Combine(validatedRoot, "mods");
                        if (Directory.Exists(modsPath)) sources.Add(modsPath);
                        if (extraSearchFolders != null) sources.AddRange(extraSearchFolders.Where(Directory.Exists));
                        else if (GameLocator.AutoDetect() is { } steamDir && Directory.Exists(Path.Combine(steamDir, "data")))
                            sources.Add(Path.Combine(steamDir, "data"));

                        var plan = await HellForgePack.PlanAsync(expectedSelection, outputFolder, sources, null, ct, progress);
                        if (exportPackOverride != null)
                        {
                            await exportPackOverride(plan, true, progress, ct);
                        }
                        else
                        {
                            await client.EnsureExportAndInstallCapacityAsync(plan, ct);
                            await HellForgePack.ExportAsync(plan, download: true, progress: null, ct: ct, richProgress: progress);
                        }

                        try
                        {
                            await client.InstallPackAsync(plan.Output, expectedSelection, progress, ct);
                        }
                        catch (Exception ex)
                        {
                            HellForgePack.ReclaimFailedInstallExport(plan, ex);
                            throw;
                        }
                    }
                    prefs.AppliedSelection = prefs.SelectionKey;
                    packRepaired = true;
                }

                packVerified = true;
                steps.Add(new(4, "Active pack integrity", true, packRepaired,
                    packRepaired
                        ? $"Rebuilt and verified {expectedSelection.Mode} ({expectedSelection.Texture}) pack."
                        : $"Verified all {expectedSelection.Files.Count} Star Wars pack files by SHA-256."));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            steps.Add(new(4, "Active pack integrity", false, false, ex.Message));
            return FailEarly(steps, "Active pack verification/repair failed: " + ex.Message, gameplayRepairedCount);
        }

        // ── Step 5/5: Stale cache cleanup (never touches saves/account data!) ──
        ct.ThrowIfCancellationRequested();
        progress?.Report(new("Cleaning stale caches", "Reclaiming disk space from old backups and partial files…", 5, 5, CanCancel: false));
        bytesReclaimed = await ForgeDiskManager.CleanSafeFilesAsync(validatedRoot, ct);
        steps.Add(new(5, "Stale cache cleanup", true, bytesReclaimed > 0,
            bytesReclaimed > 0
                ? $"Reclaimed {Pack.FormatBytes(bytesReclaimed)} of stale backups and staging files."
                : "No stale cache files needed cleanup."));

        var summary = BuildHealthySummary(step1Repaired, gameplayRepairedCount, packRepaired, bytesReclaimed);
        return new ForgeVerifyReport(
            Healthy: true,
            Steps: steps,
            GameplayModsRepaired: gameplayRepairedCount,
            PackVerified: packVerified,
            PackRepaired: packRepaired,
            BytesReclaimed: bytesReclaimed,
            Summary: summary);
    }

    static string ValidateForgeInstallRoot(string installRoot, bool requireWindowsBinaries)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            throw new InvalidDataException("No HellForge installation folder is configured.");
        if (requireWindowsBinaries)
            return HellForgeCommands.ValidateRoot(installRoot);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        for (var dir = new DirectoryInfo(root); dir != null; dir = dir.Parent)
        {
            FileSafety.RejectLink(dir.FullName);
            if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose the separate HellForge folder, outside Steam.");
        }
        var marker = FileSafety.Child(root, "hellforge-install.json");
        if (!File.Exists(marker) || new FileInfo(marker).Length > 64 * 1024)
            throw new InvalidDataException("Choose the HellForge installation folder containing hellforge-install.json.");
        using var doc = JsonDocument.Parse(File.ReadAllText(marker));
        if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported HellForge installation format.");
        return root;
    }

    static ForgeVerifyReport FailEarly(List<ForgeVerifyStepResult> steps, string reason, int gameplayRepaired = 0) =>
        new(
            Healthy: false,
            Steps: steps,
            GameplayModsRepaired: gameplayRepaired,
            PackVerified: false,
            PackRepaired: false,
            BytesReclaimed: 0,
            Summary: reason);

    static string BuildHealthySummary(bool step1Repaired, int gameplayRepaired, bool packRepaired, long bytesReclaimed)
    {
        var actions = new List<string>();
        if (step1Repaired) actions.Add("recovered interrupted transaction");
        if (gameplayRepaired > 0) actions.Add($"repaired gameplay preset ({gameplayRepaired} update(s))");
        if (packRepaired) actions.Add("repaired Star Wars pack");
        if (bytesReclaimed > 0) actions.Add($"freed {Pack.FormatBytes(bytesReclaimed)}");
        return actions.Count == 0
            ? "All 5 checks passed — Forge, r8 gameplay preset, and active pack are healthy."
            : "Verified & repaired: " + string.Join(", ", actions) + ".";
    }
}
