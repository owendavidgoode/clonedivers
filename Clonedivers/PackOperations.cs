using System.Text.Json;

namespace Clonedivers;

/// <summary>The exact requested bytes and settings, independent of the current online feed.</summary>
public sealed class RecoveryTarget
{
    public string PackVersion { get; set; } = "";
    public string? GameBuild { get; set; }
    public Dictionary<string, bool> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? TextureProfile { get; set; }
    public bool TargetActive { get; set; } = true;
    public List<PackFile> Files { get; set; } = new();

    public static RecoveryTarget? Read(string gameDir, string planPath)
    {
        try
        {
            if (!File.Exists(planPath)) return null;
            var plan = JsonSerializer.Deserialize<PlanFile>(File.ReadAllText(planPath));
            if (plan is null || plan.Format != 2 || !SameGame(gameDir, plan.GameDir)) return null;
            FileSafety.ValidateTargets(plan.TargetFiles);
            return new() { PackVersion = plan.PackVersion, GameBuild = plan.GameBuild,
                Options = new(plan.Options ?? new(), StringComparer.OrdinalIgnoreCase), TextureProfile = plan.TextureProfile,
                TargetActive = plan.TargetActive, Files = plan.TargetFiles.Select(f => f.Clone()).ToList() };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        { return null; }
    }

    internal static bool SameGame(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
}

public sealed class InstalledFile
{
    public PackFile File { get; set; } = new();
    public long LastWriteUtcTicks { get; set; }
}

/// <summary>Successful installed target. Layout checks are cheap metadata checks, never a claim of a fresh hash verification.</summary>
public sealed class InstallReceipt
{
    public int Format { get; set; } = 1;
    public string GameDir { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string? GameBuild { get; set; }
    public Dictionary<string, bool> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? TextureProfile { get; set; }
    public bool TargetActive { get; set; } = true;
    public DateTimeOffset InstalledUtc { get; set; }
    public DateTimeOffset? VerifiedUtc { get; set; }
    public List<InstalledFile> Files { get; set; } = new();

    public static InstallReceipt? Load(string path, string gameDir)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var r = JsonSerializer.Deserialize<InstallReceipt>(File.ReadAllText(path));
            if (r is null || r.Format != 1 || !RecoveryTarget.SameGame(r.GameDir, gameDir) || r.Options is null || r.Files is null || r.Files.Any(f => f is null)) return null;
            FileSafety.ValidateTargets(r.Files.Select(f => f.File).ToList());
            r.Options = new(r.Options, StringComparer.OrdinalIgnoreCase);
            return r;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        { return null; }
    }

    public bool MatchesLayout(string gameDir)
    {
        try
        {
            if (!RecoveryTarget.SameGame(GameDir, gameDir) || ModFiles.HasStagedFiles(gameDir)) return false;
            var active = ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.DataFolder));
            var parked = ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.OffFolder));
            if (active.Length > 0 && parked.Length > 0) return false;
            var paths = active.Length > 0 ? active : parked;
            if (paths.Length != Files.Count) return false;
            var actual = paths.ToDictionary(p => Path.GetFileName(p)!, StringComparer.OrdinalIgnoreCase);
            return Files.All(f => actual.TryGetValue(f.File.Name, out var path) && new FileInfo(path) is var fi &&
                fi.Length == f.File.Size && fi.LastWriteTimeUtc.Ticks == f.LastWriteUtcTicks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return false; }
    }

    public RecoveryTarget ToRecoveryTarget(bool? targetActive = null) => new() { PackVersion = PackVersion, GameBuild = GameBuild,
        Options = new(Options, StringComparer.OrdinalIgnoreCase), TextureProfile = TextureProfile, TargetActive = targetActive ?? TargetActive,
        Files = Files.Select(f => f.File.Clone()).ToList() };

    public ApplyResult ToApplyResult() => new() { PackVersion = PackVersion, GameBuild = GameBuild, GameDir = GameDir,
        Options = new(Options, StringComparer.OrdinalIgnoreCase), TextureProfile = TextureProfile, TargetActive = TargetActive, VerifiedUtc = VerifiedUtc };

    public static void Invalidate(string path)
    {
        FileSafety.RejectLink(path);
        if (File.Exists(path)) File.Delete(path);
    }

    public static void Write(string path, string gameDir, IReadOnlyList<PackFile> wanted, ApplyResult result)
    {
        FileSafety.RejectLink(path); FileSafety.RejectLink(path + ".tmp");
        var previous = Load(path, gameDir);
        if (result.VerifiedUtc is null && previous is not null && previous.PackVersion == result.PackVersion &&
            previous.MatchesLayout(gameDir) && previous.Files.Count == wanted.Count &&
            previous.Files.All(p => wanted.Any(f => f.Name.Equals(p.File.Name, StringComparison.OrdinalIgnoreCase) && f.Sha256.Equals(p.File.Sha256, StringComparison.OrdinalIgnoreCase))))
            result.VerifiedUtc = previous.VerifiedUtc;
        var destination = Path.Combine(gameDir, result.TargetActive ? ModFiles.DataFolder : ModFiles.OffFolder);
        var r = new InstallReceipt { GameDir = Path.GetFullPath(gameDir), PackVersion = result.PackVersion, GameBuild = result.GameBuild,
            Options = new(result.Options ?? new(), StringComparer.OrdinalIgnoreCase), TextureProfile = result.TextureProfile,
            TargetActive = result.TargetActive, InstalledUtc = DateTimeOffset.UtcNow, VerifiedUtc = result.VerifiedUtc,
            Files = wanted.Select(f => new InstalledFile { File = f.Clone(), LastWriteUtcTicks = new FileInfo(FileSafety.Child(destination, f.Name)).LastWriteTimeUtc.Ticks }).ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
