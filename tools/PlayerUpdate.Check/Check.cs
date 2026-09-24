using System.Text.Json;
using Clonedivers;

static class PlayerUpdateCheck
{
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    static string Bytes(PackFile f) => $"{f.Size}|{f.Sha256}|{f.Url}";
    static string Main(PackFile file)
    {
        ModFiles.TryParsePatchName(file.Name, out var archive, out var index, out _);
        return $"{archive}.patch_{index}";
    }
    static async Task Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("baseline-manifest candidate-manifest");
        var baseline = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
        var candidate = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
        Require(candidate.CombinedRoster, "Combined roster missing.");
        var candidateFolder = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(candidateFolder, "candidate.json")));
        foreach (var asset in report.RootElement.GetProperty("newAssets").EnumerateObject())
        {
            var path = Path.Combine(candidateFolder, "assets", asset.Name);
            Require(new FileInfo(path).Length == asset.Value.GetProperty("size").GetInt64() &&
                await Pack.Sha256Async(path, CancellationToken.None) == asset.Name, "New asset hash/size mismatch.");
        }
        var oldScopes = baseline.Files.Where(f => {
            ModFiles.TryParsePatchName(f.Name, out _, out var index, out _);
            return index is >= 262 and <= 273 && f.Size > 0;
        }).Select(Bytes).ToHashSet();
        Require(!candidate.Files.Any(f => oldScopes.Contains(Bytes(f))), "A scope remover remains active.");
        // Everything outside the explicitly replaced resources must retain its original bytes.
        var replacements = new HashSet<int> {251,252,253,255,257,259,261};
        var audioNames = report.RootElement.GetProperty("audioReplacedBundles").EnumerateArray().Select(x => x.GetString()!).ToHashSet();
        var expectedAudio = Enumerable.Range(108, 51).Concat(new[] {160,164,177,227}).Select(i => $"9ba626afa44a3aa3.patch_{i}").ToHashSet();
        Require(audioNames.SetEquals(expectedAudio), "Unexpected audio replacement scope.");
        var audioReportPath = report.RootElement.GetProperty("audioRebaseReport").GetString()!;
        Require(await Pack.Sha256Async(audioReportPath, CancellationToken.None) == report.RootElement.GetProperty("audioRebaseReportSha256").GetString(), "Audio receipt hash mismatch.");
        Require(!candidate.Files.Any(f => Main(f) == "9ba626afa44a3aa3.patch_177"), "Guard Dog probe override remains.");
        var watcherReportPath = report.RootElement.GetProperty("watcherReport").GetString()!;
        Require(await Pack.Sha256Async(watcherReportPath, CancellationToken.None) == report.RootElement.GetProperty("watcherReportSha256").GetString(), "Watcher receipt hash mismatch.");
        var guardDog = candidate.Files.Single(f => f.Name == "9ba626afa44a3aa3.patch_147");
        var watcher = candidate.Files.Single(f => f.Name == "9ba626afa44a3aa3.patch_314");
        if (report.RootElement.TryGetProperty("vehicleReport", out var vehicleReport))
            Require(await Pack.Sha256Async(vehicleReport.GetString()!, CancellationToken.None) == report.RootElement.GetProperty("vehicleReportSha256").GetString(), "Vehicle receipt hash mismatch.");
        foreach (var file in baseline.Files)
        {
            ModFiles.TryParsePatchName(file.Name, out _, out var index, out _);
            if (replacements.Contains(index) || index is >= 262 and <= 273 || audioNames.Contains(Main(file))) continue;
            var order = new Dictionary<int, int> { [0]=0, [7]=1, [1]=2, [6]=3, [2]=4, [3]=5, [4]=6, [5]=7 };
            var expectedName = order.TryGetValue(index, out var moved) ? System.Text.RegularExpressions.Regex.Replace(file.Name, @"\.patch_\d+", ".patch_" + moved) : file.Name;
            Require(candidate.Files.Any(f => f.Name == expectedName && Bytes(f) == Bytes(file)), "Unrelated baseline asset changed: " + file.Name);
        }
        var armor = candidate.Files.Where(f => {
            ModFiles.TryParsePatchName(f.Name, out _, out var index, out _);
            return index is >= 253 and <= 261;
        }).ToList();
        foreach (var delta in new[] {false, true})
        foreach (var droids in new[] {false, true})
        foreach (var aiming in new[] {false, true})
        foreach (var profile in new[] {"full", "lighter"})
        {
            bool Enabled(string id) => id switch { "commandos" => delta, "droids" => droids, "aimpoints" => aiming, "skinny" => profile == "lighter", _ => false };
            var files = Pack.EffectiveFiles(candidate, Enabled, profile);
            FileSafety.ValidateTargets(files);
            Require(files.Any(f => Bytes(f) == Bytes(guardDog)) && files.Any(f => Bytes(f) == Bytes(watcher)), "Guard Dog restoration or Watcher audio missing.");
            if (report.RootElement.TryGetProperty("vehicleFiles", out _))
            {
                foreach (var index in new[] {315,316,317})
                {
                    var asset = candidate.Files.Single(f => f.Name == $"9ba626afa44a3aa3.patch_{index}" && f.TextureProfiles!.Contains(profile));
                    Require(files.Any(f => Bytes(f) == Bytes(asset)), "Required vehicle or spider weapon asset missing.");
                }
            }
            foreach (var group in files.GroupBy(f => f.Name.Split('.')[0]))
            {
                var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out var index, out _); return index; }).Distinct().Order().ToArray();
                Require(indices.SequenceEqual(Enumerable.Range(0, indices.Length)), "Numbering gap.");
            }
            var wantedArmor = armor.Where(f => PackProfiles.Includes(f, delta ? "commandos" : "clonedivers", profile)).ToList();
            Require(wantedArmor.Select(Main).Distinct().Count() == 9 && wantedArmor.All(f => files.Any(x => Bytes(x) == Bytes(f))), "Commando armor missing with this voice/profile choice.");
            Require(files.Count(f => f.Name.EndsWith(".patch_0") && !f.Name.StartsWith("9ba626afa44a3aa3.")) == 89, "Original-archive scope dependencies missing.");
            Require(files.Any(f => f.Option == "commandos"), "Required Delta audio/labels missing.");
            Require(files.Any(f => f.Option == "droids") == droids, "Droid toggle failed.");
            var hider = candidate.Files.Where(f => Main(f) == "9ba626afa44a3aa3.patch_198" && f.Size > 0).ToList();
            Require(!hider.Any(f => files.Any(x => Bytes(x) == Bytes(f))), "Obsolete cannon hider remains.");
            Require(LauncherModes.Current(ModState.On, new Settings { Options = new() { ["commandos"] = delta } }, candidate) == LauncherMode.Clonedivers, "Combined roster shows the wrong mode.");
            Console.WriteLine($"PASS saved delta={delta}, droids={droids}, saved cannons={aiming}, {profile}: {files.Count} validated files; Delta and weapons enforced.");
        }
        Console.WriteLine("PASS new asset hashes; preserved unrelated Full/Lighter bytes; removed obsolete scope overrides; 16 selection combinations. Gameplay not validated.");
    }
}
