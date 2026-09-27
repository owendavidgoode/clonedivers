using Clonedivers;

static class SaiCheck
{
    static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    static string Identity(PackFile f) => $"{f.Name}|{f.Size}|{f.Sha256}";
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("r15-manifest sai-manifest");
            var baseline = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
            var candidate = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
            var added = candidate.Files.Where(f => f.Name == "9ba626afa44a3aa3.patch_321" || f.Name.StartsWith("9ba626afa44a3aa3.patch_321.")).ToList();
            Require(added.Count == 3 && added.Count(f => f.Size > 0) == 1, "Expected one SAI bundle with empty sidecars.");
            Require(candidate.Files.Count == baseline.Files.Count + 3, "Only the SAI bundle may be added.");
            foreach (var profile in PackProfiles.Supported(candidate))
            foreach (var mode in new[] { LauncherMode.Clonedivers, LauncherMode.EmpireDivers })
            foreach (var covenant in new[] { false, true })
            foreach (var droids in new[] { false, true })
            {
                var settings = new Settings { TextureProfile = profile.Id, Options = new() { ["droids"] = droids, ["covenant"] = covenant } };
                var options = LauncherModes.OptionsFor(settings, candidate, mode);
                var before = Pack.EffectiveFiles(baseline, id => options[id], profile.Id);
                var after = Pack.EffectiveFiles(candidate, id => options[id], profile.Id);
                FileSafety.ValidateTargets(after);
                Require(after.Count == before.Count + 3, "SAI not included in this mode/profile.");
                Require(after.Take(before.Count).Select(Identity).SequenceEqual(before.Select(Identity)), "Existing mode contents changed.");
                Require(after.Skip(before.Count).Select(f => (f.Size, f.Sha256)).Order().SequenceEqual(added.Select(f => (f.Size, f.Sha256)).Order()), "Wrong added payloads.");
                foreach (var group in after.GroupBy(f => f.Name.Split('.')[0]))
                {
                    var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out var i, out _); return i; }).Distinct().Order().ToArray();
                    Require(indices.SequenceEqual(Enumerable.Range(0, indices.Length)), "Patch numbering gap.");
                }
                Console.WriteLine($"PASS SAI sound/bolts, existing content retained: {mode}/{profile.Id}, Covenant={covenant}, droids={droids}");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
