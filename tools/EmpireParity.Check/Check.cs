using System.Text.Json;
using Clonedivers;

static class EmpireParityCheck
{
    static void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
    static string Identity(PackFile file) => $"{file.Name}|{file.Size}|{file.Sha256}|{file.Url}";
    static string Payload(PackFile file)
    {
        ModFiles.TryParsePatchName(file.Name, out var archive, out _, out var suffix);
        return $"{archive}|{suffix}|{file.Size}|{file.Sha256}|{file.Url}";
    }
    static string Bundle(PackFile file)
    {
        ModFiles.TryParsePatchName(file.Name, out var archive, out var index, out _);
        return $"{archive}.patch_{index}";
    }
    static int Slot(PackFile file)
    {
        ModFiles.TryParsePatchName(file.Name, out var archive, out var index, out _);
        return archive == "9ba626afa44a3aa3" ? index : -1;
    }
    static bool Weapon(PackFile file) => Slot(file) is >= 20 and <= 158 or 164 or >= 167 and <= 176 or 227 or >= 274 and <= 311 or 313 or 321 || Slot(file) == -1;

    static HashSet<(ulong Id, ulong Type)> Resources(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        Require(reader.ReadUInt32() == 0xf0000011, "Invalid archive header");
        var types = reader.ReadUInt32(); var count = reader.ReadUInt32();
        var start = 72L + types * 32L;
        Require(start + count * 80L <= stream.Length, "Truncated archive directory");
        var keys = new HashSet<(ulong, ulong)>();
        for (var i = 0; i < count; i++)
        {
            stream.Position = start + i * 80L;
            Require(keys.Add((reader.ReadUInt64(), reader.ReadUInt64())), "Duplicate resource");
        }
        return keys;
    }

    static async Task Main(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("baseline candidate full-source lighter-source imperial-assets intro-assets");
        var before = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
        var after = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
        Require(before.Files.Count == after.Files.Count, "Parity release must reuse existing entries only");
        var promoted = new List<PackFile>();
        for (var i = 0; i < before.Files.Count; i++)
        {
            var old = before.Files[i]; var file = after.Files[i];
            var oldModes = old.Modes!.ToArray(); var newModes = file.Modes!.ToArray();
            if (!oldModes.SequenceEqual(newModes))
            {
                Require(!oldModes.Contains("empire") && newModes.SequenceEqual(oldModes.Append("empire")), "Unexpected mode change");
                promoted.Add(file);
            }
            var restored = JsonSerializer.Deserialize<PackFile>(JsonSerializer.Serialize(file))!;
            restored.Modes = old.Modes;
            Require(JsonSerializer.Serialize(old) == JsonSerializer.Serialize(restored), "Asset bytes, conditions or order changed: " + file.Name);
        }
        Require(promoted.Select(Bundle).Distinct().Count() == 126 && promoted.Count == 400, "Incomplete shared setup");
        var forbidden = new HashSet<int> { 0,1,2,3,4,5,6,7,159,160,162,163,178,179,180,214,215,216,217,218,219,221,230,231,232,233,234,235,238,243,244,246,247,248,249,250,251,252,253,254,255,256,257,258,259,260,261,312 };
        Require(after.Files.Where(f => forbidden.Contains(Slot(f))).All(f => !f.Modes!.Contains("empire")), "Clone voices or faction-specific replacements leaked into Empire");
        var imperial = before.Files.Where(f => f.Modes!.SequenceEqual(new[] { "empire" })).ToList();
        Require(imperial.Count == 15 && imperial.All(f => after.Files.Any(x => JsonSerializer.Serialize(x) == JsonSerializer.Serialize(f))), "Imperial models or intro changed");
        var scopeBundles = after.Files.Where(f => Slot(f) == -1 || Slot(f) == 313).Select(Bundle).Distinct().ToList();
        Require(scopeBundles.Count == 90 && after.Files.Where(f => scopeBundles.Contains(Bundle(f))).All(f => f.Modes!.Contains("empire")), "Scope archive dependency missing");
        var checkedHashes = new HashSet<string>();
        var additional = new List<object>();
        var resourceReports = new List<object>();
        foreach (var profile in PackProfiles.Supported(after))
        {
            var defaults = new Settings { TextureProfile = profile.Id };
            var options = LauncherModes.OptionsFor(defaults, before, LauncherMode.Clonedivers);
            var sourceFiles = Pack.EffectiveFiles(before, id => options[id], profile.Id);
            var sources = sourceFiles.GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => Path.Combine(args[profile.Id == "full" ? 2 : 3], g.First().Name));
            foreach (var file in promoted.Where(f => f.Size > 0 && PackProfiles.Includes(f, "empire", profile.Id)))
            {
                if (!checkedHashes.Add(file.Sha256)) continue;
                var path = sources[file.Sha256];
                Require(new FileInfo(path).Length == file.Size && await Pack.Sha256Async(path, CancellationToken.None) == file.Sha256, "Promoted source hash mismatch");
            }
            var sharedKeys = promoted.Where(f => f.Name == Bundle(f) && PackProfiles.Includes(f, "empire", profile.Id))
                .SelectMany(f => Resources(sources[f.Sha256])).ToHashSet();
            var imperialKeys = new HashSet<(ulong Id, ulong Type)>();
            foreach (var file in imperial.Where(f => f.Name == Bundle(f)))
            {
                var path = args.Skip(4).Select(folder => Path.Combine(folder, file.Sha256)).Single(File.Exists);
                Require(await Pack.Sha256Async(path, CancellationToken.None) == file.Sha256, "Imperial resource source changed");
                imperialKeys.UnionWith(Resources(path));
            }
            var overlap = sharedKeys.Intersect(imperialKeys).ToHashSet();
            Require(overlap.SetEquals(new[] { (0x55c14c4b194bce0fUL, 0x535a7bd3e650d799UL), (0x55c14c4b194bce0fUL, 0xaf32095c82f2b070UL) }),
                "Unexpected Imperial/shared resource collision");
            // The unchanged, later Empire intro deliberately retains its cutscene SFX
            // bank/dependency. No shared gun, scope, armor or vehicle resource collides.
            resourceReports.Add(new { profile = profile.Id, sharedResources = sharedKeys.Count, imperialResources = imperialKeys.Count,
                allowedIntroAudioOverlaps = overlap.Count, unexpectedOverlaps = 0 });
            foreach (var droids in new[] { false, true })
            foreach (var covenant in new[] { false, true })
            foreach (var staleDelta in new[] { false, true })
            {
                var settings = new Settings { TextureProfile = profile.Id, Options = new() { ["droids"] = droids, ["covenant"] = covenant, ["commandos"] = staleDelta } };
                var cloneOptions = LauncherModes.OptionsFor(settings, after, LauncherMode.Clonedivers);
                var empireOptions = LauncherModes.OptionsFor(settings, after, LauncherMode.EmpireDivers);
                var clonesBefore = Pack.EffectiveFiles(before, id => cloneOptions[id], profile.Id);
                var clones = Pack.EffectiveFiles(after, id => cloneOptions[id], profile.Id);
                Require(clonesBefore.Select(Identity).SequenceEqual(clones.Select(Identity)), "Clonedivers changed");
                var empireBefore = Pack.EffectiveFiles(before, id => empireOptions[id], profile.Id);
                var empire = Pack.EffectiveFiles(after, id => empireOptions[id], profile.Id);
                var expectedNew = promoted.Where(f => PackProfiles.Includes(f, "empire", profile.Id)).ToList();
                Require(empire.Select(Payload).Order().SequenceEqual(empireBefore.Concat(expectedNew).Select(Payload).Order()), "Empire port omitted or changed assets");
                FileSafety.ValidateTargets(empire);
                foreach (var group in empire.GroupBy(f => f.Name.Split('.')[0]))
                {
                    var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out var i, out _); return i; }).Distinct().Order().ToArray();
                    Require(indices.SequenceEqual(Enumerable.Range(0, indices.Length)), "Patch numbering gap");
                }
                foreach (var weapon in after.Files.Where(f => Weapon(f) && PackProfiles.Includes(f, "commandos", profile.Id)))
                    Require(empire.Any(f => Payload(f) == Payload(weapon)), "Weapon parity failed: " + weapon.Name);
                Require(empire.Any(f => f.Option == "covenant") == covenant && empire.Any(f => f.Option == "droids") == droids, "Enemy toggles lost");
                var watcher = after.Files.Where(f => Slot(f) == 314 && f.Size > 0).Select(Payload).ToHashSet();
                Require(empire.Any(f => watcher.Contains(Payload(f))) == !covenant, "Covenant/Watcher collision");
                Console.WriteLine($"PASS {profile.Id}, droids={droids}, Covenant={covenant}, savedDelta={staleDelta}: complete weapons, Clones unchanged, voices isolated, Empire additions={expectedNew.Count}");
                if (droids && !covenant && staleDelta)
                    additional.Add(new { profile = profile.Id, files = expectedNew.Count, bytes = expectedNew.Sum(f => f.Size), empireFiles = empire.Count });
            }
        }
        var report = new { combinations = 16, modeSelections = 32, promotedBundles = 126, scopeArchives = 90, verifiedUniqueAssets = checkedHashes.Count, profiles = additional, resourceChecks = resourceReports, clonediversUnchanged = true, cloneVoicesExcluded = true, imperialAssetsUnchanged = true, gameplayTested = false };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[1])!, "parity-check.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
