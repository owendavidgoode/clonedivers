using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

public sealed record ForgeDiskAnalysis(
    long GameCopyBytes,
    long ActivePackBytes,
    long DownloadCacheBytes,
    long OldBackupsBytes,
    long LogsBytes,
    long TotalBytes,
    long ReclaimableBytes);

/// <summary>
/// Analyzes disk usage across a HellForge installation + sibling Clonedivers-HellForge-cache
/// and safely cleans stale partial files, completed backups, and superseded staging caches
/// without ever touching game files, active mods, profiles, or account captures.
/// </summary>
public static class ForgeDiskManager
{
    static readonly Regex ZipHashPartial = new(@"^\.zip-hashes\.json\.[0-9a-f]{8}\.partial$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex LauncherExportName = new(@"^(clonedivers|commandos|empire|linux)-[0-9a-f]{32}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? GetCacheRoot(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return null;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
            var parent = Path.GetDirectoryName(full);
            return string.IsNullOrWhiteSpace(parent) ? null : Path.Combine(parent, "Clonedivers-HellForge-cache");
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static ForgeDiskAnalysis Analyze(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return new(0, 0, 0, 0, 0, 0, 0);

        string root;
        try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(0, 0, 0, 0, 0, 0, 0);
        }

        if (IsReparsePoint(root))
            return new(0, 0, 0, 0, 0, 0, 0);

        var cacheRoot = GetCacheRoot(root);
        HashSet<string> protectedBackups = [];
        HashSet<string> completedBackups = [];
        bool backupsReadable = cacheRoot != null && TryGetBackupStates(cacheRoot, out protectedBackups, out completedBackups);
        bool txLockHeld = cacheRoot != null && IsAnyTransactionLockHeld(cacheRoot);
        bool canCleanBackups = backupsReadable && !txLockHeld;
        var installedSelections = GetInstalledPackIdentities(root);
        var registeredModFolders = GetRegisteredModFolders(root);

        long gameCopyBytes = SumDirectoryBytes(Path.Combine(root, "game"));
        long logsBytes = SumDirectoryBytes(Path.Combine(root, "logs"));

        long activePackBytes = 0;
        long oldBackupsBytes = 0;
        long downloadCacheBytes = 0;
        long reclaimableBytes = 0;

        // 1. Inspect <installRoot>/mods (separate active mods from .zip-hashes.*.partial and unregistered *.bak* leftovers)
        var modsRoot = Path.Combine(root, "mods");
        if (Directory.Exists(modsRoot) && !IsReparsePoint(modsRoot))
        {
            try
            {
                foreach (var entry in new DirectoryInfo(modsRoot).EnumerateFileSystemInfos())
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    var fullEntry = Path.TrimEndingDirectorySeparator(entry.FullName);

                    if (entry is DirectoryInfo subDir)
                    {
                        if (subDir.Name.Equals(".unpacked", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var f in EnumerateSafeFiles(subDir.FullName))
                            {
                                if (ZipHashPartial.IsMatch(f.Name))
                                {
                                    downloadCacheBytes += f.Length;
                                    reclaimableBytes += f.Length;
                                }
                                else
                                {
                                    activePackBytes += f.Length;
                                }
                            }
                        }
                        else if (IsBackupName(subDir.Name) && !registeredModFolders.Contains(fullEntry))
                        {
                            var sz = SumDirectoryBytes(subDir.FullName);
                            oldBackupsBytes += sz;
                            reclaimableBytes += sz;
                        }
                        else
                        {
                            activePackBytes += SumDirectoryBytes(subDir.FullName);
                        }
                    }
                    else if (entry is FileInfo modFile)
                    {
                        if (IsBackupName(modFile.Name) || IsStaleTempFileName(modFile.Name))
                        {
                            oldBackupsBytes += modFile.Length;
                            reclaimableBytes += modFile.Length;
                        }
                        else
                        {
                            activePackBytes += modFile.Length;
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // 2. Inspect sibling Clonedivers-HellForge-cache
        if (cacheRoot != null && Directory.Exists(cacheRoot) && !IsReparsePoint(cacheRoot))
        {
            // 2a. Backups
            var backupsDir = Path.Combine(cacheRoot, "backups");
            if (Directory.Exists(backupsDir) && !IsReparsePoint(backupsDir))
            {
                try
                {
                    foreach (var entry in new DirectoryInfo(backupsDir).EnumerateFileSystemInfos())
                    {
                        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (entry is DirectoryInfo bDir)
                        {
                            var sz = SumDirectoryBytes(bDir.FullName);
                            oldBackupsBytes += sz;
                            var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bDir.FullName));
                            if (canCleanBackups && !protectedBackups.Contains(norm) && IsCompletedOrDeletingBackup(bDir, completedBackups))
                                reclaimableBytes += sz;
                        }
                        else if (entry is FileInfo bFile)
                        {
                            oldBackupsBytes += bFile.Length;
                            if (canCleanBackups)
                                reclaimableBytes += bFile.Length;
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            // 2b. Appearance partial downloads
            var appearanceDownloads = Path.Combine(cacheRoot, "appearance-downloads");
            if (Directory.Exists(appearanceDownloads) && !IsReparsePoint(appearanceDownloads))
            {
                var lockHeld = IsLockHeld(Path.Combine(appearanceDownloads, "download.lock"));
                foreach (var f in EnumerateSafeFiles(appearanceDownloads))
                {
                    if (f.Name.Equals("download.lock", StringComparison.OrdinalIgnoreCase)) continue;
                    downloadCacheBytes += f.Length;
                    if (!lockHeld && (f.Name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || IsStaleTempFileName(f.Name) || IsBackupName(f.Name)))
                        reclaimableBytes += f.Length;
                }
            }

            // 2c. Prepared export staging folders
            var exportsDir = Path.Combine(cacheRoot, "exports");
            if (Directory.Exists(exportsDir) && !IsReparsePoint(exportsDir))
            {
                var reclaimableExports = FindReclaimableExports(exportsDir, installedSelections);
                try
                {
                    foreach (var entry in new DirectoryInfo(exportsDir).EnumerateFileSystemInfos())
                    {
                        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (entry is DirectoryInfo eDir)
                        {
                            var sz = SumDirectoryBytes(eDir.FullName);
                            downloadCacheBytes += sz;
                            var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(eDir.FullName));
                            if (!txLockHeld && reclaimableExports.Contains(norm))
                                reclaimableBytes += sz;
                        }
                        else if (entry is FileInfo eFile)
                        {
                            downloadCacheBytes += eFile.Length;
                            if (!txLockHeld && (IsStaleTempFileName(eFile.Name) || IsBackupName(eFile.Name)))
                                reclaimableBytes += eFile.Length;
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            // 2d. Gameplay archive staging cache
            var gameplayDir = Path.Combine(cacheRoot, "gameplay");
            if (Directory.Exists(gameplayDir) && !IsReparsePoint(gameplayDir))
            {
                try
                {
                    foreach (var entry in new DirectoryInfo(gameplayDir).EnumerateFileSystemInfos())
                    {
                        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (entry is DirectoryInfo gDir)
                        {
                            bool isCurrentCatalog = string.Equals(gDir.Name, HellForgeGameplayCatalog.Pinned.Id, StringComparison.Ordinal);
                            if (!isCurrentCatalog)
                            {
                                var sz = SumDirectoryBytes(gDir.FullName);
                                downloadCacheBytes += sz;
                                if (!txLockHeld)
                                    reclaimableBytes += sz;
                            }
                            else
                            {
                                foreach (var f in EnumerateSafeFiles(gDir.FullName))
                                {
                                    downloadCacheBytes += f.Length;
                                    var rel = Path.GetRelativePath(gDir.FullName, f.FullName);
                                    if (!txLockHeld && (rel.StartsWith("imports" + Path.DirectorySeparatorChar, PlatformPaths.Comparison) ||
                                        IsStaleTempFileName(f.Name) || IsBackupName(f.Name)))
                                    {
                                        reclaimableBytes += f.Length;
                                    }
                                }
                            }
                        }
                        else if (entry is FileInfo gFile)
                        {
                            downloadCacheBytes += gFile.Length;
                            if (!txLockHeld)
                                reclaimableBytes += gFile.Length;
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            // 2e. Installer cache + any stray top-level *.bak* / *.tmp in cacheRoot
            var installersDir = Path.Combine(cacheRoot, "installers");
            if (Directory.Exists(installersDir) && !IsReparsePoint(installersDir))
            {
                var lockHeld = IsLockHeld(Path.Combine(installersDir, "forge-setup.lock"));
                foreach (var f in EnumerateSafeFiles(installersDir))
                {
                    if (f.Name.Equals("forge-setup.lock", StringComparison.OrdinalIgnoreCase)) continue;
                    downloadCacheBytes += f.Length;
                    if (!lockHeld && (IsStaleTempFileName(f.Name) || IsBackupName(f.Name)))
                        reclaimableBytes += f.Length;
                }
            }

            try
            {
                foreach (var entry in new DirectoryInfo(cacheRoot).EnumerateFileSystemInfos())
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (entry.Name is "backups" or "appearance-downloads" or "exports" or "gameplay" or "installers" or "transactions" or "accounts")
                        continue;
                    if (entry is DirectoryInfo extraDir && (IsBackupName(extraDir.Name) || extraDir.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase)))
                    {
                        var sz = SumDirectoryBytes(extraDir.FullName);
                        oldBackupsBytes += sz;
                        reclaimableBytes += sz;
                    }
                    else if (entry is FileInfo extraFile && (IsBackupName(extraFile.Name) || IsStaleTempFileName(extraFile.Name)))
                    {
                        oldBackupsBytes += extraFile.Length;
                        reclaimableBytes += extraFile.Length;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        long totalBytes = checked(gameCopyBytes + activePackBytes + downloadCacheBytes + oldBackupsBytes + logsBytes);
        return new ForgeDiskAnalysis(
            GameCopyBytes: gameCopyBytes,
            ActivePackBytes: activePackBytes,
            DownloadCacheBytes: downloadCacheBytes,
            OldBackupsBytes: oldBackupsBytes,
            LogsBytes: logsBytes,
            TotalBytes: totalBytes,
            ReclaimableBytes: reclaimableBytes);
    }

    public static Task<long> CleanSafeFilesAsync(string? installRoot, CancellationToken ct = default) =>
        Task.Run(() => CleanCore(installRoot, staleOnly: false, ct), ct);

    internal static long CleanSafeFilesSync(
        string? installRoot,
        CancellationToken ct = default,
        IEnumerable<string>? preservePaths = null,
        bool reclaimUnpreservedExports = false,
        string? callerOwnedLockPath = null) =>
        CleanCore(installRoot, staleOnly: false, ct, preservePaths, reclaimUnpreservedExports, callerOwnedLockPath);

    /// <summary>
    /// Non-blocking startup/post-install background sweep. Never throws, skips active downloads/sessions,
    /// and removes stale partial files, deleting-* remnants, and completed old backups.
    /// </summary>
    public static async Task<long> AutoCleanStaleAsync(string? installRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return 0L;
        try
        {
            return await Task.Run(() =>
            {
                try { if (Game.IsRunning()) return 0L; } catch { return 0L; }
                return CleanCore(installRoot, staleOnly: true, ct);
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            return 0L;
        }
    }

    static long CleanCore(
        string? installRoot,
        bool staleOnly,
        CancellationToken ct,
        IEnumerable<string>? preservePaths = null,
        bool reclaimUnpreservedExports = false,
        string? callerOwnedLockPath = null)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(installRoot)) return 0L;
        if (!staleOnly && Game.IsRunning())
            throw new InvalidOperationException("Close Helldivers 2 before cleaning Forge cache files.");

        var pathComp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var preservedFiles = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var preservedDirs = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (preservePaths != null)
        {
            foreach (var rawPath in preservePaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                try
                {
                    var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawPath));
                    preservedFiles.Add(full);
                    for (var parent = Path.GetDirectoryName(full); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                        preservedDirs.Add(Path.TrimEndingDirectorySeparator(parent));
                }
                catch { }
            }
        }
        bool IsPreservedTree(string dirFull)
        {
            var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dirFull));
            if (preservedDirs.Contains(norm) || preservedFiles.Contains(norm)) return true;
            var prefix = norm + Path.DirectorySeparatorChar;
            return preservedFiles.Any(f => f.StartsWith(prefix, pathComp));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        FileSafety.RejectLink(root);
        var cacheRoot = GetCacheRoot(root);
        HashSet<string> protectedBackups = [];
        HashSet<string> completedBackups = [];
        bool backupsReadable = cacheRoot != null && TryGetBackupStates(cacheRoot, out protectedBackups, out completedBackups);
        bool txLockHeld = cacheRoot != null && IsAnyTransactionLockHeld(cacheRoot, callerOwnedLockPath);
        bool canCleanBackups = backupsReadable && !txLockHeld;
        var installedSelections = GetInstalledPackIdentities(root);
        var registeredModFolders = GetRegisteredModFolders(root);
        var staleCutoffUtc = DateTime.UtcNow.AddMinutes(-15);

        long freed = 0L;

        // 1. Clean <installRoot>/mods/.unpacked/.zip-hashes.json.*.partial and unregistered *.bak* / *.tmp in mods/
        var modsRoot = Path.Combine(root, "mods");
        if (!txLockHeld && Directory.Exists(modsRoot) && !IsReparsePoint(modsRoot))
        {
            var unpacked = Path.Combine(modsRoot, ".unpacked");
            if (Directory.Exists(unpacked) && !IsReparsePoint(unpacked))
            {
                foreach (var f in SafeGetFiles(unpacked))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ZipHashPartial.IsMatch(f.Name)) continue;
                    if (staleOnly && f.LastWriteTimeUtc >= DateTime.UtcNow.AddMinutes(-2)) continue;
                    freed += TryDeleteFile(f);
                }
            }

            try
            {
                foreach (var entry in new DirectoryInfo(modsRoot).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    var fullEntry = Path.TrimEndingDirectorySeparator(entry.FullName);
                    if (entry is DirectoryInfo subDir && !subDir.Name.Equals(".unpacked", StringComparison.OrdinalIgnoreCase) &&
                        IsBackupName(subDir.Name) && !registeredModFolders.Contains(fullEntry))
                    {
                        if (staleOnly && subDir.LastWriteTimeUtc >= staleCutoffUtc) continue;
                        freed += TryDeleteDirectorySafe(modsRoot, subDir.FullName);
                    }
                    else if (entry is FileInfo modFile && (IsBackupName(modFile.Name) || IsStaleTempFileName(modFile.Name)))
                    {
                        if (staleOnly && modFile.LastWriteTimeUtc >= staleCutoffUtc) continue;
                        freed += TryDeleteFile(modFile);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        if (cacheRoot == null || !Directory.Exists(cacheRoot) || IsReparsePoint(cacheRoot))
            return freed;

        // 2. Clean completed/superseded backups (never non-terminal or transaction-referenced backups; fail-closed on corrupt tx JSON!)
        var backupsDir = Path.Combine(cacheRoot, "backups");
        if (canCleanBackups && Directory.Exists(backupsDir) && !IsReparsePoint(backupsDir))
        {
            try
            {
                foreach (var entry in new DirectoryInfo(backupsDir).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (entry is DirectoryInfo bDir)
                    {
                        var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bDir.FullName));
                        if (protectedBackups.Contains(norm) || IsPreservedTree(norm)) continue;
                        if (!IsCompletedOrDeletingBackup(bDir, completedBackups, staleOnly, staleCutoffUtc)) continue;
                        freed += TryDeleteDirectorySafe(cacheRoot, bDir.FullName);
                    }
                    else if (entry is FileInfo bFile)
                    {
                        if (preservedFiles.Contains(Path.GetFullPath(bFile.FullName))) continue;
                        if (staleOnly && bFile.LastWriteTimeUtc >= staleCutoffUtc) continue;
                        freed += TryDeleteFile(bFile);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // 3. Clean unlocked stale partial files in appearance-downloads
        var appearanceDownloads = Path.Combine(cacheRoot, "appearance-downloads");
        if (Directory.Exists(appearanceDownloads) && !IsReparsePoint(appearanceDownloads) && !IsLockHeld(Path.Combine(appearanceDownloads, "download.lock")))
        {
            foreach (var f in SafeGetFiles(appearanceDownloads))
            {
                ct.ThrowIfCancellationRequested();
                if (f.Name.Equals("download.lock", StringComparison.OrdinalIgnoreCase)) continue;
                if (preservedFiles.Contains(Path.GetFullPath(f.FullName))) continue;
                if (!(f.Name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || IsStaleTempFileName(f.Name) || IsBackupName(f.Name)))
                    continue;
                if (staleOnly && f.LastWriteTimeUtc >= staleCutoffUtc) continue;
                freed += TryDeleteFile(f);
            }
        }

        // 4. Clean superseded / abandoned / deleting-* exports
        var exportsDir = Path.Combine(cacheRoot, "exports");
        if (!txLockHeld && Directory.Exists(exportsDir) && !IsReparsePoint(exportsDir))
        {
            var reclaimableExports = FindReclaimableExports(exportsDir, installedSelections, reclaimUnpreservedExports);
            try
            {
                foreach (var entry in new DirectoryInfo(exportsDir).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (entry is DirectoryInfo eDir)
                    {
                        var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(eDir.FullName));
                        if (!reclaimableExports.Contains(norm) || IsPreservedTree(norm)) continue;
                        if (staleOnly && !eDir.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase) && eDir.LastWriteTimeUtc >= staleCutoffUtc)
                            continue;
                        freed += TryDeleteDirectorySafe(cacheRoot, eDir.FullName);
                    }
                    else if (entry is FileInfo eFile && (IsStaleTempFileName(eFile.Name) || IsBackupName(eFile.Name)))
                    {
                        if (staleOnly && eFile.LastWriteTimeUtc >= staleCutoffUtc) continue;
                        freed += TryDeleteFile(eFile);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // 5. Clean retired gameplay catalogs and temporary gameplay/imports/*.zip copies (skip when transactions/*.lock held!)
        var gameplayDir = Path.Combine(cacheRoot, "gameplay");
        if (!txLockHeld && Directory.Exists(gameplayDir) && !IsReparsePoint(gameplayDir))
        {
            try
            {
                foreach (var entry in new DirectoryInfo(gameplayDir).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (entry is DirectoryInfo gDir)
                    {
                        if (!string.Equals(gDir.Name, HellForgeGameplayCatalog.Pinned.Id, StringComparison.Ordinal))
                        {
                            if (staleOnly && gDir.LastWriteTimeUtc >= staleCutoffUtc) continue;
                            freed += TryDeleteDirectorySafe(cacheRoot, gDir.FullName);
                        }
                        else
                        {
                            var importsDir = Path.Combine(gDir.FullName, "imports");
                            if (Directory.Exists(importsDir) && !IsReparsePoint(importsDir))
                            {
                                if (!staleOnly)
                                {
                                    freed += TryDeleteDirectorySafe(cacheRoot, importsDir);
                                }
                                else
                                {
                                    foreach (var importEntry in new DirectoryInfo(importsDir).EnumerateFileSystemInfos())
                                    {
                                        ct.ThrowIfCancellationRequested();
                                        if (importEntry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                                        if (importEntry.LastWriteTimeUtc >= staleCutoffUtc) continue;
                                        if (importEntry is FileInfo importFile)
                                            freed += TryDeleteFile(importFile);
                                        else if (importEntry is DirectoryInfo importSubDir)
                                            freed += TryDeleteDirectorySafe(cacheRoot, importSubDir.FullName);
                                    }
                                    try
                                    {
                                        var dirInfo = new DirectoryInfo(importsDir);
                                        if (dirInfo.LastWriteTimeUtc < staleCutoffUtc && !Directory.EnumerateFileSystemEntries(importsDir).Any())
                                            Directory.Delete(importsDir, recursive: false);
                                    }
                                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                                }
                            }

                            foreach (var f in SafeGetFiles(gDir.FullName))
                            {
                                if (IsStaleTempFileName(f.Name) || IsBackupName(f.Name))
                                {
                                    if (staleOnly && f.LastWriteTimeUtc >= staleCutoffUtc) continue;
                                    freed += TryDeleteFile(f);
                                }
                            }
                        }
                    }
                    else if (entry is FileInfo gFile)
                    {
                        if (staleOnly && gFile.LastWriteTimeUtc >= staleCutoffUtc) continue;
                        freed += TryDeleteFile(gFile);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // 6. Clean installer temp/partial files + stray top-level *.bak* / deleting-* in cacheRoot
        var installersDir = Path.Combine(cacheRoot, "installers");
        if (Directory.Exists(installersDir) && !IsReparsePoint(installersDir) && !IsLockHeld(Path.Combine(installersDir, "forge-setup.lock")))
        {
            foreach (var f in SafeGetFiles(installersDir))
            {
                ct.ThrowIfCancellationRequested();
                if (f.Name.Equals("forge-setup.lock", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsStaleTempFileName(f.Name) || IsBackupName(f.Name))
                {
                    if (staleOnly && f.LastWriteTimeUtc >= staleCutoffUtc) continue;
                    freed += TryDeleteFile(f);
                }
            }
        }

        try
        {
            foreach (var entry in new DirectoryInfo(cacheRoot).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (entry.Name is "backups" or "appearance-downloads" or "exports" or "gameplay" or "installers" or "transactions" or "accounts")
                    continue;
                if (entry is DirectoryInfo extraDir && (IsBackupName(extraDir.Name) || extraDir.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase)))
                {
                    freed += TryDeleteDirectorySafe(cacheRoot, extraDir.FullName);
                }
                else if (entry is FileInfo extraFile && (IsBackupName(extraFile.Name) || IsStaleTempFileName(extraFile.Name)))
                {
                    freed += TryDeleteFile(extraFile);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return freed;
    }

    static HashSet<string> FindReclaimableExports(
        string exportsDir,
        HashSet<string> installedSelections,
        bool reclaimUnpreservedExports = false)
    {
        var reclaimable = new HashSet<string>(PlatformPaths.Comparer);
        try
        {
            var validByMode = new List<(DirectoryInfo Dir, HellForgePack.Selection Selection, string Identity)>();
            foreach (var dir in new DirectoryInfo(exportsDir).EnumerateDirectories())
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir.FullName));
                if (dir.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase) || IsBackupName(dir.Name))
                {
                    reclaimable.Add(norm);
                    continue;
                }

                var receiptPath = Path.Combine(dir.FullName, HellForgePack.ReceiptName);
                if (!File.Exists(receiptPath))
                {
                    // Unfinished / abandoned staging export without a completed receipt
                    reclaimable.Add(norm);
                    continue;
                }

                try
                {
                    var sel = HellForgePack.ReadStorageReceipt(dir.FullName);
                    if (sel != null)
                        validByMode.Add((dir, sel, HellForgePack.SelectionIdentity(sel)));
                    else
                        reclaimable.Add(norm);
                }
                catch
                {
                    reclaimable.Add(norm);
                }
            }

            // Any receipted export that is already installed in mods/ (duplicate copy) or superseded by a newer export of the same mode is reclaimable.
            // Under low-space capacity preflight (reclaimUnpreservedExports=true), launcher-named staging exports not referenced by plan.Local are also reclaimable.
            foreach (var group in validByMode.GroupBy(x => x.Selection.Mode, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = group.OrderByDescending(x => x.Dir.LastWriteTimeUtc).ThenBy(x => x.Dir.Name, StringComparer.Ordinal).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    var item = ordered[i];
                    var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Dir.FullName));
                    bool isLauncherStaging = LauncherExportName.IsMatch(item.Dir.Name);
                    if (installedSelections.Contains(item.Identity) || (i > 0 && isLauncherStaging) || (reclaimUnpreservedExports && isLauncherStaging))
                        reclaimable.Add(norm);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return reclaimable;
    }

    static bool TryGetBackupStates(string cacheRoot, out HashSet<string> protectedPaths, out HashSet<string> completedPaths)
    {
        protectedPaths = new HashSet<string>(PlatformPaths.Comparer);
        completedPaths = new HashSet<string>(PlatformPaths.Comparer);
        var txDir = Path.Combine(cacheRoot, "transactions");
        if (!Directory.Exists(txDir)) return true;
        if (IsReparsePoint(txDir)) return false;

        try
        {
            foreach (var path in Directory.EnumerateFiles(txDir))
            {
                var fileName = Path.GetFileName(path);
                if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || IsStaleTempFileName(fileName))
                    continue;

                try
                {
                    if (IsReparsePoint(path)) return false;
                    var len = new FileInfo(path).Length;
                    if (len <= 0 || len > 16384) return false;
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

                    string? backup = null;
                    string? state = null;
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name.Equals("Backup", StringComparison.OrdinalIgnoreCase))
                        {
                            if (prop.Value.ValueKind != JsonValueKind.String) return false;
                            backup = prop.Value.GetString();
                        }
                        else if (prop.Name.Equals("State", StringComparison.OrdinalIgnoreCase))
                        {
                            if (prop.Value.ValueKind != JsonValueKind.String) return false;
                            state = prop.Value.GetString();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(backup)) return false;
                    var normBackup = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backup));
                    // Terminal completed records ("applied" / "restored") provide positive completion evidence;
                    // non-terminal states ("pending", "outcome-unknown", "recovery-needed", missing) — including
                    // retired older-catalog recovery records (*.gameplay.json.retired-*) — remain strictly protected.
                    if (state is "applied" or "restored")
                        completedPaths.Add(normBackup);
                    else
                        protectedPaths.Add(normBackup);
                }
                catch
                {
                    // Fail-closed on backups/ if any active or retired transaction record is unreadable, oversized, or corrupt.
                    return false;
                }
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsAnyTransactionLockHeld(string cacheRoot, string? callerOwnedLockPath = null)
    {
        var txDir = Path.Combine(cacheRoot, "transactions");
        if (!Directory.Exists(txDir)) return false;
        if (IsReparsePoint(txDir)) return true;
        string? normalizedOwned = null;
        if (!string.IsNullOrWhiteSpace(callerOwnedLockPath))
        {
            try { normalizedOwned = Path.GetFullPath(callerOwnedLockPath); } catch { }
        }
        try
        {
            foreach (var lockFile in Directory.EnumerateFiles(txDir, "*.lock"))
            {
                if (IsReparsePoint(lockFile))
                    return true;
                if (normalizedOwned != null && string.Equals(Path.GetFullPath(lockFile), normalizedOwned, PlatformPaths.Comparison))
                    continue;
                if (IsLockHeld(lockFile))
                    return true;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    static HashSet<string> GetInstalledPackIdentities(string installRoot)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var mods = Path.Combine(installRoot, "mods");
        if (!Directory.Exists(mods) || IsReparsePoint(mods)) return set;
        try
        {
            foreach (var dir in new DirectoryInfo(mods).EnumerateDirectories())
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                try
                {
                    if (HellForgePack.ReadStorageReceipt(dir.FullName) is { } sel)
                        set.Add(HellForgePack.SelectionIdentity(sel));
                }
                catch { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return set;
    }

    static HashSet<string> GetRegisteredModFolders(string installRoot)
    {
        var folders = new HashSet<string>(PlatformPaths.Comparer);
        var modsDir = Path.Combine(installRoot, "mods");
        var modsJson = Path.Combine(modsDir, "hellforge-mods.json");
        if (!File.Exists(modsJson) || IsReparsePoint(modsJson)) return folders;
        try
        {
            if (new FileInfo(modsJson).Length > 2 * 1024 * 1024) return folders;
            using var doc = JsonDocument.Parse(File.ReadAllText(modsJson));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return folders;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("folder", out var fProp) && fProp.ValueKind == JsonValueKind.String && fProp.GetString() is { Length: > 0 } f)
                            folders.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(f) ? f : Path.Combine(modsDir, f))));
                    }
                }
            }
        }
        catch { }
        return folders;
    }

    static bool IsCompletedOrDeletingBackup(DirectoryInfo dir, HashSet<string> completedBackups, bool staleOnly = false, DateTime staleCutoffUtc = default)
    {
        if (dir.Name.StartsWith("deleting-", StringComparison.OrdinalIgnoreCase)) return true;
        if (staleOnly && dir.Name.StartsWith("gameplay-", StringComparison.OrdinalIgnoreCase) && dir.LastWriteTimeUtc >= staleCutoffUtc)
            return false;
        var norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir.FullName));
        if (completedBackups.Contains(norm)) return true;
        var outcomePath = Path.Combine(dir.FullName, "outcome.json");
        try
        {
            if (!File.Exists(outcomePath) || IsReparsePoint(outcomePath) || new FileInfo(outcomePath).Length > 16384)
                return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(outcomePath));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("outcome", out var o) &&
                   o.ValueKind == JsonValueKind.String &&
                   o.GetString() is "applied" or "restored";
        }
        catch { return false; }
    }

    static bool IsBackupName(string name) =>
        name.Contains(".bak", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("~", StringComparison.Ordinal);

    static bool IsStaleTempFileName(string name) =>
        name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    static bool IsLockHeld(string lockFilePath)
    {
        if (!File.Exists(lockFilePath)) return false;
        try
        {
            using var probe = new FileStream(lockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    static IEnumerable<FileInfo> EnumerateSafeFiles(string folder)
    {
        if (!Directory.Exists(folder) || IsReparsePoint(folder)) yield break;
        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true
            });
        }
        catch { yield break; }

        foreach (var file in files)
            yield return file;
    }

    static IReadOnlyList<FileInfo> SafeGetFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder) || IsReparsePoint(folder)) return [];
            return new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true
            }).ToList();
        }
        catch { return []; }
    }

    static long SumDirectoryBytes(string folder)
    {
        long sum = 0;
        foreach (var file in EnumerateSafeFiles(folder))
        {
            try { sum = checked(sum + file.Length); }
            catch (IOException) { }
        }
        return sum;
    }

    static long TryDeleteFile(FileInfo file)
    {
        try
        {
            FileSafety.RejectLink(file.FullName);
            var len = file.Length;
            if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                file.Attributes = FileAttributes.Normal;
            file.Delete();
            return len;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return 0L;
        }
    }

    static long TryDeleteDirectorySafe(string confiningRoot, string targetFolder)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(confiningRoot));
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder));
            if (folder.Equals(root, PlatformPaths.Comparison) ||
                !folder.StartsWith(root + Path.DirectorySeparatorChar, PlatformPaths.Comparison))
                return 0L;

            FileSafety.RejectLink(folder);
            if (!Directory.Exists(folder)) return 0L;

            long bytes = 0L;
            var dirs = new List<string>();
            var files = new List<FileInfo>();
            var queue = new Queue<string>();
            queue.Enqueue(folder);

            while (queue.TryDequeue(out var current))
            {
                FileSafety.RejectLink(current);
                dirs.Add(current);
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    FileSafety.RejectLink(entry);
                    if (Directory.Exists(entry)) queue.Enqueue(entry);
                    else files.Add(new FileInfo(entry));
                }
            }

            foreach (var f in files)
                bytes += TryDeleteFile(f);

            for (int i = dirs.Count - 1; i >= 0; i--)
            {
                try { Directory.Delete(dirs[i], recursive: false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return 0L;
        }
    }
}
