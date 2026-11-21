using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clonedivers;

public sealed record HostMutatorOptions
{
    [JsonPropertyName("format")]
    public int Format { get; init; } = 1;

    [JsonPropertyName("chaosIntensity")]
    public string ChaosIntensity { get; init; } = "standard";

    [JsonPropertyName("heavySeaf")]
    public string HeavySeaf { get; init; } = "standard";

    [JsonPropertyName("fasterCorpseCleanup")]
    public bool FasterCorpseCleanup { get; init; } = true;

    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public void Validate()
    {
        if (Format != 1 ||
            ChaosIntensity is not ("standard" or "off" or "total-war") ||
            HeavySeaf is not ("standard" or "heavy" or "constant"))
        {
            throw new InvalidDataException("Unsupported host gameplay mutator options.");
        }
    }
}

/// <summary>
/// Reads and atomically writes &lt;installRoot&gt;/mods/clonedivers-host-options.json (symlink-safe, UTF-8 without BOM)
/// so host-authoritative mods can read runtime intensity without altering HellForge co-op mod hashes.
/// </summary>
public static class HostMutatorConfig
{
    public const string FileName = "clonedivers-host-options.json";

    static readonly JsonSerializerOptions WriteJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static bool IsValidChaosIntensity(string? value) =>
        value is "standard" or "off" or "total-war";

    public static bool IsValidHeavySeaf(string? value) =>
        value is "standard" or "heavy" or "constant";

    public static string GetFilePath(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            throw new ArgumentException("Install root is required.", nameof(installRoot));

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        for (var dir = new DirectoryInfo(root); dir != null; dir = dir.Parent)
            FileSafety.RejectLink(dir.FullName);

        var mods = FileSafety.Child(root, "mods");
        return FileSafety.Child(mods, FileName);
    }

    public static HostMutatorOptions Read(string installRoot)
    {
        var path = GetFilePath(installRoot);
        if (!File.Exists(path))
            return new HostMutatorOptions();

        if (new FileInfo(path).Length > 64 * 1024)
            throw new InvalidDataException("Host mutator options file is oversized.");

        var raw = File.ReadAllText(path, Encoding.UTF8);
        var parsed = JsonSerializer.Deserialize<HostMutatorOptions>(raw, WriteJson)
            ?? throw new InvalidDataException("Host mutator options file is empty.");
        parsed.Validate();
        return parsed;
    }

    public static void Write(string installRoot, HostMutatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var path = GetFilePath(installRoot);
        var modsDir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(modsDir);
        FileSafety.RejectLink(modsDir);
        FileSafety.RejectLink(path);

        var temp = FileSafety.Child(modsDir, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var payload = JsonSerializer.Serialize(options, WriteJson);
            var bytes = Utf8NoBom.GetBytes(payload);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            FileSafety.RejectLink(path);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static HostMutatorOptions Write(
        string installRoot,
        string chaosIntensity,
        string heavySeaf,
        bool fasterCorpseCleanup,
        DateTimeOffset? updatedUtc = null)
    {
        var options = new HostMutatorOptions
        {
            Format = 1,
            ChaosIntensity = chaosIntensity,
            HeavySeaf = heavySeaf,
            FasterCorpseCleanup = fasterCorpseCleanup,
            UpdatedUtc = updatedUtc ?? DateTimeOffset.UtcNow
        };
        Write(installRoot, options);
        return options;
    }
}
