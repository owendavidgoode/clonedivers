using System.Text.Json;
using Clonedivers;

static class CustomizationCheck
{
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    static string Bytes(PackFile f) => $"{f.Size}|{f.Sha256}|{f.Url}";
    static string Identity(PackFile f) => $"{f.Name}|{Bytes(f)}";
    static async Task Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("baseline-v3 candidate-v3 ownership-report");
        var baseline = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
        var candidate = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
        using var report = JsonDocument.Parse(File.ReadAllText(args[2]));
        var cis = report.RootElement.EnumerateArray().Where(r => r.GetProperty("mod").GetString() == "Automaton to CIS Overhaul")
            .Select(r => r.GetProperty("file").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(cis.Count == 33, "Unexpected CIS ownership inventory.");
        Require(candidate.Files.Select(Identity).Order().SequenceEqual(baseline.Files.Select(Identity).Order()), "Candidate changes asset identities.");
        foreach (var file in candidate.Files)
        {
            ModFiles.TryParsePatchName(file.Name, out var archive, out var index, out _);
            Require((file.Option == "droids") == cis.Contains($"{archive}.patch_{index}"), "Droid gate includes an unrelated asset or misses a CIS companion.");
        }
        Require(candidate.Options.Single(o => o.Id == "droids").Default, "Existing players must retain their droid visuals by default.");
        foreach (var commandos in new[] { false, true })
        foreach (var profile in new[] { "full", "lighter" })
        {
            bool Enabled(string id) => id == "commandos" ? commandos : id == "skinny" ? profile == "lighter" : true;
            var original = Pack.EffectiveFiles(baseline, Enabled, profile);
            var on = Pack.EffectiveFiles(candidate, Enabled, profile);
            var off = Pack.EffectiveFiles(candidate, id => id == "droids" ? false : Enabled(id), profile);
            Require(on.Select(Identity).SequenceEqual(original.Select(Identity)), "Default selection differs from r9.");
            var excluded = candidate.Files.Where(f => f.Option == "droids" && PackProfiles.Includes(f, commandos ? "commandos" : "clonedivers", profile)).ToList();
            Require(off.Count + excluded.Count == on.Count, "Incorrect number of files removed.");
            Require(off.Select(Bytes).Concat(excluded.Select(Bytes)).Order().SequenceEqual(on.Select(Bytes).Order()), "Toggle changes unrelated payloads.");
            FileSafety.ValidateTargets(off);
            foreach (var group in off.GroupBy(f => f.Name.Split('.')[0]))
            {
                var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out var i, out _); return i; }).Distinct().Order();
                Require(indices.SequenceEqual(Enumerable.Range(0, indices.Count())), "Toggle leaves patch-number gaps.");
            }
            Console.WriteLine($"PASS commandos={commandos}/{profile}: on={on.Count}, off={off.Count}; baseline, ownership, payload and numbering verified.");
        }

        // Exercise renumbering, parking, cache reuse and vanilla preservation with real installer code.
        var folder = Path.Combine(Path.GetTempPath(), "clonedivers-extras-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        var fixture = new PackManifest { Version = "fixture", Options = new() { new() { Id = "droids" } } };
        for (int i = 0; i < 3; i++)
        {
            var name = $"a.patch_{i}";
            var path = Path.Combine(folder, "data", name);
            File.WriteAllText(path, "distinct payload " + i);
            fixture.Files.Add(new PackFile { Name = name, Size = new FileInfo(path).Length,
                Sha256 = await Pack.Sha256Async(path, CancellationToken.None), Url = "https://example.com/fixture", Option = i == 1 ? "droids" : null });
        }
        foreach (var step in new[] { (enabled: false, active: true), (enabled: true, active: true), (enabled: false, active: false), (enabled: true, active: false) })
        {
            var wanted = Pack.EffectiveFiles(fixture, _ => step.enabled, "full");
            var local = await Pack.InventoryAsync(folder, wanted, new HashCache(), true, null, CancellationToken.None);
            var plan = Pack.Plan(folder, wanted, local, new HashSet<string>(), step.active);
            Require(plan.Downloads.Count == 0, "Cached round trip unnecessarily downloads files.");
            var result = Pack.Apply(folder, plan, Path.Combine(folder, "plan.json"), fixture.Version, null, null, null,
                new() { ["droids"] = step.enabled }, "full", true, Path.Combine(folder, "receipt.json"));
            Require(!result.Interrupted, "Fixture apply interrupted.");
            var target = Path.Combine(folder, step.active ? "data" : "mods_off");
            foreach (var file in wanted) Require(await Pack.Sha256Async(Path.Combine(target, file.Name), CancellationToken.None) == file.Sha256, "Incorrect file after renumbering.");
            if (!step.active) Require(!ModFiles.ListPatchFiles(Path.Combine(folder, "data")).Any(), "Parked option change enabled mods.");
        }
        Console.WriteLine("PASS cached on/off round trip and parked changes preserve bytes, need no downloads, and keep vanilla inactive.");
        Console.WriteLine("Fixture retained: " + folder);
    }
}
