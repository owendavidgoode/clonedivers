using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Clonedivers;

public sealed record GameplayArtifact(string Id, string Name, string Version, string Url, long Bytes, string Sha256, string ContentHash, string Source, bool Optional = false)
{
    public string FileName => Sha256 + ".zip";
}

public sealed class HellForgeGameplayCatalog
{
    public int Format { get; set; }
    public string Id { get; set; } = "";
    public string GameBuild { get; set; } = "";
    public List<GameplayArtifact> Artifacts { get; set; } = [];
    public List<string> Order { get; set; } = [];
    public static HellForgeGameplayCatalog Pinned { get; } = Load();
    public static HellForgeGameplayCatalog Current => Pinned;
    static HellForgeGameplayCatalog Load()
    {
        using var stream = typeof(HellForgeGameplayCatalog).Assembly.GetManifestResourceStream("Clonedivers.Forge.Gameplay.json")
            ?? throw new InvalidDataException("Required gameplay catalog is missing from this launcher.");
        var value = JsonSerializer.Deserialize<HellForgeGameplayCatalog>(stream, HellForgePack.Json) ?? throw new InvalidDataException("Empty gameplay catalog.");
        value.Validate(); return value;
    }
    public void Validate()
    {
        if (Format != 1 || Id != "build25480438-r17" || GameBuild != HellForgePack.SupportedBuild || Artifacts.Count != 19 || Artifacts.Select(a => a.Id).Distinct().Count() != 19 || Order.Distinct().Count() != Order.Count)
            throw new InvalidDataException("Unsupported gameplay catalog.");
        var supplied = Artifacts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var bundled = new[] { "hellforge.editor", "skyeshade.hell-forge", "skyeshade.vanilla-plus" };
        if (!supplied.SetEquals(HellForgePreset.Required.Select(r => r.Id).Where(id => !bundled.Contains(id)).Concat(HellForgePreset.Optional)) ||
            !Artifacts.Where(a => a.Optional).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(HellForgePreset.Optional) ||
            !supplied.Concat(bundled).ToHashSet(StringComparer.Ordinal).SetEquals(Order))
            throw new InvalidDataException("Gameplay catalog does not cover the required preset.");
        foreach (var a in Artifacts)
        {
            ValidateArtifact(a);
        }
    }
    public void ValidateDraft()
    {
        if (Format != 1 || string.IsNullOrWhiteSpace(Id) || GameBuild != HellForgePack.SupportedBuild ||
            Artifacts.Count == 0 || Artifacts.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != Artifacts.Count ||
            Order.Distinct(StringComparer.Ordinal).Count() != Order.Count)
            throw new InvalidDataException("Unsupported gameplay catalog draft.");
        var supplied = Artifacts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var bundled = new[] { "hellforge.editor", "skyeshade.hell-forge", "skyeshade.vanilla-plus" };
        if (!supplied.Concat(bundled).ToHashSet(StringComparer.Ordinal).SetEquals(Order) ||
            Order[^1] != HellForgePreset.BingusSharedLoader)
            throw new InvalidDataException("Gameplay catalog draft has an invalid load order.");
        foreach (var a in Artifacts)
        {
            ValidateArtifact(a);
        }
    }
    static void ValidateArtifact(GameplayArtifact a)
    {
        FileSafety.ValidateLeaf(a.Id);
        if (a.Bytes <= 0 || a.Bytes > 128 * 1024 * 1024 || !FileSafety.IsSha256(a.Sha256) || !FileSafety.IsSha256(a.ContentHash) ||
            !Uri.TryCreate(a.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Host != "github.com") throw new InvalidDataException("Untrusted gameplay artifact metadata.");
    }
    public static void RejectLinks(string path)
    {
        for (var part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part)) FileSafety.RejectLink(part);
    }
    public static async Task VerifyArchiveAsync(string file, GameplayArtifact artifact, CancellationToken ct = default)
    {
        RejectLinks(file);
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != artifact.Bytes || !Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(artifact.Name + " failed size/SHA256 verification. It was not installed.");
        input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
        long expanded = 0;
        if (zip.Entries.Count > 20000) throw new InvalidDataException("Oversized mod archive inventory.");
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Split('/').Any(p => p is ".." || p.Contains(':')) ||
                ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Unsafe path/link in " + artifact.Name);
            expanded = checked(expanded + entry.Length);
            if (expanded > 256L * 1024 * 1024) throw new InvalidDataException("Oversized expanded mod archive.");
        }
    }
    public static async Task StageAsync(string folder, IEnumerable<GameplayArtifact> artifacts, IProgress<string>? progress, CancellationToken ct = default,
        IProgress<(long done, long total)>? downloadProgress = null, IProgress<OperationProgress>? richProgress = null)
    {
        RejectLinks(folder); Directory.CreateDirectory(folder);
        var pending = artifacts.ToList(); long done = 0, total = pending.Sum(a => a.Bytes);
        var speed = new SpeedTracker();
        for (int i = 0; i < pending.Count; i++)
        {
            var artifact = pending[i];
            ct.ThrowIfCancellationRequested();
            ValidateArtifact(artifact);
            var path = FileSafety.Child(folder, artifact.FileName);
            progress?.Report($"Getting {artifact.Name} ({i + 1}/{pending.Count})…");
            richProgress?.Report(new("Downloading gameplay mods", artifact.Name, i + 1, pending.Count, done, total, CanCancel: true));

            bool alreadyVerified = false;
            if (File.Exists(path))
            {
                try { await VerifyArchiveAsync(path, artifact, ct); alreadyVerified = true; }
                catch (Exception e) when (e is IOException or InvalidDataException) { }
            }

            if (!alreadyVerified && await TryStageLocalRepoArtifactAsync(artifact, path, ct))
            {
                await VerifyArchiveAsync(path, artifact, ct);
                alreadyVerified = true;
            }

            if (!alreadyVerified)
            {
                await Pack.DownloadAsync(new PackFile { Name = artifact.FileName, Url = artifact.Url, Size = artifact.Bytes, Sha256 = artifact.Sha256 }, path,
                    new SyncProgress<(long done, long total)>(p =>
                    {
                        var currentDone = done + p.done;
                        downloadProgress?.Report((currentDone, total));
                        if (richProgress != null)
                        {
                            var (rate, eta) = speed.Update(currentDone, total);
                            richProgress.Report(new("Downloading gameplay mods", artifact.Name, i + 1, pending.Count, currentDone, total, rate, eta, CanCancel: true));
                        }
                    }), ct);
                await VerifyArchiveAsync(path, artifact, ct);
            }

            done += artifact.Bytes;
            downloadProgress?.Report((done, total));
            if (richProgress != null)
            {
                var (rate, eta) = speed.Update(done, total);
                richProgress.Report(new("Downloading gameplay mods", artifact.Name, i + 1, pending.Count, done, total, rate, eta, CanCancel: true));
            }
        }
    }

    static async Task<bool> TryStageLocalRepoArtifactAsync(GameplayArtifact artifact, string targetPath, CancellationToken ct)
    {
        if (!Uri.TryCreate(artifact.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" ||
            uri.Host != "github.com" ||
            !uri.AbsolutePath.StartsWith("/owendavidgoode/clonedivers/releases/download/", StringComparison.Ordinal))
        {
            return false;
        }

        var zipLeaf = Uri.UnescapeDataString(uri.Segments[^1]);
        if (string.IsNullOrWhiteSpace(zipLeaf) || !zipLeaf.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var startDir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(PlatformPaths.Comparer))
        {
            for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
            {
                var forgeModsDir = Path.Combine(dir.FullName, "forge-mods");
                if (!Directory.Exists(forgeModsDir)) continue;
                try
                {
                    FileSafety.RejectLink(forgeModsDir);
                    foreach (var modDir in Directory.EnumerateDirectories(forgeModsDir))
                    {
                        var candidate = Path.Combine(modDir, "dist", zipLeaf);
                        if (!File.Exists(candidate)) continue;
                        FileSafety.RejectLink(modDir);
                        FileSafety.RejectLink(Path.Combine(modDir, "dist"));
                        FileSafety.RejectLink(candidate);
                        var fi = new FileInfo(candidate);
                        if (fi.Length != artifact.Bytes) continue;
                        var temp = targetPath + ".local-" + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            var sha = await PackArtifactEngine.CopyAndHashAsync(candidate, temp, artifact.Bytes, ct);
                            if (!string.Equals(sha, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(temp);
                                continue;
                            }
                            FileSafety.RejectLink(targetPath);
                            File.Move(temp, targetPath, overwrite: true);
                            return true;
                        }
                        finally
                        {
                            try { if (File.Exists(temp)) File.Delete(temp); }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
                break;
            }
        }
        return false;
    }
}
