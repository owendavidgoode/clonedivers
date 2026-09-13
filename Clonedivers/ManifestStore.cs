using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clonedivers;

/// <summary>Validated, source-scoped metadata for offline use. Test feeds never replace the public cache.</summary>
public static class ManifestStore
{
    public const string ProfileFeed = "https://raw.githubusercontent.com/owendavidgoode/clonedivers/main/manifest-v3.json";

    public static void ValidateForUse(Manifest manifest)
    {
        if (manifest.Pack is not { IsPublished: true, IsPerFile: true })
            throw new InvalidDataException("Pack information is incomplete. Using previously saved information if available.");
    }

    public static string CachePath(string? source, string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers");
        var name = string.IsNullOrWhiteSpace(source) ? "public-v3" :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        return Path.Combine(directory, $"metadata-{name}.json");
    }

    public static Manifest? Load(string path)
    {
        try
        {
            FileSafety.RejectLink(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024) return null;
            var manifest = Manifest.Parse(File.ReadAllText(path));
            return manifest.Pack is { IsPublished: true, IsPerFile: true } ? manifest : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return null; }
    }

    public static void Save(string path, Manifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest);
        var checkedManifest = Manifest.Parse(json);
        ValidateForUse(checkedManifest);
        FileSafety.RejectLink(path); FileSafety.RejectLink(path + ".tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static async Task<Manifest> FetchAsync(string? source, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(source)) return await Pack.FetchManifestAsync(source, ct);
        try
        {
            var current = await Pack.FetchManifestAsync(ProfileFeed, ct);
            if (current.Format != 3) throw new InvalidDataException("The profile feed has an unsupported format.");
            return current;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The old feed remains readable throughout the coordinated launcher rollout.
            return await Pack.FetchManifestAsync(null, ct);
        }
    }
}
