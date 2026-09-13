using Clonedivers;

if (args.Length != 4) throw new ArgumentException("Usage: release-manifest tested-manifest baseline-manifest game-directory");
var release = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
var tested = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
var baseline = Manifest.Parse(File.ReadAllText(args[2])).Pack!;
string Identity(PackFile f) => $"{f.Name}|{f.Size}|{f.Sha256}";
foreach (var f in release.Files.Where(f => f.Size > 0))
    if (!f.Url.StartsWith("https://github.com/owendavidgoode/clonedivers/releases/download/"))
        throw new Exception("Non-release asset URL");
if (release.Options.Count != 3) throw new Exception("Unexpected options");
for (int mask = 0; mask < 8; mask++)
{
    bool Enabled(string id) => (mask & (1 << release.Options.FindIndex(o => o.Id == id))) != 0;
    var wanted = Pack.EffectiveFiles(release, Enabled);
    FileSafety.ValidateTargets(wanted);
    if (!wanted.Select(Identity).Order().SequenceEqual(Pack.EffectiveFiles(tested, Enabled).Select(Identity).Order()))
        throw new Exception("Release differs from tested option combination " + mask);
    if (!Enabled("commandos") && !wanted.Select(Identity).Order().SequenceEqual(Pack.EffectiveFiles(baseline, Enabled).Select(Identity).Order()))
        throw new Exception("Clonedivers baseline changed " + mask);
    foreach (var group in wanted.GroupBy(f => f.Name.Split('.')[0]))
    {
        var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out int i, out _); return i; }).Distinct().Order();
        if (!indices.SequenceEqual(Enumerable.Range(0, indices.Count()))) throw new Exception("Patch numbering gap");
    }
    Console.WriteLine($"PASS options {mask}: {wanted.Count} unique gap-free files match tested content.");
}
var active = Pack.EffectiveFiles(release, id => id != "skinny");
var data = Path.Combine(args[3], "data");
if (!ModFiles.ListPatchFiles(data).Select(Path.GetFileName).Order().SequenceEqual(active.Select(f => f.Name).Order()))
    throw new Exception("Live file set differs");
foreach (var f in active)
{
    var path = Path.Combine(data, f.Name);
    if (new FileInfo(path).Length != f.Size || await Pack.Sha256Async(path, CancellationToken.None) != f.Sha256)
        throw new Exception("Live hash differs: " + f.Name);
}
Console.WriteLine($"PASS: all {active.Count} deployed files match the release; all eight option combinations validated.");
