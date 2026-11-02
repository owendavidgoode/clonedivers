using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers;

static class CompatibilitySelect
{
    static int Main(string[] args)
    {
        if (args.Length != 2 || File.Exists(args[1])) throw new ArgumentException("manifest fresh-output-json");
        var raw = File.ReadAllBytes(args[0]);
        var pack = Manifest.Parse(Encoding.UTF8.GetString(raw)).Pack!;
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var sets = new Dictionary<string, List<PackFile>>();
        var selections = new List<object>();
        foreach (var profile in new[] { "full", "lighter" })
        foreach (var droids in new[] { false, true }) foreach (var covenant in new[] { false, true })
        foreach (var delta in new[] { false, true }) foreach (var aimpoints in new[] { false, true })
        foreach (var mode in new[] { LauncherMode.Clonedivers, LauncherMode.CommandoDivers, LauncherMode.EmpireDivers })
        {
            var requested = new Dictionary<string, bool> { ["droids"] = droids, ["covenant"] = covenant, ["commandos"] = delta, ["aimpoints"] = aimpoints };
            var settings = new Settings { TextureProfile = profile, Options = requested };
            var flags = LauncherModes.OptionsFor(settings, pack, mode);
            var files = Pack.EffectiveFiles(pack, id => flags[id], profile);
            var id = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files, options))).ToLowerInvariant();
            sets.TryAdd(id, files);
            selections.Add(new { profile, mode = mode.ToString(), requestedOptions = requested, effectiveOptions = flags, fileSet = id });
        }
        var report = new { passed = true, manifestSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(), productionSelector = "LauncherModes.OptionsFor + Pack.EffectiveFiles", requestedStates = selections.Count, uniqueFileSets = sets.Count, selections, fileSets = sets };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllText(args[1], JsonSerializer.Serialize(report, options));
        Console.WriteLine($"PASS {selections.Count} selections / {sets.Count} distinct file sets");
        return 0;
    }
}
