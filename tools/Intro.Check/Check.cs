using Clonedivers;
using System.Text.Json;

static class IntroCheck
{
    static void Require(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
    static string Identity(PackFile f) => $"{f.Name}|{f.Size}|{f.Sha256}";
    static bool IsIntro(PackFile f) => ModFiles.TryParsePatchName(f.Name, out var archive, out var index, out _) &&
        archive == "9ba626afa44a3aa3" && index is >= 322 and <= 324;
    static async Task Main(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("r16 candidate legacy-projection asset-directory");
        var before = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
        var after = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
        var legacy = Manifest.Parse(File.ReadAllText(args[2])).Pack!;
        var added = after.Files.Where(IsIntro).ToList();
        Require(added.Count == 9 && added.All(f => f.Modes!.SequenceEqual(new[] { "empire" })), "Exactly three Empire-only bundles expected");
        Require(JsonSerializer.Serialize(after.Files.Where(f => !IsIntro(f))) == JsonSerializer.Serialize(before.Files), "Existing logical entries changed");
        foreach (var file in added.Where(f => f.Size > 0))
        {
            var path = Path.Combine(args[3], file.Sha256);
            Require(new FileInfo(path).Length == file.Size && await Pack.Sha256Async(path, CancellationToken.None) == file.Sha256, "Intro source hash mismatch");
        }
        foreach (var profile in PackProfiles.Supported(after))
        foreach (var mode in new[] { LauncherMode.Clonedivers, LauncherMode.EmpireDivers })
        foreach (var covenant in new[] { false, true })
        foreach (var droids in new[] { false, true })
        {
            var settings = new Settings { TextureProfile = profile.Id, Options = new() { ["covenant"] = covenant, ["droids"] = droids } };
            var options = LauncherModes.OptionsFor(settings, after, mode);
            var oldFiles = Pack.EffectiveFiles(before, id => options[id], profile.Id);
            var files = Pack.EffectiveFiles(after, id => options[id], profile.Id);
            FileSafety.ValidateTargets(files);
            Require(files.Take(oldFiles.Count).Select(Identity).SequenceEqual(oldFiles.Select(Identity)), "Existing contents or order changed");
            var empire = mode == LauncherMode.EmpireDivers;
            Require(files.Count == oldFiles.Count + (empire ? 9 : 0), "Wrong intro mode membership");
            if (empire)
                Require(files.Skip(oldFiles.Count).Select(f => (f.Size, f.Sha256)).Order().SequenceEqual(added.Select(f => (f.Size, f.Sha256)).Order()), "Intro payload missing");
            else
                Require(Pack.EffectiveFiles(legacy, id => options.GetValueOrDefault(id), profile.Id).Select(Identity).SequenceEqual(files.Select(Identity)), "Old launcher projection changed Clonedivers contents");
            foreach (var group in files.GroupBy(f => f.Name.Split('.')[0]))
            {
                var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out var i, out _); return i; }).Distinct().Order().ToArray();
                Require(indices.SequenceEqual(Enumerable.Range(0, indices.Length)), "Numbering gap");
            }
            Console.WriteLine($"PASS {mode}/{profile.Id}, Covenant={covenant}, droids={droids}: {(empire ? "battle intro added" : "Venator unchanged")}; SAI and existing content retained");
        }
    }
}
