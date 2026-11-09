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
    static HellForgeGameplayCatalog Load()
    {
        using var stream = typeof(HellForgeGameplayCatalog).Assembly.GetManifestResourceStream("Clonedivers.Forge.Gameplay.json")
            ?? throw new InvalidDataException("Required gameplay catalog is missing from this launcher.");
        var value = JsonSerializer.Deserialize<HellForgeGameplayCatalog>(stream, HellForgePack.Json) ?? throw new InvalidDataException("Empty gameplay catalog.");
        value.Validate(); return value;
    }
    public void Validate()
    {
        if (Format != 1 || Id != "build25480438-r3" || GameBuild != HellForgePack.SupportedBuild || Artifacts.Count != 8 || Artifacts.Select(a => a.Id).Distinct().Count() != 8 || Order.Distinct().Count() != Order.Count)
            throw new InvalidDataException("Unsupported gameplay catalog.");
        var supplied = Artifacts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var bundled = new[] { "hellforge.editor", "skyeshade.hell-forge", "skyeshade.vanilla-plus" };
        if (!supplied.SetEquals(HellForgePreset.Required.Select(r => r.Id).Where(id => !bundled.Contains(id)).Append(HellForgePreset.CleanExit)) ||
            !Artifacts.Where(a => a.Optional).Select(a => a.Id).SequenceEqual([HellForgePreset.CleanExit]) || !supplied.Concat(bundled).ToHashSet(StringComparer.Ordinal).SetEquals(Order))
            throw new InvalidDataException("Gameplay catalog does not cover the required preset.");
        foreach (var a in Artifacts)
        {
            FileSafety.ValidateLeaf(a.Id);
            if (a.Bytes <= 0 || a.Bytes > 128 * 1024 * 1024 || !FileSafety.IsSha256(a.Sha256) || !FileSafety.IsSha256(a.ContentHash) ||
                !Uri.TryCreate(a.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !(uri.Host == "github.com" || uri.Host == "hf-mod.com")) throw new InvalidDataException("Untrusted gameplay artifact metadata.");
        }
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
    public static async Task StageAsync(string folder, IEnumerable<GameplayArtifact> artifacts, IProgress<string>? progress, CancellationToken ct = default)
    {
        RejectLinks(folder); Directory.CreateDirectory(folder);
        foreach (var artifact in artifacts)
        {
            ct.ThrowIfCancellationRequested();
            var path = FileSafety.Child(folder, artifact.FileName);
            progress?.Report("Getting " + artifact.Name + "…");
            await Pack.DownloadAsync(new PackFile { Name = artifact.FileName, Url = artifact.Url, Size = artifact.Bytes, Sha256 = artifact.Sha256 }, path, null, ct);
            await VerifyArchiveAsync(path, artifact, ct);
        }
    }
}
