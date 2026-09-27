using Clonedivers;

static class FactionCheck
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("baseline-manifest covenant-manifest");
            var baseline = Manifest.Parse(File.ReadAllText(args[0])).Pack!;
            var candidate = Manifest.Parse(File.ReadAllText(args[1])).Pack!;
            Require(PackExtras.Visible(candidate).Any(o => o.Id == "covenant" && !o.Default), "Covenant must be visible and opt-in.");
            var watcher = baseline.Files.Where(f => f.Name.StartsWith("9ba626afa44a3aa3.patch_314.") || f.Name == "9ba626afa44a3aa3.patch_314").ToList();
            Require(watcher.Count == 3, "Expected three Watcher files.");
            var added = candidate.Files.Where(f => f.Option == "covenant").ToList();
            Require(added.Count == 3, "Expected one Covenant bundle.");
            foreach (var profile in PackProfiles.Supported(candidate))
            foreach (var droids in new[] { false, true })
            foreach (var delta in new[] { false, true })
            {
                bool Enabled(string id) => id == "droids" ? droids : id == "commandos" ? delta : id is not ("covenant" or "atst" or "empire");
                var before = Pack.EffectiveFiles(baseline, Enabled, profile.Id);
                var off = Pack.EffectiveFiles(candidate, Enabled, profile.Id);
                Require(before.Select(f => (f.Name, f.Size, f.Sha256)).SequenceEqual(off.Select(f => (f.Name, f.Size, f.Sha256))), "Covenant off must exactly reproduce the baseline.");
                var on = Pack.EffectiveFiles(candidate, id => id == "covenant" || Enabled(id), profile.Id);
                FileSafety.ValidateTargets(on);
                Require(on.Count == before.Count, "Covenant replaces exactly one bundle.");
                foreach (var file in added) Require(on.Any(f => f.Sha256 == file.Sha256), "Covenant asset missing.");
                foreach (var file in watcher.Where(f => f.Size > 0)) Require(on.All(f => f.Sha256 != file.Sha256), "Probe audio conflicts with Engineer audio.");
                var retained = before.Where(f => !watcher.Any(w => w.Size > 0 && w.Sha256 == f.Sha256) && f.Size > 0).Select(f => (f.Size, f.Sha256)).OrderBy(x => x.Sha256).ToList();
                var actual = on.Where(f => !added.Any(a => a.Size > 0 && a.Sha256 == f.Sha256) && f.Size > 0).Select(f => (f.Size, f.Sha256)).OrderBy(x => x.Sha256).ToList();
                Require(retained.SequenceEqual(actual), "Unrelated pack content changed.");
                Console.WriteLine($"PASS Covenant on/off and Watcher isolation: {profile.Id}, droids={droids}, delta={delta}");
            }
            if (candidate.Options.Any(o => o.Id == "atst"))
            {
                var atstFiles = candidate.Files.Where(f => f.Option == "atst").ToList();
                Require(atstFiles.Count == 3, "Expected one AT-ST bundle.");
                Require(!candidate.Options.Single(o => o.Id == "atst").Default, "AT-ST test must be opt-in.");
                var parked = candidate.Files.Where(f => f.UnlessOption == "atst").ToList();
                Require(parked.Count == 12, "Only two AT-TE bundles in Full/Lighter may be gated.");
                foreach (var profile in PackProfiles.Supported(candidate))
                foreach (var droids in new[] { false, true })
                foreach (var covenant in new[] { false, true })
                {
                    bool Enabled(string id) => id == "droids" ? droids : id == "covenant" ? covenant : id != "atst";
                    var before = Pack.EffectiveFiles(candidate, Enabled, profile.Id);
                    var after = Pack.EffectiveFiles(candidate, id => id == "atst" || Enabled(id), profile.Id);
                    FileSafety.ValidateTargets(after);
                    Require(after.Count == before.Count - 3, "AT-ST replaces two bundles with one.");
                    foreach (var file in atstFiles.Where(f => f.Size > 0)) Require(after.Any(f => f.Sha256 == file.Sha256), "AT-ST asset missing.");
                    // Retained AT-TE variants can share texture bytes. Compare multiplicities,
                    // so removing two bundles does not forbid a shared stream still in use.
                    var expected = before.Where(f => f.Size > 0).Select(f => (f.Size, f.Sha256)).ToList();
                    foreach (var file in parked.Where(f => f.Size > 0 && f.TextureProfiles?.Contains(profile.Id) == true))
                        Require(expected.Remove((file.Size, file.Sha256)), "Expected AT-TE entry missing from baseline.");
                    expected.AddRange(atstFiles.Where(f => f.Size > 0).Select(f => (f.Size, f.Sha256)));
                    var actual = after.Where(f => f.Size > 0).Select(f => (f.Size, f.Sha256)).OrderBy(x => x.Sha256).ThenBy(x => x.Size);
                    Require(expected.OrderBy(x => x.Sha256).ThenBy(x => x.Size).SequenceEqual(actual), "AT-ST changes unrelated content.");
                    Console.WriteLine($"PASS AT-ST isolation: {profile.Id}, droids={droids}, covenant={covenant}");
                }
            }
            if (LauncherModes.SupportsEmpire(candidate))
            {
                Require(!candidate.Options.Single(o => o.Id == "empire").Default && PackExtras.Visible(candidate).All(o => o.Id != "empire"), "Empire is an opt-in mode, not an extra toggle.");
                var exclusive = candidate.Files.Where(f => f.Modes?.SequenceEqual(new[] { "empire" }) == true).ToList();
                Require(exclusive.Count == 6, "Empire needs the AT-ST and Imperial visual bundles.");
                var forbidden = baseline.Files.Where(f => new[] { 0, 1, 2, 159, 160, 161, 214, 230, 231, 232, 233, 234, 235, 238 }
                    .Any(i => f.Name == $"9ba626afa44a3aa3.patch_{i}") && f.Size > 0).Select(f => f.Sha256).ToHashSet();
                foreach (var profile in PackProfiles.Supported(candidate))
                foreach (var droids in new[] { false, true })
                foreach (var covenant in new[] { false, true })
                {
                    var settings = new Settings { TextureProfile = profile.Id, Options = new() { ["droids"] = droids, ["covenant"] = covenant } };
                    var options = LauncherModes.OptionsFor(settings, candidate, LauncherMode.EmpireDivers);
                    var empire = Pack.EffectiveFiles(candidate, id => options[id], profile.Id);
                    FileSafety.ValidateTargets(empire);
                    Require(empire.All(f => !forbidden.Contains(f.Sha256)), "Republic armor, voices, ship or AT-TE leaked into Empire.");
                    foreach (var file in exclusive.Where(f => f.Size > 0)) Require(empire.Any(f => f.Sha256 == file.Sha256), "An Imperial asset is missing.");
                    Require(added.Where(f => f.Size > 0).All(f => empire.Any(e => e.Sha256 == f.Sha256) == covenant), "Empire ignored Covenant choice.");
                    Require(watcher.Where(f => f.Size > 0).All(f => empire.Any(e => e.Sha256 == f.Sha256) != covenant), "Empire Watcher conflict.");
                    foreach (var group in empire.GroupBy(f => f.Name.Split('.')[0]))
                    {
                        var indices = group.Select(f => { ModFiles.TryParsePatchName(f.Name, out _, out var index, out _); return index; }).Distinct().Order().ToArray();
                        Require(indices.SequenceEqual(Enumerable.Range(0, indices.Length)), "Empire numbering has a gap.");
                    }
                    settings.Options = options;
                    var back = LauncherModes.OptionsFor(settings, candidate, LauncherMode.Clonedivers);
                    Require(!back["empire"] && back["covenant"] == covenant && back["droids"] == droids, "Returning to Clones changed independent choices.");
                    Console.WriteLine($"PASS Empire contents and return to Clones: {profile.Id}, droids={droids}, covenant={covenant}");
                }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
