using System.Text.Json;
using Clonedivers;

static class ProfileCheck
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("Accepted invalid input: " + name); }
    static PackFile File(string name, string sha, string[]? modes = null, string[]? profiles = null) => new() { Name = name, Size = 1, Sha256 = sha.PadLeft(64, '0'), Url = "https://example.com/file", Modes = modes?.ToList(), TextureProfiles = profiles?.ToList() };
    static Manifest Parse(Manifest manifest) => Manifest.Parse(JsonSerializer.Serialize(manifest));
    static Manifest Fixture() => new() { Format = 3, Pack = new() {
        Options = new() { new() { Id = "commandos" } },
        TextureProfiles = new() { new() { Id = "full", Name = "Full" }, new() { Id = "lighter", Name = "Lighter" }, new() { Id = "reduced", Name = "Reduced" } },
        Files = new() {
            File("9ba626afa44a3aa3.patch_0", "1", profiles: new[]{"full"}),
            File("9ba626afa44a3aa3.patch_0", "2", profiles: new[]{"lighter","reduced"}),
            File("9ba626afa44a3aa3.patch_1", "3", new[]{"commandos"}, new[]{"full"}),
            File("9ba626afa44a3aa3.patch_1", "4", new[]{"commandos"}, new[]{"lighter","reduced"}),
            File("9ba626afa44a3aa3.patch_2", "5")
        }
    } };
    static int Main(string[] args)
    {
        try {
            var pack = Parse(Fixture()).Pack!;
            foreach (var mode in new[]{false,true}) foreach (var profile in new[]{"full","lighter","reduced"}) {
                var result=Pack.EffectiveFiles(pack, id => id == "commandos" && mode, profile);
                Check(result.Count == (mode ? 3 : 2), $"mode/profile count {mode}/{profile}");
                Check(result.Select(f => f.Name).SequenceEqual(Enumerable.Range(0,result.Count).Select(i=>$"9ba626afa44a3aa3.patch_{i}")), $"gap-free {mode}/{profile}");
                Check(result[0].Sha256.EndsWith(profile == "full" ? "1" : "2"), $"quality replacement {mode}/{profile}");
                Check(mode || result.All(f => !f.Sha256.EndsWith("3") && !f.Sha256.EndsWith("4")), $"RC remains gated {mode}/{profile}");
            }
            Reject(()=>Pack.EffectiveFiles(pack, _=>false,"typo"),"unknown explicit profile rejected");
            var copied=pack.Files[1].Clone(); copied.TextureProfiles!.Clear(); Check(pack.Files[1].TextureProfiles!.Count==2,"clone owns its profile list");
            var bad=Fixture();bad.Pack!.Files.Add(bad.Pack.Files[0].Clone());Reject(()=>Parse(bad),"overlapping duplicate rejected");
            bad=Fixture();bad.Pack!.Files[0].TextureProfiles=new(){"typo"};Reject(()=>Parse(bad),"unknown file profile rejected");
            bad=Fixture();bad.Pack!.Files[0].Modes=new(){"helldivers"};Reject(()=>Parse(bad),"invalid visual mode rejected");
            bad=Fixture();bad.Pack!.Files[0].Modes=new();Reject(()=>Parse(bad),"empty mode predicate rejected");
            bad=Fixture();bad.Pack!.TextureProfiles.Add(new(){Id="full",Name="again"});Reject(()=>Parse(bad),"duplicate profile rejected");
            bad=Fixture();bad.Format=2;Reject(()=>Parse(bad),"format 2 cannot silently accept format 3 conditions");
            bad=Fixture();bad.Format=99;Reject(()=>Parse(bad),"future format rejected");
            var legacy=new Manifest{Format=2,Pack=new(){Options=new(){new(){Id="skinny"}},Files=new(){File("9ba626afa44a3aa3.patch_0","1"),File("9ba626afa44a3aa3.patch_0","2")}}};
            legacy.Pack.Files[0].UnlessOption="skinny";legacy.Pack.Files[1].Option="skinny";
            var old=Parse(legacy).Pack!;Check(Pack.EffectiveFiles(old,_=>true)[0].Sha256.EndsWith("2"),"legacy skinny choice migrates");
            Check(Pack.EffectiveFiles(old,_=>true,"full")[0].Sha256.EndsWith("1"),"explicit full overrides stale skinny");
            Check(Pack.EffectiveFiles(old,_=>false,"lighter")[0].Sha256.EndsWith("2"),"explicit lighter selects legacy variant");
            foreach(var path in args) {
                var actual=Manifest.Parse(System.IO.File.ReadAllText(path));
                foreach(var rc in new[]{false,true}) foreach(var profile in PackProfiles.Supported(actual.Pack!)) {
                    var files=Pack.EffectiveFiles(actual.Pack!,id=>id=="commandos"?rc:actual.Pack!.Options.FirstOrDefault(o=>o.Id==id)?.Default??false,profile.Id);
                    FileSafety.ValidateTargets(files);Check(files.Count>0,$"candidate valid {Path.GetFileName(path)} rc={rc}/{profile.Id} ({files.Count} files)");
                }
            }
            Console.WriteLine($"PASS {checks} profile checks");return 0;
        } catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
