namespace Clonedivers;

public static class LinuxInstaller
{
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

    public static async Task<UpdatePlan> PlanAsync(string gameDir, IReadOnlyList<PackFile> files, bool active, CancellationToken ct)
    {
        FileSafety.ValidateTargets(files);
        var inventory = await Pack.InventoryAsync(gameDir, files, new HashCache(), true, null, ct);
        var downloads = await Pack.VerifiedDownloadsAsync(Path.Combine(gameDir, Pack.DownloadFolder), files, ct);
        return Pack.Plan(gameDir, files, inventory, downloads, active);
    }

    public static async Task<ApplyResult> ApplyAsync(string gameDir, RecoveryTarget target, string planPath, string receiptPath,
        Func<UpdatePlan, bool> confirm, IProgress<(long done, long total, int file, int files)>? progress, CancellationToken ct)
    {
        RequireClosed();
        var plan = await PlanAsync(gameDir, target.Files, target.TargetActive, ct);
        if (!confirm(plan)) throw new OperationCanceledException("No files changed.");
        await Pack.DownloadPlanAsync(plan, Path.Combine(gameDir, Pack.DownloadFolder), progress, ct);
        ct.ThrowIfCancellationRequested();
        RequireClosed();
        // Re-inventory after downloading: the game, Steam or a manual edit may have changed files.
        plan = await PlanAsync(gameDir, target.Files, target.TargetActive, ct);
        if (plan.Downloads.Count > 0) throw new IOException("Some downloads are missing. Retry to resume.");
        return Pack.Apply(gameDir, plan, planPath, target.PackVersion, null, null,
            target.GameBuild, target.Options, target.TextureProfile, true, receiptPath);
    }
}
