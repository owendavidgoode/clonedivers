using Clonedivers;
using System.Text.Json;

static class EmpireRefinementCheck
{
    static void Require(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
    static string Identity(PackFile f) => $"{f.Name}|{f.Size}|{f.Sha256}|{f.Url}";
    static string Payload(PackFile f) => $"{f.Size}|{f.Sha256}|{f.Url}";
    static int Slot(PackFile f) { ModFiles.TryParsePatchName(f.Name, out var a, out var i, out _); return a == "9ba626afa44a3aa3" ? i : -1; }
    static async Task<int> Main(string[] args)
    {
        try { await Check(args); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static async Task Check(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("baseline candidate new-assets");
        var before = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
        var after = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
        var newSlots = new HashSet<int> { 320,325,326,327,328,329,330,331 };
        Require(after.Files.Count == before.Files.Count + 21, "Unexpected file membership");
        foreach (var old in before.Files.Where(f => Slot(f) != 320))
            Require(after.Files.Any(f => JsonSerializer.Serialize(f) == JsonSerializer.Serialize(old)), "Unrelated logical asset changed: " + old.Name);
        var changed = after.Files.Where(f => newSlots.Contains(Slot(f))).ToList();
        Require(changed.Count == 24 && changed.All(f => f.Modes!.SequenceEqual(new[] { "empire" })), "New assets must be Empire-only");
        foreach (var file in changed.Where(f => f.Size > 0).DistinctBy(f => f.Sha256))
        {
            var path = Path.Combine(args[2],file.Sha256);
            Require(new FileInfo(path).Length == file.Size && await Pack.Sha256Async(path,CancellationToken.None) == file.Sha256, "New asset hash mismatch");
        }
        var forbidden = new HashSet<int> {159,160,162,163,243,244,246,247,248,249,250,251,252};
        Require(after.Files.Where(f => forbidden.Contains(Slot(f))).All(f => !f.Modes!.Contains("empire")), "Clone voice, labels, map or Venator leaked");
        var checks = 0;
        foreach (var profile in PackProfiles.Supported(after))
        foreach (var droids in new[] {false,true})
        foreach (var covenant in new[] {false,true})
        foreach (var delta in new[] {false,true})
        {
            var settings = new Settings { TextureProfile=profile.Id, Options=new() { ["droids"]=droids,["covenant"]=covenant,["commandos"]=delta } };
            var clones = LauncherModes.OptionsFor(settings,after,LauncherMode.Clonedivers);
            Require(Pack.EffectiveFiles(before,id=>clones[id],profile.Id).Select(Identity).SequenceEqual(Pack.EffectiveFiles(after,id=>clones[id],profile.Id).Select(Identity)), "Clonedivers changed");
            var options = LauncherModes.OptionsFor(settings,after,LauncherMode.EmpireDivers);
            var empire = Pack.EffectiveFiles(after,id=>options[id],profile.Id);
            var oldEmpire = Pack.EffectiveFiles(before,id=>options[id],profile.Id);
            var expected = oldEmpire.Select(Payload).ToList();
            foreach (var replaced in before.Files.Where(f=>Slot(f)==320))
                Require(expected.Remove(Payload(replaced)), "Missing old Imperial file");
            expected.AddRange(changed.Select(Payload));
            Require(empire.Select(Payload).Order().SequenceEqual(expected.Order()), "Empire assets missing or changed");
            FileSafety.ValidateTargets(empire);
            foreach (var group in empire.GroupBy(f=>f.Name.Split('.')[0]))
            {
                var indices = group.Select(f=>{ModFiles.TryParsePatchName(f.Name,out _,out var i,out _);return i;}).Distinct().Order().ToArray();
                Require(indices.SequenceEqual(Enumerable.Range(0,indices.Length)), "Patch numbering gap");
            }
            Require(empire.Any(f=>f.Option=="covenant")==covenant && empire.Any(f=>f.Option=="droids")==droids,"Enemy toggles broken");
            foreach (var file in before.Files.Where(f=>Slot(f)==321 && f.Size>0))
                Require(empire.Any(f=>Payload(f)==Payload(file)),"SAI sound/laser mod changed");
            checks += 2;
            Console.WriteLine($"PASS {profile.Id}, droids={droids}, Covenant={covenant}, savedDelta={delta}: Clones unchanged; complete Empire upgrade; SAI retained");
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[1])!,"mode-check.json"),JsonSerializer.Serialize(new {modeSelections=checks,clonediversUnchanged=true,cloneVoicesExclusive=true,saiPreserved=true,gameplayTested=false}));
    }
}
