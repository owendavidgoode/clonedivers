using Clonedivers;
using System.Text.Json;

static class ProfileReleaseCheck
{
    sealed record DirectorySpec(string Id, string Directory);
    static async Task Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("candidate-v3 baseline-v2 profile-directories.json");
        var manifest = Manifest.Parse(File.ReadAllText(args[0]));
        if (manifest.Format != 3) throw new InvalidDataException("Expected profile feed format 3.");
        var pack = manifest.Pack!;
        var baseline = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
        var sources = JsonSerializer.Deserialize<List<DirectorySpec>>(File.ReadAllText(args[2]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .ToDictionary(p => p.Id, p => p.Directory, StringComparer.OrdinalIgnoreCase);
        string Identity(PackFile f) => $"{f.Name}|{f.Size}|{f.Sha256}";
        foreach (var profile in PackProfiles.Supported(pack))
        {
            if (!sources.TryGetValue(profile.Id, out var folder)) throw new InvalidDataException("Missing source directory: " + profile.Id);
            foreach (var commandos in new[] { false, true })
            {
                bool Enabled(string id) => id == "commandos" ? commandos : id == "skinny" ? profile.Id != "full" : baseline.Options.FirstOrDefault(o => o.Id == id)?.Default ?? true;
                var files = Pack.EffectiveFiles(pack, Enabled, profile.Id);
                FileSafety.ValidateTargets(files);
                foreach (var group in files.GroupBy(f => f.Name.Split('.')[0]))
                {
                    var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out var i, out _); return i; }).Distinct().Order();
                    if (!indices.SequenceEqual(Enumerable.Range(0, indices.Count()))) throw new InvalidDataException("Patch numbering gap.");
                }
                if (profile.Id == "full" || (profile.Id == "lighter" && !commandos))
                {
                    var expected = Pack.EffectiveFiles(baseline, Enabled, profile.Id);
                    if (!files.Select(Identity).Order().SequenceEqual(expected.Select(Identity).Order()))
                        throw new InvalidDataException($"Unintended baseline change: commandos={commandos}, {profile.Id}");
                }
                Console.WriteLine($"PASS {profile.Id}/commandos={commandos}: {files.Count} unique gap-free files; baseline/isolation checks passed.");
                if (!commandos) continue;
                var actual = ModFiles.ListPatchFiles(folder);
                if (!actual.Select(Path.GetFileName).Order().SequenceEqual(files.Select(f => f.Name).Order()))
                    throw new InvalidDataException("Profile file membership differs: " + profile.Id);
                foreach (var f in files)
                {
                    var path = Path.Combine(folder, f.Name);
                    if (new FileInfo(path).Length != f.Size || await Pack.Sha256Async(path, CancellationToken.None) != f.Sha256)
                        throw new InvalidDataException("Profile source hash differs: " + path);
                }
                Console.WriteLine($"PASS {profile.Id}: every source file hashed against candidate.");
            }
        }
    }
}
