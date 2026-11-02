namespace Clonedivers;

public static class LinuxInstaller
{
    public static string PlanPathFor(string gameDir)
    {
        var scoped = Path.Combine(PlatformPaths.ConfigDirectory, "updates", Path.GetFileName(Settings.ReceiptPathFor(gameDir)));
        // Finish a 0.1.0 recovery target in place; other installations use their own file.
        return !File.Exists(scoped) && RecoveryTarget.Read(gameDir, Settings.PlanPath) is not null ? Settings.PlanPath : scoped;
    }

    public static RecoveryTarget RepairTarget(string gameDir, InstallReceipt receipt) => receipt.ToRecoveryTarget(ModFiles.GetState(gameDir) switch
    {
        ModState.On => true,
        ModState.Off => false,
        _ => receipt.TargetActive
    });

    public static void RequireNoSteamUpdate(string gameDir)
    {
        if (SteamAcf.Read(gameDir)?.UpdatePending == true)
            throw new InvalidOperationException("Steam has a game update queued. Finish it before changing or launching the pack.");
    }

    public static void RequireClosed()
    {
        if (Game.IsRunning()) throw new InvalidOperationException("Close Helldivers 2 before changing or launching the pack.");
    }

    public static FileStream Lock(string gameDir)
    {
        var path = FileSafety.Child(gameDir, ".clonedivers.lock");
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another launcher may be using this game folder. Close it and retry.", ex); }
    }

    public static async Task<UpdatePlan> PlanAsync(string gameDir, IReadOnlyList<PackFile> files, bool active, CancellationToken ct, IProgress<(long done, long total)>? hashProgress = null)
    {
        FileSafety.ValidateTargets(files);
        var inventory = await Pack.InventoryAsync(gameDir, files, new HashCache(), true, hashProgress, ct);
        var downloads = await Pack.VerifiedDownloadsAsync(Path.Combine(gameDir, Pack.DownloadFolder), files, ct);
        return Pack.Plan(gameDir, files, inventory, downloads, active);
    }

    public static Task<ApplyResult> ApplyAsync(string gameDir, RecoveryTarget target, string planPath, string receiptPath,
        Func<UpdatePlan, bool> confirm, IProgress<(long done, long total, int file, int files)>? progress, CancellationToken ct)
        => ApplyWithConfirmationAsync(gameDir, target, planPath, receiptPath, p => Task.FromResult(confirm(p)), progress, ct);

    public static async Task<ApplyResult> ApplyWithConfirmationAsync(string gameDir, RecoveryTarget target, string planPath, string receiptPath,
        Func<UpdatePlan, Task<bool>> confirm, IProgress<(long done, long total, int file, int files)>? progress, CancellationToken ct,
        IProgress<(string phase, long done, long total)>? preparation = null)
    {
        RequireClosed();
        RequireNoSteamUpdate(gameDir);
        var observedBuild = SteamAcf.Read(gameDir)?.BuildId;
        var phase = "Checking local files";
        var hashes = new SyncProgress<(long done, long total)>(p => preparation?.Report((phase, p.done, p.total)));
        preparation?.Report((phase, 0, 0));
        var plan = await PlanAsync(gameDir, target.Files, target.TargetActive, ct, hashes);
        preparation?.Report(("Review download", 0, 0));
        if (!await confirm(plan)) throw new OperationCanceledException("No files changed.");
        preparation?.Report(("Downloading pack", 0, 0));
        await Pack.DownloadPlanAsync(plan, Path.Combine(gameDir, Pack.DownloadFolder), progress, ct);
        ct.ThrowIfCancellationRequested();
        RequireClosed();
        RequireNoSteamUpdate(gameDir);
        // Re-inventory after downloading: the game, Steam or a manual edit may have changed files.
        phase = "Final verification";
        preparation?.Report((phase, 0, 0));
        plan = await PlanAsync(gameDir, target.Files, target.TargetActive, ct, hashes);
        if (plan.Downloads.Count > 0) throw new IOException("Some downloads are missing. Retry to resume.");
        RequireNoSteamUpdate(gameDir);
        if (SteamAcf.Read(gameDir)?.BuildId != observedBuild)
            throw new IOException("Steam changed the game build during preparation. Retry to review compatibility with the new build; installed mods were left untouched.");
        preparation?.Report(("Finishing update — please wait", 0, 0));
        return Pack.Apply(gameDir, plan, planPath, target.PackVersion, null, null,
            target.GameBuild, target.Options, target.TextureProfile, true, receiptPath);
    }
}
