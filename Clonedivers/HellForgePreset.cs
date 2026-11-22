namespace Clonedivers;

/// <summary>Player preset presence checks. These do not certify artifact hashes or multiplayer.</summary>
public static class HellForgePreset
{
    public static bool IsConflictingAlternative(HellForgeMod mod) =>
        mod.Id is "local.clonedivers-mine-boundary" or "pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91" or "pack.95ef276a-6287-465f-ac5b-8512d2227b74" or "chef.smarter-seaf" ||
        System.Text.RegularExpressions.Regex.IsMatch(mod.Name, @"(?i)SEAF[ -]?Companion|Pick\s*Your\s*(Bots|Bugs|Squids|Enemies)|Gameplay Reforged|Supreme Command|SEAF Commander|Armored Overhaul|Scope Framing|Captain of the S\.E\.A\.F\.|Chaos Divers|Cyberstan Rising") ||
        mod.Name.Contains("Smarter SEAF", StringComparison.OrdinalIgnoreCase) && mod.Id != SmarterSeaf;
    public const string BingusSharedLoader = "pack.612eaf70-d682-43c7-9efd-16dcc695f977";
    public const string SmarterSeaf = "pack.f7c3abbe-902c-41a7-8183-057a6ab15768";
    public const string GuardDogs = "pack.6f1d2c8e-5a47-4b93-9e0a-3c7b1f4d8a21";
    public const string CleanExit = "clonedivers.clean-exit";
    public const string LegoCompactLeafWriter = "clonedivers.lego-compact-leaf-writer";
    public const string EmpireLegoYoda = "clonedivers.empire-lego-yoda";
    public const string EmpireAtstWriter = "clonedivers.empire-atst-writer";
    public const string EmpireTieSwitch = "clonedivers.empire-tie-switch";
    public const string EmpireCameraPullback = "clonedivers.empire-camera-pullback";
    public const string DedicatedMeleeSlot = "vibeslacker.dedicated-melee-slot";
    public const string FasterCorpseCleanup = "local.faster-corpse-cleanup";
    public const string ChaosInvasions = "clonedivers.chaos-invasions";
    public static readonly string[] Optional = [CleanExit, LegoCompactLeafWriter, EmpireLegoYoda, EmpireAtstWriter, EmpireTieSwitch, EmpireCameraPullback, DedicatedMeleeSlot, ChaosInvasions];
    public static readonly (string Id, string Name, string Version)[] Required = [
        ("hellforge.editor", "HellForge Editor", "0.4.2"),
        ("skyeshade.hell-forge", "Hell Forge Difficulty", "0.16.1"),
        ("local.hf-shared-dispatcher", "Shared Callback Manager", "0.1.0"),
        ("local.operation-reroller", "Operation Reroller", "0.1.6"),
        (FasterCorpseCleanup, "Faster Corpse Cleanup", "1.0.6-clonedivers.1"),
        ("cowboybingus.vanilla-plus-megapack", "Vanilla Plus Megapack", "0.2.1"),
        ("local.heavy-seaf-presence", "Heavy SEAF Presence Everywhere", "1.2.0-clonedivers.5"),
        ("local.mod-9fd7880666f6", "SEAF Squad Stratagems", "0.1.4-clonedivers.3"),
        (BingusSharedLoader, "Bingus Shared Loader - v20", ""),
        (SmarterSeaf, "Smarter SEAF 1.0.4", ""),
        (GuardDogs, "Smarter Guard Dogs & Sentries 5.2.0", "")
    ];

    public static string? Problem(HellForgeMods catalog, bool checkOrder = false)
    {
        if (catalog.HostingProblem != null) return catalog.HostingProblem;
        var issues = new List<string>();
        foreach (var conflict in catalog.Mods.Where(m => m.Enabled && IsConflictingAlternative(m))) issues.Add("Disable conflicting " + conflict.Name);
        foreach (var required in Required)
        {
            var mod = catalog.Mods.SingleOrDefault(m => m.Id == required.Id);
            if (mod == null) issues.Add("Install " + required.Name);
            else if (!mod.Enabled) issues.Add("Enable " + required.Name);
            else if (required.Version.Length > 0 && mod.Version != required.Version)
                issues.Add($"{required.Name} needs preset version {required.Version} (found {mod.Version.DefaultIfEmptyVersion()})");
            else if (required.Version.Length == 0 && mod.Name != required.Name)
                issues.Add("Check the author artifact/version for " + required.Name);
        }
        bool Enabled(string id) => catalog.Mods.Any(m => m.Enabled && m.Id == id);
        if (!Enabled("skyeshade.vanilla-plus") && !Enabled("cowboybingus.vanilla-plus-megapack") && !Enabled("pack.80a03e3b-a671-4e54-a2ce-52c35bb64c91"))
            issues.Add("Enable Vanilla Plus or Vanilla Plus Megapack");
        // HellForge lists client-only mods after every gameplay mod whatever order it stores, and
        // optional mods may be disabled or reordered by the player, so optional artifacts have no checkable position.
        var order = HellForgeGameplayCatalog.Pinned.Order.Where(id => catalog.Mods.Any(m => m.Id == id) &&
            !HellForgeGameplayCatalog.Pinned.Artifacts.Any(a => a.Id == id && a.Optional)).ToArray();
        if (checkOrder && !catalog.Mods.Where(m => order.Contains(m.Id)).Select(m => m.Id).SequenceEqual(order))
            issues.Add("Restore the pinned mod order");
        var dogs = catalog.Mods.SingleOrDefault(m => m.Id == GuardDogs && m.Enabled);
        if (dogs != null)
        {
            if (dogs.Options?.SingleOrDefault(o => o.Name == "Targeting Marker") is not { On: false })
                issues.Add("Turn off the Guard Dogs Targeting Marker");
            foreach (var name in new[] { "Smarter Guard Dogs and Sentries", "Guard Dogs", "Sentries", "Intelligence", "Safety", "Sentry Safety", "SEAF Safety" })
                if (dogs.Options?.SingleOrDefault(o => o.Name == name) is not { On: true })
                    issues.Add("Enable Guard Dogs option: " + name);
        }
        return issues.Count == 0 ? null : "Required gameplay setup: " + string.Join("; ", issues) + ". Use Install required mods to download and configure the pinned preset.";
    }

    public static string? SharingProblem(string? settingsJson)
    {
        const string instruction = "Use Set up selected mode to turn OFF automatic gameplay-mod sharing. Every player gets the verified author artifacts through setup; this preserves their separate redistribution terms.";
        if (settingsJson == null) return instruction; // the official default is sharing ON
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(settingsJson);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                document.RootElement.TryGetProperty("share", out var share) && share.ValueKind == System.Text.Json.JsonValueKind.False
                ? null : instruction;
        }
        catch (System.Text.Json.JsonException) { return "HellForge mod-sharing settings could not be read. Review them in Setup & mods before launching the private preset."; }
    }

    static string DefaultIfEmptyVersion(this string value) => string.IsNullOrEmpty(value) ? "unknown" : value;
}
