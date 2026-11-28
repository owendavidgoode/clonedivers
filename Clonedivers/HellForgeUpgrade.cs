using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clonedivers;

internal static partial class HellForgeSetup
{
    internal static PackFile PreviousInstaller040 => new() { Name = "HellForge-Setup.exe", Url = "https://github.com/SkyeShade/HellForge/releases/download/v0.4.0-beta/HellForge-Setup.exe",
        Size = 526467508, Sha256 = "97dbdd07638e5198b98bf2ad74a1ba907c53f569fcdcdcff330e6c74359f04d7" };
    internal static PackFile PreviousInstaller030 => new() { Name = "HellForge-Setup.exe", Url = "https://github.com/SkyeShade/HellForge/releases/download/v0.3.0-beta/HellForge-Setup.exe",
        Size = 315829864, Sha256 = "b815655998d6b4b31cd12e821460cd0bbde5e360c2886381ca59e051a106a7e4" };
    internal static PackFile PreviousInstaller => PreviousInstaller040;
    internal static bool Is040(string? version) => version != null && System.Text.RegularExpressions.Regex.IsMatch(version, @"^0\.4\.0-beta(?: \([0-9a-f]{7,40}\))?$");
    internal static bool Is030(string? version) => version != null && System.Text.RegularExpressions.Regex.IsMatch(version, @"^0\.3\.0-beta(?: \([0-9a-f]{7,40}\))?$");
    internal static bool IsPrevious(string? version) => Is040(version) || Is030(version);
    internal static PackFile PreviousInstallerFor(string? version) => Is030(version) ? PreviousInstaller030 : PreviousInstaller040;
    internal static string PreviousShortLabel(string? version) => Is030(version) ? "0.3.0" : "0.4.0";
    internal static bool MatchesPreviousRuntime(HellForgeStatus s) => s.Build == HellForgePack.SupportedBuild &&
        ((Is040(s.Version) && s.Engine == "0.1.47" && s.ModSdk == 16) ||
         (Is030(s.Version) && s.Engine == "0.1.41" && s.ModSdk == 15));

    sealed record ProtectedFile(string Relative, string Sha256);
    sealed record UpgradeIntent(string Root, string PreviousInstaller, string Backup, string ProtectedSha256, string State,
        string? ModListSha256 = null, string? PreviousVersion = null);

    static string UpgradeRecord(string root, string? cache = null) => Path.Combine(cache ?? CacheRoot, "updates",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).ToLowerInvariant() + ".json");

    internal static string? UpgradeProblem(string root)
    {
        try
        {
            var file = UpgradeRecord(root); HellForgeGameplayCatalog.RejectLinks(file);
            if (!File.Exists(file)) return null;
            if (new FileInfo(file).Length > 16384) return "Forge upgrade recovery record needs review.";
            var intent = JsonSerializer.Deserialize<UpgradeIntent>(File.ReadAllText(file), HellForgePack.Json) ?? throw new InvalidDataException("Empty Forge recovery record.");
            if (!Path.GetFullPath(root).Equals(intent.Root, StringComparison.OrdinalIgnoreCase) || intent.State is not ("pending" or "applied" or "restored") || !FileSafety.IsSha256(intent.ProtectedSha256) ||
                intent.ModListSha256 != null && !FileSafety.IsSha256(intent.ModListSha256))
                throw new InvalidDataException("Forge recovery record does not match this installation.");
            return intent.State == "pending"
                ? "A Forge upgrade was interrupted. Restore the previous Forge application before continuing." : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return "Forge upgrade recovery needs review: " + error.Message; }
    }

    static void SaveUpgrade(string file, UpgradeIntent intent)
    {
        HellForgeGameplayCatalog.RejectLinks(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (var output = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(output, intent, HellForgePack.Json); output.Flush(flushToDisk: true); }
        File.Move(file + ".tmp", file, true);
    }

    static void RequireUpgradeIdle(string root)
    {
        if (Game.IsRunning() || HellForgeAccount.CaptureBlock() != null) throw new InvalidOperationException("End HD2/account sync before upgrading Forge.");
        foreach (var name in new[] { "HellForge.Session", "HellForge.Cli", "HellForge", "HellForge.Launcher" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    if (process.HasExited) continue;
                    if (name is "HellForge.Session" or "HellForge.Cli") throw new InvalidOperationException("Wait for the Forge session/CLI to finish before upgrading.");
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (path == null || Within(Path.GetFullPath(path), root)) throw new IOException("Close the official HellForge window, then continue. Your game copy and account will be kept.");
                    }
                    catch (System.ComponentModel.Win32Exception) { throw new IOException("Close the official HellForge window before upgrading."); }
                }
    }

    static List<string> ProtectedPaths(string root)
    {
        // Note: HellForge 0.5's SetupForm.UnpackQuietly calls KeptVersions.StartModList, resetting mods/hellforge-mods.json
        // to bundled mods only. Snapshot mods/hellforge-mods.json separately so we restore the player's mod selection
        // with importDone=true on success and restore exact original bytes on rollback.
        var files = new List<string>(); long bytes = 0;
        foreach (var relative in new[] { "hellforge-install.json", "launcher-settings.json" })
            if (File.Exists(Path.Combine(root, relative))) files.Add(relative);
        foreach (var name in new[] { "profiles", "account-snapshot" })
        {
            var directory = Path.Combine(root, name); HellForgeGameplayCatalog.RejectLinks(directory);
            if (!Directory.Exists(directory)) continue;
            var queue = new Queue<string>(); queue.Enqueue(directory);
            while (queue.TryDequeue(out var folder))
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    FileSafety.RejectLink(path);
                    if (Directory.Exists(path)) { queue.Enqueue(path); continue; }
                    bytes = checked(bytes + new FileInfo(path).Length);
                    if (files.Count > 10000 || bytes > 256L * 1024 * 1024) throw new IOException("Account backup exceeds the supported size; nothing was upgraded.");
                    files.Add(Path.GetRelativePath(root, path));
                }
        }
        return files;
    }

    static async Task<(List<ProtectedFile> Files, string? ModListSha256)> SnapshotUpgrade(string root, string backup, CancellationToken ct)
    {
        var records = new List<ProtectedFile>();
        foreach (var relative in ProtectedPaths(root))
        {
            var source = Path.Combine(root, relative); HellForgeGameplayCatalog.RejectLinks(source);
            var target = Path.Combine(backup, "protected", relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var hash = await HellForgePack.HashAsync(source, ct); File.Copy(source, target, false);
            if (await HellForgePack.HashAsync(target, ct) != hash || await HellForgePack.HashAsync(source, ct) != hash) throw new IOException("Account state changed during backup. Retry when idle.");
            records.Add(new(relative, hash));
        }
        string? modListSha = null;
        var modListPath = Path.Combine(root, "mods", "hellforge-mods.json");
        HellForgeGameplayCatalog.RejectLinks(modListPath);
        if (File.Exists(modListPath))
        {
            if (new FileInfo(modListPath).Length > 4 * 1024 * 1024) throw new IOException("Mod preferences exceed the supported backup size.");
            var modListBackup = Path.Combine(backup, "modlist", "hellforge-mods.json");
            Directory.CreateDirectory(Path.GetDirectoryName(modListBackup)!);
            modListSha = await HellForgePack.HashAsync(modListPath, ct);
            File.Copy(modListPath, modListBackup, false);
            if (await HellForgePack.HashAsync(modListBackup, ct) != modListSha || await HellForgePack.HashAsync(modListPath, ct) != modListSha)
                throw new IOException("Mod list changed during backup. Retry when idle.");
        }
        await File.WriteAllTextAsync(Path.Combine(backup, "protected.json"), JsonSerializer.Serialize(records, HellForgePack.Json), ct);
        return (records, modListSha);
    }

    internal static long ProtectedBackupBytes(string root)
    {
        long total = ProtectedPaths(root).Sum(relative =>
        {
            var path = Path.Combine(root, relative); HellForgeGameplayCatalog.RejectLinks(path);
            return new FileInfo(path).Length;
        });
        var modList = Path.Combine(root, "mods", "hellforge-mods.json");
        HellForgeGameplayCatalog.RejectLinks(modList);
        if (File.Exists(modList)) total = checked(total + new FileInfo(modList).Length);
        return total;
    }

    static async Task VerifyProtected(string root, IEnumerable<ProtectedFile> files, CancellationToken ct)
    {
        foreach (var file in files)
        {
            if (Path.IsPathFullyQualified(file.Relative) || file.Relative.Split('/', '\\').Any(p => p is ".." || p.Contains(':')) || !FileSafety.IsSha256(file.Sha256))
                throw new InvalidDataException("Invalid Forge account backup record.");
            var path = Path.Combine(root, file.Relative); HellForgeGameplayCatalog.RejectLinks(path);
            if (!File.Exists(path) || await HellForgePack.HashAsync(path, ct) != file.Sha256)
                throw new IOException("Forge account/settings changed during the application update. Review the retained private backup; no profile backup was replayed automatically.");
        }
    }

    static async Task RestoreModListFor05Async(string root, string backup, string? modListSha256)
    {
        if (modListSha256 == null) return;
        var saved = Path.Combine(backup, "modlist", "hellforge-mods.json");
        HellForgeGameplayCatalog.RejectLinks(saved);
        if (!File.Exists(saved) || await HellForgePack.HashAsync(saved, CancellationToken.None) != modListSha256)
            throw new InvalidDataException("Saved mod-list snapshot failed verification.");
        var target = Path.Combine(root, "mods", "hellforge-mods.json");
        var temp = target + ".upgrade-restore.tmp";
        HellForgeGameplayCatalog.RejectLinks(target); HellForgeGameplayCatalog.RejectLinks(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var raw = await File.ReadAllBytesAsync(saved, CancellationToken.None);
        byte[] outputBytes = raw;
        try
        {
            var text = Encoding.UTF8.GetString(raw).TrimStart('\uFEFF');
            if (JsonNode.Parse(text) is JsonObject obj)
            {
                obj["importDone"] = true;
                outputBytes = JsonSerializer.SerializeToUtf8Bytes(obj, HellForgePack.Json);
            }
        }
        catch (JsonException) { }
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fs.WriteAsync(outputBytes, CancellationToken.None);
                fs.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static async Task RestoreOriginalModListAsync(string root, string backup, string? modListSha256)
    {
        if (modListSha256 == null) return;
        var saved = Path.Combine(backup, "modlist", "hellforge-mods.json");
        HellForgeGameplayCatalog.RejectLinks(saved);
        if (!File.Exists(saved) || await HellForgePack.HashAsync(saved, CancellationToken.None) != modListSha256)
            throw new InvalidDataException("Saved mod-list snapshot failed verification.");
        var target = Path.Combine(root, "mods", "hellforge-mods.json");
        var temp = target + ".rollback.tmp";
        HellForgeGameplayCatalog.RejectLinks(target); HellForgeGameplayCatalog.RejectLinks(temp);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            File.Copy(saved, temp, overwrite: true);
            if (await HellForgePack.HashAsync(temp, CancellationToken.None) != modListSha256)
                throw new IOException("Rolled-back mod list failed SHA-256 verification.");
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static async Task ReuseKeptInstallerInRootAsync(string root, string cache, PackFile artifact, CancellationToken ct,
        Func<string, long?>? availableBytes = null)
    {
        var keptDir = Path.Combine(root, "installer");
        if (!Directory.Exists(keptDir)) return;
        try
        {
            HellForgeGameplayCatalog.RejectLinks(keptDir);
            var target = InstallerPath(cache, artifact);
            if (File.Exists(target) && new FileInfo(target).Length == artifact.Size &&
                string.Equals(await HellForgePack.HashAsync(target, ct), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var exe in Directory.EnumerateFiles(keptDir, "*.exe", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                ct.ThrowIfCancellationRequested();
                FileSafety.RejectLink(exe);
                var info = new FileInfo(exe);
                if (info.Length != artifact.Size) continue;
                if (!string.Equals(await HellForgePack.HashAsync(exe, ct), artifact.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                if ((availableBytes ?? HellForgePack.AvailableExportBytes)(cache) is long free && free < artifact.Size + CacheReserve)
                    return;
                Directory.CreateDirectory(cache);
                var temp = target + ".kept-" + Guid.NewGuid().ToString("N") + ".tmp";
                HellForgeGameplayCatalog.RejectLinks(temp);
                try
                {
                    await HellForgePack.CopyLocalAsync(exe, temp, artifact.Size, ct);
                    if (!string.Equals(await HellForgePack.HashAsync(temp, ct), artifact.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                    File.Move(temp, target, overwrite: true);
                    return;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
    }

    internal static async Task UpdateAsync(string root, IProgress<ForgeSetupProgress>? progress, CancellationToken ct,
        string? cache = null, PackFile? nextArtifact = null, PackFile? previousArtifact = null,
        Func<string, string, Task>? unpack = null, Func<CancellationToken, Task<HellForgeStatus>>? status = null, Action<string>? idle = null,
        Func<string, long?>? cacheAvailableBytes = null)
    {
        bool defaultCache = cache == null;
        root = HellForgeCommands.ValidateRoot(root); cache ??= CacheRoot; unpack ??= UnpackAsync; idle ??= RequireUpgradeIdle;
        status ??= token => new HellForgeClient(root).StatusAsync(token);
        idle(root);
        var before = await status(ct);
        if (before.Running) throw new InvalidOperationException("End the current Forge session before upgrading.");
        if (SupportsVersion(before.Version) && before.CompatibilityProblem == null) return;
        if (!MatchesPreviousRuntime(before))
            throw new InvalidDataException("This upgrade supports verified 0.4.0-beta (engine 0.1.47 / SDK 16) or 0.3.0-beta (engine 0.1.41 / SDK 15) installations only; review another runtime before changing it.");
        var record = UpgradeRecord(root, cache); HellForgeGameplayCatalog.RejectLinks(record);
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        using var lease = new FileStream(record + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(record) && JsonSerializer.Deserialize<UpgradeIntent>(File.ReadAllText(record), HellForgePack.Json)?.State == "pending")
            throw new IOException("Restore the interrupted Forge upgrade before retrying.");
        nextArtifact ??= Installer; previousArtifact ??= PreviousInstallerFor(before.Version);
        foreach (var artifact in new[] { nextArtifact, previousArtifact }.DistinctBy(a => a.Sha256))
            await ReuseKeptInstallerInRootAsync(root, cache, artifact, ct, cacheAvailableBytes);
        if (defaultCache)
            foreach (var artifact in new[] { nextArtifact, previousArtifact }.DistinctBy(a => a.Sha256))
                await ReuseInstallerAsync(InstallCache(root), cache, artifact, ct, cacheAvailableBytes);
        await RequireCacheSpaceAsync(cache, [nextArtifact, previousArtifact], ProtectedBackupBytes(root), ct, cacheAvailableBytes);
        var combinedTotal = checked(nextArtifact.Size + previousArtifact.Size);
        var prevShort = PreviousShortLabel(before.Version);
        var nextProgress = progress == null ? null : new SyncProgress<ForgeSetupProgress>(p =>
            progress.Report(new("Downloading Forge 0.5.0 update…", p.Total > 0 ? p.Done : 0, combinedTotal, p.CanCancel)));
        var next = await DownloadAsync(cache, nextProgress, ct, nextArtifact);
        var prevProgress = progress == null ? null : new SyncProgress<ForgeSetupProgress>(p =>
            progress.Report(new($"Downloading rollback backup ({prevShort})…", checked(nextArtifact.Size + (p.Total > 0 ? p.Done : 0)), combinedTotal, p.CanCancel)));
        var previous = await DownloadAsync(cache, prevProgress, ct, previousArtifact);
        idle(root); ct.ThrowIfCancellationRequested();
        await RequireCacheSpaceAsync(cache, [], ProtectedBackupBytes(root), ct, cacheAvailableBytes);
        var backup = Path.Combine(cache, "backups", "forge-upgrade-" + Guid.NewGuid().ToString("N"));
        HellForgeGameplayCatalog.RejectLinks(backup); Directory.CreateDirectory(backup);
        var (files, modListSha) = await SnapshotUpgrade(root, backup, ct);
        using var nextLock = File.Open(next, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var previousLock = File.Open(previous, FileMode.Open, FileAccess.Read, FileShare.Read);
        var intent = new UpgradeIntent(root, previous, backup, await HellForgePack.HashAsync(Path.Combine(backup, "protected.json"), ct), "pending",
            ModListSha256: modListSha, PreviousVersion: before.Version);
        idle(root); ct.ThrowIfCancellationRequested(); SaveUpgrade(record, intent);
        progress?.Report(new("Upgrading Forge to 0.5.0… Account, mods and game copy are retained. Please wait."));
        try
        {
            await unpack(next, root); // official installer owns every application write
            HellForgeRelease.SyncShippedBundledMods(root);
            await VerifyProtected(root, files, CancellationToken.None);
            await RestoreModListFor05Async(root, backup, modListSha);
            var after = await status(CancellationToken.None);
            if (!SupportsVersion(after.Version) || after.Build != before.Build || after.Engine != HellForgeRelease.Engine || after.ModSdk != HellForgeRelease.Sdk)
                throw new IOException("The official Forge update did not report the expected release/engine/SDK/build.");
            SaveUpgrade(record, intent with { State = "applied" });
        }
        catch (Exception error)
        {
            if (error is HellForgeCommandOutcomeUnknownException) throw;
            try
            {
                idle(root);
                await unpack(previous, root);
                HellForgeRelease.SyncShippedBundledMods(root);
                await RestoreOriginalModListAsync(root, backup, modListSha);
                await VerifyProtected(root, files, CancellationToken.None);
                var restored = await status(CancellationToken.None);
                if (!IsPrevious(restored.Version) || (Is030(before.Version) ? !Is030(restored.Version) : !Is040(restored.Version)))
                    throw new IOException("Previous application version was not restored.");
                SaveUpgrade(record, intent with { State = "restored" });
            }
            catch (Exception recovery) { throw new IOException("Forge upgrade recovery is pending. Retained account backup: " + backup, new AggregateException(error, recovery)); }
            throw new IOException("Forge upgrade failed; the previous official application was restored. " + error.Message, error);
        }
    }

    internal static async Task RecoverUpgradeAsync(string root, CancellationToken ct, string? cache = null, PackFile? previousArtifact = null,
        Func<string, string, Task>? unpack = null, Func<CancellationToken, Task<HellForgeStatus>>? status = null, Action<string>? idle = null)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); cache ??= CacheRoot;
        unpack ??= UnpackAsync; idle ??= RequireUpgradeIdle; status ??= token => new HellForgeClient(root).StatusAsync(token);
        idle(root);
        var record = UpgradeRecord(root, cache); HellForgeGameplayCatalog.RejectLinks(record);
        if (new FileInfo(record).Length > 16384) throw new InvalidDataException("Oversized Forge recovery record.");
        using var lease = new FileStream(record + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var intent = JsonSerializer.Deserialize<UpgradeIntent>(File.ReadAllText(record), HellForgePack.Json) ?? throw new InvalidDataException("Empty upgrade recovery record.");
        previousArtifact ??= intent.PreviousVersion != null
            ? PreviousInstallerFor(intent.PreviousVersion)
            : intent.PreviousInstaller.EndsWith(PreviousInstaller030.Sha256 + ".exe", StringComparison.OrdinalIgnoreCase)
                ? PreviousInstaller030
                : PreviousInstaller040;
        var expectedInstaller = Path.Combine(cache, "HellForge-Setup-" + previousArtifact.Sha256 + ".exe");
        if (!root.Equals(intent.Root, StringComparison.OrdinalIgnoreCase) || intent.State != "pending" || intent.PreviousInstaller != expectedInstaller ||
            Path.GetDirectoryName(Path.GetFullPath(intent.Backup)) != Path.Combine(cache, "backups") || !FileSafety.IsSha256(intent.ProtectedSha256) ||
            intent.ModListSha256 != null && !FileSafety.IsSha256(intent.ModListSha256))
            throw new InvalidDataException("Forge recovery does not match this installation.");
        HellForgeGameplayCatalog.RejectLinks(intent.Backup); HellForgeGameplayCatalog.RejectLinks(expectedInstaller);
        var metadata = Path.Combine(intent.Backup, "protected.json");
        if (new FileInfo(metadata).Length > 4 * 1024 * 1024 || await HellForgePack.HashAsync(metadata, ct) != intent.ProtectedSha256)
            throw new InvalidDataException("Forge recovery metadata changed. Review the retained backup.");
        var files = JsonSerializer.Deserialize<List<ProtectedFile>>(await File.ReadAllTextAsync(metadata, ct), HellForgePack.Json) ?? throw new InvalidDataException("Empty protected account inventory.");
        if (new FileInfo(expectedInstaller).Length != previousArtifact.Size || await HellForgePack.HashAsync(expectedInstaller, ct) != previousArtifact.Sha256)
            throw new InvalidDataException("Retained previous official installer failed verification.");
        using var pinned = File.Open(expectedInstaller, FileMode.Open, FileAccess.Read, FileShare.Read);
        ct.ThrowIfCancellationRequested();
        await unpack(expectedInstaller, root);
        HellForgeRelease.SyncShippedBundledMods(root);
        await RestoreOriginalModListAsync(root, intent.Backup, intent.ModListSha256);
        await VerifyProtected(root, files, CancellationToken.None);
        var restored = await status(CancellationToken.None);
        if (!IsPrevious(restored.Version) || (intent.PreviousVersion != null && (Is030(intent.PreviousVersion) ? !Is030(restored.Version) : !Is040(restored.Version))))
            throw new IOException("Previous Forge version was not restored.");
        SaveUpgrade(record, intent with { State = "restored" });
    }
}
