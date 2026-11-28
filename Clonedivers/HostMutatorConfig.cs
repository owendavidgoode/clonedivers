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

    [JsonPropertyName("theaterPreset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TheaterPreset { get; init; }

    [JsonPropertyName("activeMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveMode { get; init; }

    [JsonPropertyName("corpseDelaySeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CorpseDelaySeconds { get; init; }

    [JsonPropertyName("corpseTurboReaper")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CorpseTurboReaper { get; init; }

    [JsonPropertyName("targetActiveUnits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TargetActiveUnits { get; init; }

    [JsonPropertyName("virtualTheaterBudget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? VirtualTheaterBudget { get; init; }

    [JsonPropertyName("spawnRadiusMinM")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpawnRadiusMinM { get; init; }

    [JsonPropertyName("spawnRadiusMaxM")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpawnRadiusMaxM { get; init; }

    [JsonPropertyName("seafWaveCeiling")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SeafWaveCeiling { get; init; }

    [JsonPropertyName("mapRadiusPermille")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MapRadiusPermille { get; init; }

    [JsonPropertyName("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsUltrakillTheater =>
        HostMutatorConfig.IsUltrakillTheater(TheaterPreset, ChaosIntensity);

    public void Validate()
    {
        if (Format != 1 ||
            !HostMutatorConfig.IsValidChaosIntensity(ChaosIntensity) ||
            !HostMutatorConfig.IsValidHeavySeaf(HeavySeaf) ||
            (TheaterPreset != null && !HostMutatorConfig.IsValidTheaterPreset(TheaterPreset)) ||
            (ActiveMode != null && !HostMutatorConfig.IsValidActiveMode(ActiveMode)) ||
            CorpseDelaySeconds is < 0 or > 120 ||
            TargetActiveUnits is < 12 or > 8000 ||
            VirtualTheaterBudget is < 12 or > 20000 ||
            SpawnRadiusMinM is < 28 or > 400 ||
            SpawnRadiusMaxM is < 45 or > 950 ||
            (SpawnRadiusMinM is { } rMin && SpawnRadiusMaxM is { } rMax && rMin > rMax) ||
            SeafWaveCeiling is < 6 or > 600 ||
            MapRadiusPermille is < 380 or > 950)
        {
            throw new InvalidDataException("Unsupported host gameplay mutator options.");
        }
    }
}

/// <summary>
/// Reads and atomically writes &lt;installRoot&gt;/mods/clonedivers-host-options.json (symlink-safe, UTF-8 without BOM)
/// so host-authoritative mods can read runtime intensity and Level 12 ULTRAKILL theater budgets without altering HellForge co-op mod hashes.
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
        value is "standard" or "off" or "total-war" or "ultrakill" or "clone-wars";

    public static bool IsValidHeavySeaf(string? value) =>
        value is "standard" or "heavy" or "constant" or "clone-wars";

    public static bool IsValidTheaterPreset(string? value) =>
        value is "standard" or "level12-ultrakill" or "clone-wars";

    public static bool IsValidActiveMode(string? value) =>
        value is "empire" or "clonedivers" or "commandos" or "regular" or "auto";

    public static bool IsUltrakillTheater(string? theaterPreset, string? chaosIntensity = null) =>
        theaterPreset is "level12-ultrakill" or "clone-wars" ||
        chaosIntensity is "ultrakill" or "clone-wars";

    public static HostMutatorOptions BuildOptions(
        string chaosIntensity,
        string heavySeaf,
        bool fasterCorpseCleanup,
        string? theaterPreset = null,
        DateTimeOffset? updatedUtc = null,
        string? activeMode = null)
    {
        var normalizedTheater = string.IsNullOrWhiteSpace(theaterPreset) ? "standard" : theaterPreset.Trim();
        var normalizedMode = string.IsNullOrWhiteSpace(activeMode) ? null : activeMode.Trim();
        bool isCloneWars = normalizedTheater == "clone-wars";
        bool isUltrakill = isCloneWars || normalizedTheater == "level12-ultrakill";

        if (!isUltrakill)
        {
            var std = new HostMutatorOptions
            {
                Format = 1,
                ChaosIntensity = chaosIntensity,
                HeavySeaf = heavySeaf,
                FasterCorpseCleanup = fasterCorpseCleanup,
                ActiveMode = normalizedMode,
                UpdatedUtc = updatedUtc ?? DateTimeOffset.UtcNow
            };
            std.Validate();
            return std;
        }

        var effectiveChaos = chaosIntensity switch
        {
            "off" => "off",
            "total-war" => isCloneWars ? "total-war" : "ultrakill",
            _ => isCloneWars ? "clone-wars" : "ultrakill"
        };
        var effectiveSeaf = isCloneWars
            ? (heavySeaf == "standard" ? "clone-wars" : heavySeaf)
            : (heavySeaf == "standard" ? "constant" : heavySeaf);

        var uk = new HostMutatorOptions
        {
            Format = 1,
            ChaosIntensity = effectiveChaos,
            HeavySeaf = effectiveSeaf,
            FasterCorpseCleanup = true,
            TheaterPreset = normalizedTheater,
            ActiveMode = normalizedMode,
            CorpseDelaySeconds = 0,
            CorpseTurboReaper = true,
            TargetActiveUnits = isCloneWars ? 820 : 720,
            VirtualTheaterBudget = isCloneWars ? 2050 : 1650,
            SpawnRadiusMinM = 55,
            SpawnRadiusMaxM = 365,
            SeafWaveCeiling = isCloneWars ? 78 : 54,
            MapRadiusPermille = 920,
            UpdatedUtc = updatedUtc ?? DateTimeOffset.UtcNow
        };
        uk.Validate();
        return uk;
    }

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
