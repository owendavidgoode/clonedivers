namespace Clonedivers;

public sealed class PackTextureProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Texture quality and visual mode are independent. Legacy skinny is an input alias only.</summary>
public static class PackProfiles
{
    public const string PotatoName = "JohnsonPotatoMode™";
    public static PackTextureProfile? Potato(PackManifest pack) =>
        Supported(pack).FirstOrDefault(p => p.Id == "reduced512") ??
        Supported(pack).FirstOrDefault(p => p.Id == "lighter") ??
        Supported(pack).FirstOrDefault(p => p.Id != "full");
    public static IReadOnlyList<PackTextureProfile> Supported(PackManifest pack) => pack.TextureProfiles.Count > 0
        ? pack.TextureProfiles
        : new List<PackTextureProfile> {
            new() { Id = "full", Name = "Full textures", Description = "Original texture detail." },
            new() { Id = "lighter", Name = "Lighter textures", Description = "Full texture detail, streamed as needed." }
        };

    public static string Resolve(PackManifest pack, Func<string, bool> enabled, string? explicitProfile = null)
    {
        var chosen = explicitProfile ?? (pack.Options.Any(o => string.Equals(o.Id, "skinny", StringComparison.OrdinalIgnoreCase)) && enabled("skinny") ? "lighter" : "full");
        if (chosen == "lighter" && !Supported(pack).Any(p => p.Id == "lighter") && Potato(pack) is {} potato) chosen = potato.Id;
        var profile = Supported(pack).FirstOrDefault(p => string.Equals(p.Id, chosen, StringComparison.OrdinalIgnoreCase));
        if (profile is null) throw new InvalidDataException($"This pack does not support texture profile '{chosen}'.");
        return profile.Id;
    }

    public static bool Includes(PackFile file, string mode, string profile) =>
        (file.Modes is null || file.Modes.Contains(mode, StringComparer.OrdinalIgnoreCase)) &&
        (file.TextureProfiles is null || file.TextureProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase));

    public static void Validate(PackManifest pack)
    {
        if (pack.TextureProfiles is null || pack.TextureProfiles.Count == 0 || pack.TextureProfiles.Any(p => p is null))
            throw new InvalidDataException("Format 3 requires a texture profile catalog.");
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in pack.TextureProfiles)
            if (string.IsNullOrWhiteSpace(profile.Id) || !System.Text.RegularExpressions.Regex.IsMatch(profile.Id, "^[a-z][a-z0-9-]{0,31}$") || !profiles.Add(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
                throw new InvalidDataException("Texture profile IDs must be unique lower-case identifiers with display names.");
        if (!profiles.Contains("full")) throw new InvalidDataException("Format 3 must include the full texture profile.");
        if (pack.Options.Any(o => string.IsNullOrWhiteSpace(o.Id)) || pack.Options.Select(o => o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pack.Options.Count)
            throw new InvalidDataException("Format 3 option IDs must be nonempty and unique.");
        var modes = new HashSet<string>(new[] { "clonedivers", "commandos" }, StringComparer.OrdinalIgnoreCase);
        foreach (var file in pack.Files)
        {
            ValidateSet(file.Modes, modes, "mode", file.Name);
            ValidateSet(file.TextureProfiles, profiles, "texture profile", file.Name);
        }
        // Check overlap analytically for each mode/profile, including any legacy optional gates.
        // No exponential enumeration of untrusted optional groups is needed.
        foreach (var mode in modes)
        foreach (var profile in profiles)
        foreach (var group in pack.Files.Where(f => Includes(f, mode, profile)).GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var files = group.ToList();
            for (int i = 0; i < files.Count; i++)
            for (int j = i + 1; j < files.Count; j++)
                if (CanCoexist(files[i], files[j], mode, profile))
                    throw new InvalidDataException($"manifest.json: {group.Key} overlaps in {mode}/{profile}.");
        }
    }

    static void ValidateSet(List<string>? values, HashSet<string> supported, string kind, string file)
    {
        if (values is null) return;
        if (values.Count == 0 || values.Any(v => v is null || !supported.Contains(v)) || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new InvalidDataException($"manifest.json: {file} has an invalid {kind} condition.");
    }

    static bool CanCoexist(PackFile a, PackFile b, string mode, string profile)
    {
        var requirements = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["commandos"] = mode == "commandos", ["skinny"] = profile != "full"
        };
        foreach (var file in new[] { a, b })
        {
            if (!Require(file.Option, true) || !Require(file.UnlessOption, false)) return false;
        }
        return true;
        bool Require(string? id, bool value)
        {
            if (id is null) return true;
            if (requirements.TryGetValue(id, out var previous)) return previous == value;
            requirements[id] = value;
            return true;
        }
    }
}
