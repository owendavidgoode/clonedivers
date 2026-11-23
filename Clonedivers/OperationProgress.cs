using System.Diagnostics;
using System.Text.Json;

namespace Clonedivers;

/// <summary>Structured progress snapshot shared by Windows and Linux launcher UIs.</summary>
public sealed record OperationProgress(
    string Stage,
    string? SubStep = null,
    int ItemIndex = 0,
    int ItemCount = 0,
    long BytesDone = 0,
    long BytesTotal = 0,
    double? BytesPerSecond = null,
    TimeSpan? Eta = null,
    bool IsIndeterminate = false,
    bool CanCancel = false,
    string[]? QrRows = null)
{
    public double? Fraction =>
        IsIndeterminate ? null
        : BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0d, 1d)
        : ItemCount > 0 ? Math.Clamp((double)ItemIndex / ItemCount, 0d, 1d)
        : null;

    public int? Percent => Fraction is { } f ? (int)Math.Round(f * 100d) : null;

    public string FormatMetrics()
    {
        var parts = new List<string>(4);
        if (ItemCount > 0)
            parts.Add(ItemIndex > 0 ? $"{Math.Min(ItemIndex, ItemCount)}/{ItemCount}" : $"0/{ItemCount}");
        if (BytesTotal > 0)
        {
            parts.Add($"{Pack.FormatBytes(Math.Clamp(BytesDone, 0, BytesTotal))} / {Pack.FormatBytes(BytesTotal)}");
            if (Percent is { } pct) parts.Add($"{pct}%");
        }
        else if (Percent is { } pct && !IsIndeterminate)
        {
            parts.Add($"{pct}%");
        }
        if (BytesPerSecond is { } rate and > 1024)
            parts.Add(FormatSpeed(rate));
        if (Eta is { } eta && eta > TimeSpan.Zero && BytesDone < BytesTotal)
            parts.Add(FormatEta(eta));
        return string.Join(" · ", parts);
    }

    public string FormatSummary()
    {
        var metrics = FormatMetrics();
        var head = string.IsNullOrWhiteSpace(SubStep) ? Stage : $"{Stage} — {SubStep}";
        return string.IsNullOrEmpty(metrics) ? head : $"{head} · {metrics}";
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024d * 1024d)
            return $"{bytesPerSecond / (1024d * 1024d):0.0} MB/s";
        if (bytesPerSecond >= 1024d)
            return $"{bytesPerSecond / 1024d:0} KB/s";
        return $"{Math.Max(0, bytesPerSecond):0} B/s";
    }

    public static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalSeconds < 10) return "< 10s left";
        if (eta.TotalSeconds < 60) return $"~{(int)Math.Ceiling(eta.TotalSeconds)}s left";
        if (eta.TotalHours < 1) return $"~{(int)eta.TotalMinutes}m {eta.Seconds:00}s left";
        return $"~{(int)eta.TotalHours}h {eta.Minutes:00}m left";
    }
}

/// <summary>EWMA throughput and ETA estimator resistant to restarts, stalls and short bursts.</summary>
public sealed class SpeedTracker
{
    readonly Func<TimeSpan> clock;
    readonly double alpha;
    readonly TimeSpan minSampleInterval;
    TimeSpan lastSampleTime;
    long lastSampleBytes;
    bool initialized;
    double? ewmaBytesPerSecond;

    public SpeedTracker(Func<TimeSpan>? clock = null, double alpha = 0.25, TimeSpan? minSampleInterval = null)
    {
        var sw = clock == null ? Stopwatch.StartNew() : null;
        this.clock = clock ?? (() => sw!.Elapsed);
        this.alpha = Math.Clamp(alpha, 0.05, 0.95);
        this.minSampleInterval = minSampleInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public void Reset()
    {
        initialized = false;
        ewmaBytesPerSecond = null;
        lastSampleBytes = 0;
        lastSampleTime = TimeSpan.Zero;
    }

    public (double? BytesPerSecond, TimeSpan? Eta) Update(long bytesDone, long bytesTotal)
    {
        var now = clock();
        if (!initialized || bytesDone < lastSampleBytes)
        {
            initialized = true;
            lastSampleTime = now;
            lastSampleBytes = Math.Max(0, bytesDone);
            ewmaBytesPerSecond = null;
            return (null, null);
        }

        var dt = (now - lastSampleTime).TotalSeconds;
        var delta = bytesDone - lastSampleBytes;
        if (dt >= minSampleInterval.TotalSeconds && delta > 0)
        {
            var instant = delta / dt;
            ewmaBytesPerSecond = ewmaBytesPerSecond is { } prev
                ? prev * (1d - alpha) + instant * alpha
                : instant;
            lastSampleTime = now;
            lastSampleBytes = bytesDone;
        }
        else if (dt >= 5.0 && delta == 0)
        {
            // Stalled stream: gently decay rate rather than reporting a stale peak speed forever.
            if (ewmaBytesPerSecond is { } prev)
                ewmaBytesPerSecond = prev * 0.5;
            lastSampleTime = now;
        }

        TimeSpan? eta = null;
        if (ewmaBytesPerSecond is { } rate and > 1024 && bytesTotal > bytesDone)
        {
            var seconds = (bytesTotal - bytesDone) / rate;
            if (seconds is > 0 and < 86400 * 7)
                eta = TimeSpan.FromSeconds(seconds);
        }
        return (ewmaBytesPerSecond, eta);
    }
}

/// <summary>Coalesces high-frequency progress callbacks so UI threads update at most ~10 Hz.</summary>
public sealed class ProgressThrottler
{
    readonly Action<OperationProgress> sink;
    readonly Func<TimeSpan> clock;
    readonly TimeSpan interval;
    readonly object gate = new();
    TimeSpan lastSent;
    OperationProgress? lastValue;
    bool hasSent;

    public ProgressThrottler(Action<OperationProgress> sink, TimeSpan? interval = null, Func<TimeSpan>? clock = null)
    {
        this.sink = sink;
        this.interval = interval ?? TimeSpan.FromMilliseconds(100);
        var sw = clock == null ? Stopwatch.StartNew() : null;
        this.clock = clock ?? (() => sw!.Elapsed);
    }

    public void Report(OperationProgress value)
    {
        bool emit;
        lock (gate)
        {
            var now = clock();
            var important = !hasSent
                || value.QrRows != null
                || lastValue == null
                || !string.Equals(value.Stage, lastValue.Stage, StringComparison.Ordinal)
                || value.CanCancel != lastValue.CanCancel
                || value.IsIndeterminate != lastValue.IsIndeterminate
                || (value.BytesTotal > 0 && value.BytesDone >= value.BytesTotal && lastValue.BytesDone < value.BytesTotal)
                || (value.ItemCount > 0 && value.ItemIndex >= value.ItemCount && lastValue.ItemIndex < value.ItemCount);

            emit = important || (now - lastSent) >= interval;
            if (emit)
            {
                hasSent = true;
                lastSent = now;
                lastValue = value;
            }
        }
        if (emit) sink(value);
    }

    public IProgress<OperationProgress> AsProgress() => new SyncProgress<OperationProgress>(Report);
}

/// <summary>Fixed local drive choice shown during guided setup.</summary>
public sealed record LocalDriveOption(
    string Root,
    string Label,
    long FreeBytes,
    long TotalBytes,
    bool MeetsBudget,
    string SuggestedInstallPath,
    bool IsRecommended = false)
{
    public double UsedFraction => TotalBytes > 0 ? Math.Clamp((double)(TotalBytes - FreeBytes) / TotalBytes, 0d, 1d) : 0d;
    public string DisplaySummary =>
        $"{Root.TrimEnd('\\', '/')} {(string.IsNullOrWhiteSpace(Label) ? "" : $"({Label}) ")}" +
        $"— {Pack.FormatBytes(FreeBytes)} free" +
        (MeetsBudget ? (IsRecommended ? " · Recommended" : " · OK") : " · Low space (< 70 GB)");
}

public static class LocalDriveCatalog
{
    public const long DefaultBudgetBytes = 70L << 30;

    public static IReadOnlyList<LocalDriveOption> EnumerateLocalDrives(long budgetBytes = DefaultBudgetBytes, string? userProfile = null)
    {
        var drives = new List<(string Root, string Label, long Free, long Total)>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (OperatingSystem.IsWindows() && d.DriveType != DriveType.Fixed) continue;
                    if (!OperatingSystem.IsWindows() && d.TotalSize <= 0) continue;
                    drives.Add((d.Name, d.VolumeLabel ?? "", d.AvailableFreeSpace, d.TotalSize));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

        return BuildOptions(drives, budgetBytes, userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OperatingSystem.IsWindows());
    }

    public static IReadOnlyList<LocalDriveOption> BuildOptions(
        IEnumerable<(string Root, string Label, long Free, long Total)> rawDrives,
        long budgetBytes,
        string userProfile,
        bool isWindows)
    {
        var list = new List<LocalDriveOption>();
        var userRoot = string.IsNullOrWhiteSpace(userProfile) ? null : Path.GetPathRoot(userProfile);

        foreach (var d in rawDrives)
        {
            if (d.Total <= 0) continue;
            string suggested;
            if (isWindows)
            {
                var sameAsProfile = userRoot != null && string.Equals(d.Root, userRoot, StringComparison.OrdinalIgnoreCase);
                suggested = sameAsProfile && !string.IsNullOrWhiteSpace(userProfile)
                    ? Path.Combine(userProfile, "HellForge")
                    : Path.Combine(d.Root, "HellForge");
            }
            else
            {
                suggested = Path.Combine(string.IsNullOrWhiteSpace(userProfile) ? d.Root : userProfile, ".local", "share", "hellforge-prefix", "pfx", "drive_c", "HellForge");
            }
            list.Add(new LocalDriveOption(d.Root, d.Label, d.Free, d.Total, d.Free >= budgetBytes, suggested));
        }

        if (list.Count == 0) return list;

        // Recommend the drive with the most free space that meets the budget (tie-breaking toward non-system data drives if C: is tight).
        var recommended = list.Where(d => d.MeetsBudget).OrderByDescending(d => d.FreeBytes).FirstOrDefault()
            ?? list.OrderByDescending(d => d.FreeBytes).First();

        return list
            .Select(d => d with { IsRecommended = string.Equals(d.Root, recommended.Root, StringComparison.OrdinalIgnoreCase) })
            .OrderByDescending(d => d.IsRecommended)
            .ThenByDescending(d => d.MeetsBudget)
            .ThenByDescending(d => d.FreeBytes)
            .ToList();
    }
}

public enum AccountSyncPhase
{
    NotStarted = 0,
    OpenHellForge = 1,
    LaunchHd2AndPassTitle = 2,
    WaitingForCleanup = 3,
    CloseHellForge = 4,
    ReadyToImport = 5,
    Imported = 6
}

public sealed record AccountSyncStepState(int Number, string Title, string Subtitle, bool Completed, bool Active);

public sealed record AccountSyncSnapshot(
    AccountSyncPhase Phase,
    IReadOnlyList<AccountSyncStepState> Steps,
    string Headline,
    string Detail,
    string PrimaryActionLabel,
    bool NeedsCleanupAction);

/// <summary>Computes live 5-step Account Sync progress from existing HellForge/process probes.</summary>
public static class AccountSyncTracker
{
    public static AccountSyncSnapshot Evaluate(
        bool managerOpen,
        string? captureBlock,
        bool captureBlockUnreadable,
        bool gameRunning,
        bool accountCaptured,
        bool accountImported,
        bool syncRequested)
    {
        bool cleanupPending = captureBlock != null && !captureBlockUnreadable &&
            captureBlock.Contains("cleanup", StringComparison.OrdinalIgnoreCase);
        bool proxyRunning = captureBlock != null && !captureBlockUnreadable && !cleanupPending;

        AccountSyncPhase phase;
        string headline, detail, action;
        if (accountImported && captureBlock == null)
        {
            phase = AccountSyncPhase.Imported;
            headline = "HD2 account synced";
            detail = "Your real HD2 inventory and progression are imported. Forge rewards stay local.";
            action = "CONTINUE";
        }
        else if (captureBlockUnreadable)
        {
            phase = AccountSyncPhase.WaitingForCleanup;
            headline = "Check account sync cleanup";
            detail = "Clonedivers could not read the hosts file to verify sync cleanup. Close interfering tools and retry.";
            action = "CHECK SYNC STATUS";
        }
        else if (cleanupPending)
        {
            phase = AccountSyncPhase.WaitingForCleanup;
            headline = "Finish capture cleanup in HellForge";
            detail = "Open HellForge → Profile → Account sync → Clean up so normal Steam networking is restored.";
            action = "OPEN HELLFORGE TO CLEAN UP";
        }
        else if (proxyRunning && gameRunning)
        {
            phase = AccountSyncPhase.LaunchHd2AndPassTitle;
            headline = "Pass the HD2 title screen, then quit HD2";
            detail = "Once you reach your Super Destroyer ship, quit Helldivers 2 normally so HellForge seals your capture.";
            action = "WAITING FOR HD2 TO CLOSE…";
        }
        else if (proxyRunning)
        {
            phase = AccountSyncPhase.WaitingForCleanup;
            headline = "Waiting for HellForge capture to finish";
            detail = "In HellForge, let Account sync finish capturing and cleaning up, then close HellForge.";
            action = "OPEN HELLFORGE";
        }
        else if (accountCaptured && !accountImported)
        {
            if (managerOpen)
            {
                phase = AccountSyncPhase.CloseHellForge;
                headline = "Sealed capture ready — close HellForge";
                detail = "Close the HellForge window so Clonedivers can import your account and unlock your owned Warbonds.";
                action = "APPLY ACCOUNT IMPORT";
            }
            else
            {
                phase = AccountSyncPhase.ReadyToImport;
                headline = "Sealed HD2 capture verified!";
                detail = "Click below to import your real HD2 inventory and progression into Forge.";
                action = "APPLY ACCOUNT IMPORT";
            }
        }
        else if (managerOpen || syncRequested)
        {
            phase = AccountSyncPhase.OpenHellForge;
            headline = "Start Sync Inventory inside HellForge";
            detail = "In HellForge choose Profile → Account sync → Sync Inventory (leave Apply import & auto-apply OFF).";
            action = "BRING HELLFORGE TO FRONT";
        }
        else
        {
            phase = AccountSyncPhase.NotStarted;
            headline = "Sync your HD2 account once";
            detail = "Brings your real Helldivers 2 inventory and progression into Forge. Forge progress saves locally.";
            action = "OPEN HELLFORGE TO SYNC";
        }

        int completedCount = phase switch
        {
            AccountSyncPhase.NotStarted => 0,
            AccountSyncPhase.OpenHellForge => 0,
            AccountSyncPhase.LaunchHd2AndPassTitle => 1,
            AccountSyncPhase.WaitingForCleanup => 2,
            AccountSyncPhase.CloseHellForge => 3,
            AccountSyncPhase.ReadyToImport => 4,
            AccountSyncPhase.Imported => 5,
            _ => 0
        };

        var steps = new[]
        {
            new AccountSyncStepState(1, "Open HellForge & click Sync Inventory",
                "Profile → Account sync → Sync Inventory (approve UAC prompt; leave auto-apply OFF).",
                completedCount >= 1, phase is AccountSyncPhase.NotStarted or AccountSyncPhase.OpenHellForge),
            new AccountSyncStepState(2, "Pass HD2 title screen",
                "Wait until you load onto your ship, then quit Helldivers 2 normally.",
                completedCount >= 2, phase == AccountSyncPhase.LaunchHd2AndPassTitle),
            new AccountSyncStepState(3, "Wait for capture cleanup",
                "HellForge restores normal networking automatically after HD2 exits.",
                completedCount >= 3, phase == AccountSyncPhase.WaitingForCleanup),
            new AccountSyncStepState(4, "Close HellForge window",
                "Closing HellForge lets Clonedivers safely import your profile and unlock Warbonds.",
                completedCount >= 4, phase == AccountSyncPhase.CloseHellForge),
            new AccountSyncStepState(5, "Import verified account",
                "Clonedivers merges your sealed capture into Forge automatically.",
                completedCount >= 5, phase == AccountSyncPhase.ReadyToImport),
        };

        return new AccountSyncSnapshot(phase, steps, headline, detail, action, cleanupPending);
    }
}

/// <summary>Fast pre-click download-size preview shown inside Customize before downloading.</summary>
public sealed record PackSizePreview(
    bool IsRegular,
    long TotalBytes,
    long CachedBytes,
    long DownloadBytes,
    int TotalFiles,
    int CachedFiles,
    int DownloadFiles,
    bool IsAppliedOnDisk,
    string BadgeText,
    string DetailText);

public static class PackSizeEstimator
{
    /// <summary>Fast non-hashing estimate using exact size matching across known local cache/mod folders, including resumable partial downloads.</summary>
    public static PackSizePreview Estimate(
        bool isRegular,
        HellForgePack.Selection? selection,
        IEnumerable<string> localSearchFolders,
        bool isAlreadyApplied = false,
        string? cacheRoot = null)
    {
        if (isRegular || selection == null)
        {
            return new PackSizePreview(
                IsRegular: true, TotalBytes: 0, CachedBytes: 0, DownloadBytes: 0,
                TotalFiles: 0, CachedFiles: 0, DownloadFiles: 0, IsAppliedOnDisk: isAlreadyApplied,
                BadgeText: isAlreadyApplied ? "Active · No download" : "No pack download needed",
                DetailText: "Standard Helldivers 2 visuals with required Forge gameplay mods.");
        }

        // Index distinct file sizes present across local folders (fast directory stat scan, no multi-GB hashing).
        var availableSizes = new Dictionary<long, int>();
        var checkedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in localSearchFolders)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string folder;
            try { folder = Path.GetFullPath(raw); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!checkedFolders.Add(folder) || !Directory.Exists(folder)) continue;
            try
            {
                var dir = new DirectoryInfo(folder);
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = true
                }))
                {
                    var len = file.Length;
                    if (len <= 0) continue;
                    availableSizes[len] = availableSizes.GetValueOrDefault(len) + 1;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // Check unique SHA-256 blobs in selection.
        var uniqueFiles = selection.Files.Where(f => f.Size > 0).DistinctBy(f => f.Sha256, StringComparer.OrdinalIgnoreCase).ToList();
        long totalBytes = uniqueFiles.Sum(f => f.Size);
        long cachedBytes = 0;
        int cachedFiles = 0;
        var remaining = new Dictionary<long, int>(availableSizes);
        var uncachedFiles = new List<PackFile>();

        foreach (var file in uniqueFiles)
        {
            if (remaining.TryGetValue(file.Size, out var count) && count > 0)
            {
                remaining[file.Size] = count - 1;
                cachedBytes += file.Size;
                cachedFiles++;
            }
            else
            {
                uncachedFiles.Add(file);
            }
        }

        long partialSavedBytes = 0;
        bool hasInterruptedState = false;
        if (!string.IsNullOrWhiteSpace(cacheRoot) && uncachedFiles.Count > 0)
        {
            try
            {
                var fullCache = Path.GetFullPath(cacheRoot);
                if (Directory.Exists(fullCache) && (File.GetAttributes(fullCache) & FileAttributes.ReparsePoint) == 0)
                {
                    var dlDir = Path.Combine(fullCache, "appearance-downloads");
                    if (Directory.Exists(dlDir) && (File.GetAttributes(dlDir) & FileAttributes.ReparsePoint) == 0)
                    {
                        foreach (var missing in uncachedFiles)
                        {
                            var partPath = Path.Combine(dlDir, missing.Sha256.ToLowerInvariant() + ".partial");
                            if (!File.Exists(partPath)) continue;
                            var fi = new FileInfo(partPath);
                            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                            if (fi.Length > 0 && fi.Length <= missing.Size)
                            {
                                partialSavedBytes += fi.Length;
                                hasInterruptedState = true;
                            }
                        }
                    }

                    var exportsDir = Path.Combine(fullCache, "exports");
                    if (Directory.Exists(exportsDir) && (File.GetAttributes(exportsDir) & FileAttributes.ReparsePoint) == 0)
                    {
                        var selectionSizes = uniqueFiles.Select(f => f.Size).ToHashSet();
                        foreach (var exp in new DirectoryInfo(exportsDir).EnumerateDirectories().Take(16))
                        {
                            if (exp.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                                exp.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase) ||
                                File.Exists(Path.Combine(exp.FullName, HellForgePack.ReceiptName)))
                                continue;
                            if (exp.EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                                   .Any(f => selectionSizes.Contains(f.Length)))
                            {
                                hasInterruptedState = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }

        long savedTotalBytes = Math.Min(totalBytes, cachedBytes + partialSavedBytes);
        long downloadBytes = Math.Max(0, totalBytes - savedTotalBytes);
        int downloadFiles = Math.Max(0, uniqueFiles.Count - cachedFiles);

        string badge, detail;
        if (isAlreadyApplied && downloadFiles == 0)
        {
            badge = "Ready · Active";
            detail = $"All {uniqueFiles.Count} files ({Pack.FormatBytes(totalBytes)}) are active and ready.";
        }
        else if (downloadFiles == 0)
        {
            badge = "Ready — cached locally";
            detail = $"All {uniqueFiles.Count} files ({Pack.FormatBytes(totalBytes)}) found locally; switching needs no download.";
        }
        else if (hasInterruptedState && savedTotalBytes > 0)
        {
            badge = $"Resume: {Pack.FormatBytes(downloadBytes)} left ({Pack.FormatBytes(savedTotalBytes)} saved)";
            detail = $"Paused download ready to resume — {Pack.FormatBytes(savedTotalBytes)} already saved locally.";
        }
        else if (cachedFiles > 0)
        {
            badge = $"~{Pack.FormatBytes(downloadBytes)} download";
            detail = $"Downloads ~{Pack.FormatBytes(downloadBytes)} ({downloadFiles} files) and reuses ~{Pack.FormatBytes(cachedBytes)} locally.";
        }
        else
        {
            badge = $"~{Pack.FormatBytes(downloadBytes)} download";
            detail = $"First-time download: ~{Pack.FormatBytes(downloadBytes)} across {downloadFiles} verified files.";
        }

        return new PackSizePreview(
            IsRegular: false,
            TotalBytes: totalBytes,
            CachedBytes: savedTotalBytes,
            DownloadBytes: downloadBytes,
            TotalFiles: uniqueFiles.Count,
            CachedFiles: cachedFiles,
            DownloadFiles: downloadFiles,
            IsAppliedOnDisk: isAlreadyApplied,
            BadgeText: badge,
            DetailText: detail);
    }

    /// <summary>Collects standard local search roots (exports, backups, mods, optional Steam HD2 folders) for fast estimation.</summary>
    public static List<string> CollectSearchFolders(string? installRoot, string? cacheRoot, string? steamGameDir = null)
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            var mods = Path.Combine(installRoot, "mods");
            if (Directory.Exists(mods)) folders.Add(mods);
        }
        if (!string.IsNullOrWhiteSpace(cacheRoot))
        {
            var exports = Path.Combine(cacheRoot, "exports");
            if (Directory.Exists(exports))
            {
                try
                {
                    foreach (var d in new DirectoryInfo(exports).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(16))
                        if (!d.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase)) folders.Add(d.FullName);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            var backups = Path.Combine(cacheRoot, "backups");
            if (Directory.Exists(backups))
            {
                try
                {
                    foreach (var d in new DirectoryInfo(backups).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).Take(16))
                    {
                        var prev = Path.Combine(d.FullName, "previous-pack");
                        if (Directory.Exists(prev)) folders.Add(prev);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        if (!string.IsNullOrWhiteSpace(steamGameDir))
        {
            foreach (var sub in new[] { "mods_download", "mods_old", "mods_off", "data" })
            {
                var p = Path.Combine(steamGameDir, sub);
                if (Directory.Exists(p)) folders.Add(p);
            }
        }
        return folders;
    }
}

/// <summary>Shared concise tactical status copy, loadout summaries, hardware labels, and disk usage lines across Windows and Linux.</summary>
public static class ForgeTacticalCopy
{
    public static string ReadyDetail(string mode, bool firstLaunch = false)
    {
        var line = mode switch
        {
            "empire" => "Imperial garrison online · D1–11 ready · Steam co-op enabled",
            "regular" => "Super Earth command online · D1–11 ready · Steam co-op enabled",
            _ => "Venator in orbit · D1–11 ready · Steam co-op enabled"
        };
        return firstLaunch ? line + " · Windows may ask for network approval once" : line;
    }

    public const string LaunchingHeadline = "Launching Helldivers 2 — please wait…";
    public const string LaunchingDetail = "Staging mods and starting the game (usually 15–30 seconds). Don't close the launcher.";
    public const string LaunchingPrimaryButton = "LAUNCHING HELLDIVERS 2 — PLEASE WAIT…";
    public const string CancelLaunchButton = "Cancel Launch";
    public const string RunningHeadline = "Helldivers 2 is running";
    public const string RunningDetail = "End the session here when you finish playing so cleanup completes cleanly.";

    public static string FormatLoadoutSummary(
        string mode,
        string enemySkin,
        bool deltaSquad,
        bool covenant,
        bool autoPerformanceTier,
        string performanceTier,
        HardwarePerformanceRecommendation? hardware,
        string hostChaosIntensity = "standard",
        string hostHeavySeaf = "standard",
        bool hostFasterCorpseCleanup = true,
        string? squidSkin = null,
        bool deathTrooperVoice = false)
    {
        var hostNote = FormatHostMutatorNote(hostChaosIntensity, hostHeavySeaf, hostFasterCorpseCleanup);
        if (string.Equals(mode, "regular", StringComparison.OrdinalIgnoreCase))
        {
            const string baseRegular = "Original Helldivers 2 visuals · Pinned gameplay preset";
            return string.IsNullOrEmpty(hostNote) ? baseRegular : $"{baseRegular} · {hostNote}";
        }

        var effectiveSquid = squidSkin ?? (covenant ? "mixed-covenant" : "native-squids");
        bool isEmpire = string.Equals(mode, "empire", StringComparison.OrdinalIgnoreCase);
        bool isClones = string.Equals(mode, "clonedivers", StringComparison.OrdinalIgnoreCase);
        bool isDefaultFullMix = string.Equals(enemySkin, "mixed-droids", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(effectiveSquid, "mixed-covenant", StringComparison.OrdinalIgnoreCase) &&
                                (!isClones || deltaSquad) &&
                                (!isEmpire || !deathTrooperVoice);

        string mixPart;
        if (isDefaultFullMix)
        {
            mixPart = "Full Star Wars Mix";
        }
        else
        {
            var segments = new List<string>(4)
            {
                enemySkin switch
                {
                    "mixed-droids" => "Mixed Bots + CIS",
                    "native-automatons" => "Native Automatons",
                    _ => "All CIS Droids"
                }
            };
            if (isClones)
                segments.Add(deltaSquad ? "Delta Squad" : "Standard Clones");
            if (squidSkin != null)
            {
                if (string.Equals(squidSkin, "mixed-covenant", StringComparison.OrdinalIgnoreCase))
                    segments.Add("Mixed Covenant");
                else if (string.Equals(squidSkin, "all-covenant", StringComparison.OrdinalIgnoreCase))
                    segments.Add("Covenant");
            }
            else if (covenant)
            {
                segments.Add("Covenant");
            }
            if (isEmpire && deathTrooperVoice)
                segments.Add("Death Trooper Voice");
            mixPart = string.Join(" · ", segments);
        }

        var resolvedTier = PerformanceTierAdvisor.GetTier(
            autoPerformanceTier ? PerformanceTierAdvisor.Auto : performanceTier,
            hardware);
        var tierPart = autoPerformanceTier || string.Equals(performanceTier, PerformanceTierAdvisor.Auto, StringComparison.OrdinalIgnoreCase)
            ? $"Auto ({resolvedTier.ShortLabel})"
            : resolvedTier.DisplayName;

        return string.IsNullOrEmpty(hostNote)
            ? $"{mixPart} · {tierPart}"
            : $"{mixPart} · {tierPart} · {hostNote}";
    }

    public static string FormatHostMutatorNote(string chaosIntensity, string heavySeaf, bool fasterCorpseCleanup)
    {
        var notes = new List<string>(3);
        if (!string.Equals(chaosIntensity, "standard", StringComparison.OrdinalIgnoreCase))
            notes.Add(chaosIntensity == "total-war" ? "Chaos: Total War" : "Chaos: Off");
        if (!string.Equals(heavySeaf, "standard", StringComparison.OrdinalIgnoreCase))
            notes.Add(heavySeaf == "constant" ? "SEAF: Constant" : "SEAF: Heavy");
        if (!fasterCorpseCleanup)
            notes.Add("Corpse Cleanup: Off");
        return notes.Count <= 1 ? string.Join(" · ", notes) : "Custom Host Rules";
    }

    public static string FormatHardwareDetectionLine(HardwarePerformanceRecommendation hw)
    {
        var autoDef = PerformanceTierAdvisor.GetTier(hw.RecommendedTierId, hw);
        var gpu = string.IsNullOrWhiteSpace(hw.GpuName) ? "System GPU" : hw.GpuName;
        var mem = hw.VramGb > 0
            ? $" ({hw.VramGb:0.#} GB)"
            : hw.RamGb > 0
                ? $" ({hw.RamGb:0.#} GB RAM)"
                : "";
        return $"Detected {gpu}{mem} · Auto → {autoDef.DisplayName}";
    }

    public static string FormatDiskUsageSummary(ForgeDiskAnalysis disk, bool hasInstallation)
    {
        if (!hasInstallation || disk.TotalBytes <= 0 && disk.GameCopyBytes <= 0 && disk.ActivePackBytes <= 0 && disk.DownloadCacheBytes <= 0 && disk.OldBackupsBytes <= 0)
            return "No HellForge installation measured yet.";

        var cacheTotal = disk.DownloadCacheBytes + disk.OldBackupsBytes;
        var summary = $"Game {Pack.FormatBytes(disk.GameCopyBytes)} · Pack {Pack.FormatBytes(disk.ActivePackBytes)} · Cache {Pack.FormatBytes(cacheTotal)}";
        if (disk.ReclaimableBytes > 0)
            summary += $" ({Pack.FormatBytes(disk.ReclaimableBytes)} reclaimable)";
        return summary;
    }

    public static string DiscordPresenceDetails(string mode, bool deltaSquad) => mode switch
    {
        "empire" => "Playing Clonedivers — Galactic Empire (HellForge)",
        "regular" => "Playing Clonedivers — Super Earth (HellForge)",
        _ => deltaSquad
            ? "Playing Clonedivers — Republic Commandos (HellForge)"
            : "Playing Clonedivers — Clone Wars (HellForge)"
    };

    public static string DiscordPresenceImageKey(string mode) =>
        string.Equals(mode, "empire", StringComparison.OrdinalIgnoreCase) ? "empire-helmet" : "clone-helmet";
}
