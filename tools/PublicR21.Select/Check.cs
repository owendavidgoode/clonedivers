using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers;

// Read-only export of actual production selections. No settings or game IO.
static class EmpireHolisticCheck
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    static async Task<int> Main(string[] args)
    {
        try
        {
            Require(args.Length == 2, "manifest fresh-output-json");
            Require(!File.Exists(args[1]), "Use a fresh receipt");
            var manifest = Manifest.Parse(File.ReadAllText(args[0]));
            ManifestStore.ValidateForUse(manifest);
            var pack = manifest.Pack!;
            Require(pack.GameBuild == "25480438", "Expand the reviewed native build scope");
            Require(pack.Options.Select(option => option.Id).ToHashSet().SetEquals(
                new[] { "empire", "droids", "covenant", "commandos", "aimpoints" }),
                "Expand the option enumeration");
            var fileSets = new Dictionary<string, List<PackFile>>();
            var selections = new List<object>();
            foreach (var profile in PackProfiles.Supported(pack))
            foreach (bool droids in new[] { false, true })
            foreach (bool covenant in new[] { false, true })
            foreach (bool delta in new[] { false, true })
            foreach (bool aimpoints in new[] { false, true })
            foreach (var mode in new[] { LauncherMode.Clonedivers, LauncherMode.CommandoDivers, LauncherMode.EmpireDivers })
            {
                var settings = new Settings { TextureProfile = profile.Id, Options = new() {
                    ["droids"] = droids, ["covenant"] = covenant, ["commandos"] = delta,
                    ["aimpoints"] = aimpoints } };
                var flags = LauncherModes.OptionsFor(settings, pack, mode);
                var files = Pack.EffectiveFiles(pack, id => flags[id], profile.Id);
                FileSafety.ValidateTargets(files);
                foreach (var archive in files.GroupBy(file => file.Name.Split('.')[0]))
                {
                    var groups = archive.Select(file => {
                        Require(ModFiles.TryParsePatchName(file.Name, out _, out var group, out _), "Bad selected name");
                        return group;
                    }).Distinct().Order().ToArray();
                    Require(groups.SequenceEqual(Enumerable.Range(0, groups.Length)), "Physical patch numbering gap");
                }
                var serialized = JsonSerializer.Serialize(files);
                var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
                fileSets.TryAdd(identity, files);
                selections.Add(new { profile = profile.Id, mode = mode.ToString(), requestedOptions = settings.Options,
                    effectiveOptions = flags, fileSet = identity, targetCount = files.Count });
            }
            File.WriteAllText(args[1], JsonSerializer.Serialize(new {
                passed = true, manifest = Path.GetFullPath(args[0]),
                manifestSha256 = await Pack.Sha256Async(args[0], CancellationToken.None),
                pack.Version, pack.GameBuild, productionSelector = "LauncherModes.OptionsFor + Pack.EffectiveFiles",
                deltaAlwaysOn = PackExtras.AlwaysOn(pack, "commandos"),
                aimpointsAlwaysOn = PackExtras.AlwaysOn(pack, "aimpoints"),
                requestedStates = selections.Count, uniqueFileSets = fileSets.Count,
                selections, fileSets, gameOrSettingsChanged = false, gameplayTested = false,
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Console.WriteLine($"PASS {selections.Count} requested states; {fileSets.Count} unique production file sets.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
