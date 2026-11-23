using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

/// <summary>
/// Computes a deterministic short squad match code (e.g. "r8-a9c4") from the sorted (Id, Version)
/// of enabled gameplay mods, excluding client-only visual/cosmetic packs and gameplay:false client mods
/// so co-op squads on different Star Wars skins or texture tiers share the exact same match code.
/// </summary>
public static class SquadFingerprint
{
    static readonly HashSet<string> KnownClientOnlyCodeMods = new(StringComparer.Ordinal)
    {
        HellForgePreset.CleanExit,
        HellForgePreset.LegoCompactLeafWriter,
        HellForgePreset.EmpireLegoYoda,
        HellForgePreset.EmpireAtstWriter,
        HellForgePreset.EmpireTieSwitch,
        HellForgePreset.EmpireCameraPullback,
        HellForgePreset.StarWarsLocalization,
        HellForgePreset.DedicatedMeleeSlot
    };

    static readonly HashSet<string> RequiredGameplayIds =
        HellForgePreset.Required.Select(r => r.Id)
            .Append("skyeshade.vanilla-plus")
            .ToHashSet(StringComparer.Ordinal);

    public static string ExtractCatalogPrefix(string? catalogId = null)
    {
        var id = string.IsNullOrWhiteSpace(catalogId) ? HellForgeGameplayCatalog.Current.Id : catalogId.Trim();
        var match = Regex.Match(id, @"-(r\d+[a-z0-9]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success) return match.Groups[1].Value.ToLowerInvariant();
        var cleaned = Regex.Replace(id, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
        return string.IsNullOrEmpty(cleaned) ? "r8" : cleaned[..Math.Min(cleaned.Length, 6)];
    }

    /// <summary>True when an enabled mod affects co-op gameplay state rather than local visuals/client-only rendering.</summary>
    public static bool IsGameplayMod(HellForgeMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (!mod.Enabled) return false;

        // 1. Star Wars visual/cosmetic packs (Clones, Delta Squad, Empire, and local overhaul variants) are always client-only.
        if (HellForgePack.IsStarWarsOverhaulMod(mod)) return false;

        // 2. Project client-only code mods declaring gameplay:false (per-player co-op).
        if (KnownClientOnlyCodeMods.Contains(mod.Id)) return false;

        var catalogArtifact = HellForgeGameplayCatalog.Current.Artifacts.FirstOrDefault(a => a.Id == mod.Id);
        if (catalogArtifact != null && catalogArtifact.Source.Contains("gameplay false", StringComparison.OrdinalIgnoreCase))
            return false;

        // 3. Explicit client/cosmetic tags without a gameplay tag.
        if (mod.Tags is { Length: > 0 })
        {
            bool hasGameplayTag = mod.Tags.Any(t => t.Equals("gameplay", StringComparison.OrdinalIgnoreCase));
            bool hasClientOnlyTag = mod.Tags.Any(t =>
                t.Equals("client", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("client-only", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("cosmetic", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("visual", StringComparison.OrdinalIgnoreCase));
            if (hasClientOnlyTag && !hasGameplayTag) return false;
        }

        // 4. Inspect on-disk manifest if present for custom user mods (symlink-safe).
        if (!string.IsNullOrWhiteSpace(mod.Folder) && !RequiredGameplayIds.Contains(mod.Id))
        {
            try
            {
                if (Directory.Exists(mod.Folder) && (File.GetAttributes(mod.Folder) & FileAttributes.ReparsePoint) == 0)
                {
                    var hfModJson = Path.Combine(mod.Folder, "hellforge-mod.json");
                    if (File.Exists(hfModJson) && (File.GetAttributes(hfModJson) & FileAttributes.ReparsePoint) == 0 &&
                        new FileInfo(hfModJson).Length <= 64 * 1024)
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(hfModJson));
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty("gameplay", out var gp) &&
                            gp.ValueKind == JsonValueKind.False)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // Pure cosmetic pack folder (manifest.json without hellforge-mod.json) not in RequiredGameplayIds.
                        var cosmeticManifest = Path.Combine(mod.Folder, "manifest.json");
                        if (File.Exists(cosmeticManifest) &&
                            (mod.Tags == null || !mod.Tags.Any(t => t.Equals("gameplay", StringComparison.OrdinalIgnoreCase))))
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }

        return true;
    }

    public static string Compute(HellForgeMods? mods, string? catalogId = null)
    {
        var prefix = ExtractCatalogPrefix(catalogId);
        if (mods == null || mods.Mods.Count == 0)
            return $"{prefix}-0000";

        var entries = mods.Mods
            .Where(IsGameplayMod)
            .Select(m => (Id: m.Id.Trim(), VersionToken: NormalizeVersion(m.Id, m.Version, m.Name)))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ThenBy(e => e.VersionToken, StringComparer.Ordinal)
            .ToList();

        if (entries.Count == 0)
            return $"{prefix}-0000";

        return FormatCode(prefix, entries);
    }

    public static string Compute(HellForgeGameplayCatalog? catalog = null, bool includeOptionalGameplay = true)
    {
        var cat = catalog ?? HellForgeGameplayCatalog.Current;
        var prefix = ExtractCatalogPrefix(cat.Id);
        var entries = new List<(string Id, string VersionToken)>();

        foreach (var artifact in cat.Artifacts)
        {
            if (KnownClientOnlyCodeMods.Contains(artifact.Id) ||
                artifact.Source.Contains("gameplay false", StringComparison.OrdinalIgnoreCase))
                continue;
            if (artifact.Optional && !includeOptionalGameplay)
                continue;

            entries.Add((artifact.Id.Trim(), NormalizeVersion(artifact.Id, artifact.Version, artifact.Name)));
        }

        // Include bundled required gameplay mods in cat.Order (e.g. hellforge.editor, skyeshade.hell-forge, skyeshade.vanilla-plus).
        var addedIds = entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in cat.Order)
        {
            if (addedIds.Contains(id) || KnownClientOnlyCodeMods.Contains(id) || HellForgePack.IsOurPack(id))
                continue;
            var req = HellForgePreset.Required.FirstOrDefault(r => r.Id == id);
            if (req.Id != null)
                entries.Add((req.Id, NormalizeVersion(req.Id, req.Version, req.Name)));
            else if (id == "skyeshade.vanilla-plus")
                entries.Add((id, NormalizeVersion(id, "", "Vanilla Plus")));
        }

        var sorted = entries
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ThenBy(e => e.VersionToken, StringComparer.Ordinal)
            .ToList();

        return sorted.Count == 0 ? $"{prefix}-0000" : FormatCode(prefix, sorted);
    }

    static string NormalizeVersion(string id, string? version, string? name)
    {
        if (!string.IsNullOrWhiteSpace(version))
            return version.Trim();

        // Bundled Vanilla Plus has no pinned version requirement in HellForgePreset.Required; normalize by ID
        // when Version is omitted or bundled so catalog and runtime mod lists produce identical hashes.
        if (id == "skyeshade.vanilla-plus")
            return "bundled";

        return string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
    }

    static string FormatCode(string prefix, IReadOnlyList<(string Id, string VersionToken)> sortedEntries)
    {
        var canonical = string.Join("\n", sortedEntries.Select(e => $"{e.Id}@{e.VersionToken}"));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("clonedivers-squad-v1\n" + canonical));
        var hex4 = Convert.ToHexString(digest, 0, 2).ToLowerInvariant();
        return $"{prefix}-{hex4}";
    }
}
