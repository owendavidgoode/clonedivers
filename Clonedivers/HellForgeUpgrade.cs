using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clonedivers;

internal static partial class HellForgeSetup
{
    internal static PackFile PreviousInstaller => new() { Name = "HellForge-Setup.exe", Url = "https://github.com/SkyeShade/HellForge/releases/download/v0.3.0-beta/HellForge-Setup.exe",
        Size = 315829864, Sha256 = "b815655998d6b4b31cd12e821460cd0bbde5e360c2886381ca59e051a106a7e4" };
    internal static bool IsPrevious(string version) => version == "0.3.0-beta" || System.Text.RegularExpressions.Regex.IsMatch(version, @"^0\.3\.0-beta \([0-9a-f]{7,40}\)$");
    sealed record ProtectedFile(string Relative, string Sha256);
    sealed record UpgradeIntent(string Root, string PreviousInstaller, string Backup, string ProtectedSha256, string State);
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
            if (!Path.GetFullPath(root).Equals(intent.Root, StringComparison.OrdinalIgnoreCase) || intent.State is not ("pending" or "applied" or "restored") || !FileSafety.IsSha256(intent.ProtectedSha256))
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
        var files = new List<string>(); long bytes = 0;
        foreach (var relative in new[] { "hellforge-install.json", "launcher-settings.json", "mods/hellforge-mods.json" })
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
    static async Task<List<ProtectedFile>> SnapshotUpgrade(string root, string backup, CancellationToken ct)
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
        await File.WriteAllTextAsync(Path.Combine(backup, "protected.json"), JsonSerializer.Serialize(records, HellForgePack.Json), ct);
        return records;
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
    internal static async Task UpdateAsync(string root, IProgress<ForgeSetupProgress>? progress, CancellationToken ct,
        string? cache = null, PackFile? nextArtifact = null, PackFile? previousArtifact = null,
        Func<string, string, Task>? unpack = null, Func<CancellationToken, Task<HellForgeStatus>>? status = null, Action<string>? idle = null)
    {
        root = HellForgeCommands.ValidateRoot(root); cache ??= CacheRoot; unpack ??= UnpackAsync; idle ??= RequireUpgradeIdle;
        status ??= token => new HellForgeClient(root).StatusAsync(token);
        idle(root);
        var before = await status(ct);
        if (before.Running) throw new InvalidOperationException("End the current Forge session before upgrading.");
        if (SupportsVersion(before.Version) && before.CompatibilityProblem == null) return;
        if (!IsPrevious(before.Version) || before.Build != HellForgePack.SupportedBuild || before.Engine != "0.1.41" || before.ModSdk != 15)
            throw new InvalidDataException("This upgrade supports the verified 0.3 / engine 0.1.41 / SDK 15 installation only; review another runtime before changing it.");
        var record = UpgradeRecord(root, cache); HellForgeGameplayCatalog.RejectLinks(record);
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        using var lease = new FileStream(record + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(record) && JsonSerializer.Deserialize<UpgradeIntent>(File.ReadAllText(record), HellForgePack.Json)?.State == "pending")
            throw new IOException("Restore the interrupted Forge upgrade before retrying.");
        var next = await DownloadAsync(cache, progress, ct, nextArtifact ?? Installer);
        var previous = await DownloadAsync(cache, progress, ct, previousArtifact ?? PreviousInstaller);
        idle(root); ct.ThrowIfCancellationRequested();
        var backup = Path.Combine(cache, "backups", "forge-upgrade-" + Guid.NewGuid().ToString("N"));
        HellForgeGameplayCatalog.RejectLinks(backup); Directory.CreateDirectory(backup);
        var files = await SnapshotUpgrade(root, backup, ct);
        using var nextLock = File.Open(next, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var previousLock = File.Open(previous, FileMode.Open, FileAccess.Read, FileShare.Read);
        var intent = new UpgradeIntent(root, previous, backup, await HellForgePack.HashAsync(Path.Combine(backup, "protected.json"), ct), "pending");
        idle(root); ct.ThrowIfCancellationRequested(); SaveUpgrade(record, intent);
        progress?.Report(new("Upgrading Forge to0.4.0… Account, mods and game copy are retained. Please wait."));
        try
        {
            await unpack(next, root); // official installer owns every application write
            await VerifyProtected(root, files, CancellationToken.None);
            var after = await status(CancellationToken.None);
            if (!SupportsVersion(after.Version) || after.Build != before.Build || after.Engine != "0.1.47" || after.ModSdk != 16) throw new IOException("The official Forge update did not report the expected release/engine/SDK/build.");
            SaveUpgrade(record, intent with { State = "applied" });
        }
        catch (Exception error)
        {
            if (error is HellForgeCommandOutcomeUnknownException) throw;
            try
            {
                idle(root); await unpack(previous, root); await VerifyProtected(root, files, CancellationToken.None);
                if (!IsPrevious((await status(CancellationToken.None)).Version)) throw new IOException("Previous application version was not restored.");
                SaveUpgrade(record, intent with { State = "restored" });
            }
            catch (Exception recovery) { throw new IOException("Forge upgrade recovery is pending. Retained account backup: " + backup, new AggregateException(error, recovery)); }
            throw new IOException("Forge upgrade failed; the previous official application was restored. " + error.Message, error);
        }
    }
    internal static async Task RecoverUpgradeAsync(string root, CancellationToken ct, string? cache = null, PackFile? previousArtifact = null,
        Func<string, string, Task>? unpack = null, Func<CancellationToken, Task<HellForgeStatus>>? status = null, Action<string>? idle = null)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); cache ??= CacheRoot; previousArtifact ??= PreviousInstaller;
        unpack ??= UnpackAsync; idle ??= RequireUpgradeIdle; status ??= token => new HellForgeClient(root).StatusAsync(token);
        idle(root);
        var record = UpgradeRecord(root, cache); HellForgeGameplayCatalog.RejectLinks(record);
        if (new FileInfo(record).Length > 16384) throw new InvalidDataException("Oversized Forge recovery record.");
        using var lease = new FileStream(record + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var intent = JsonSerializer.Deserialize<UpgradeIntent>(File.ReadAllText(record), HellForgePack.Json) ?? throw new InvalidDataException("Empty upgrade recovery record.");
        var expectedInstaller = Path.Combine(cache, "HellForge-Setup-" + previousArtifact.Sha256 + ".exe");
        if (!root.Equals(intent.Root, StringComparison.OrdinalIgnoreCase) || intent.State != "pending" || intent.PreviousInstaller != expectedInstaller ||
            Path.GetDirectoryName(Path.GetFullPath(intent.Backup)) != Path.Combine(cache, "backups") || !FileSafety.IsSha256(intent.ProtectedSha256))
            throw new InvalidDataException("Forge recovery does not match this installation.");
        HellForgeGameplayCatalog.RejectLinks(intent.Backup); HellForgeGameplayCatalog.RejectLinks(expectedInstaller);
        var metadata = Path.Combine(intent.Backup, "protected.json");
        if (new FileInfo(metadata).Length > 4 * 1024 * 1024 || await HellForgePack.HashAsync(metadata, ct) != intent.ProtectedSha256)
            throw new InvalidDataException("Forge recovery metadata changed. Review the retained backup.");
        var files = JsonSerializer.Deserialize<List<ProtectedFile>>(await File.ReadAllTextAsync(metadata, ct), HellForgePack.Json) ?? throw new InvalidDataException("Empty protected account inventory.");
        if (new FileInfo(expectedInstaller).Length != previousArtifact.Size || await HellForgePack.HashAsync(expectedInstaller, ct) != previousArtifact.Sha256)
            throw new InvalidDataException("Retained previous official installer failed verification.");
        using var pinned = File.Open(expectedInstaller, FileMode.Open, FileAccess.Read, FileShare.Read);
        ct.ThrowIfCancellationRequested(); await unpack(expectedInstaller, root);
        await VerifyProtected(root, files, CancellationToken.None);
        if (!IsPrevious((await status(CancellationToken.None)).Version)) throw new IOException("Previous Forge version was not restored.");
        SaveUpgrade(record, intent with { State = "restored" });
    }
}
