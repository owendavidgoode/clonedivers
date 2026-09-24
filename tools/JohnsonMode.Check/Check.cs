using System.Text.Json;
using Clonedivers;

namespace JohnsonMode;

// Uses the shipped planner and apply/recovery engine; never downloads or changes app settings.
static class Check
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    static async Task<List<PackFile>> Inventory(string directory)
    {
        var files = new List<PackFile>();
        foreach (var path in Directory.GetFiles(directory).Where(p => ModFiles.IsPatchFile(Path.GetFileName(p))))
            files.Add(new PackFile { Name = Path.GetFileName(path), Size = new FileInfo(path).Length,
                Sha256 = await Pack.Sha256Async(path, CancellationToken.None) });
        FileSafety.ValidateTargets(files);
        return files;
    }

    static async Task VerifyDirectory(string directory, List<PackFile> wanted)
    {
        var actual = await Inventory(directory);
        if (actual.Count != wanted.Count || wanted.Any(f => !actual.Any(a => a.Name == f.Name && a.Size == f.Size && a.Sha256 == f.Sha256)))
            throw new InvalidDataException("Installed files do not exactly match the target.");
        Console.WriteLine($"PASS: {actual.Count} installed filenames, sizes and SHA-256 hashes match.");
    }

    static async Task Apply(string game, string work, List<PackFile> wanted, string assets)
    {
        if (Game.IsRunning()) throw new InvalidOperationException("Close Helldivers 2 before installing/restoring.");
        var cache = new HashCache();
        var local = await Pack.InventoryAsync(game, wanted, cache, true, null, CancellationToken.None);
        var complete = await Pack.VerifiedDownloadsAsync(Path.Combine(game, Pack.DownloadFolder), wanted, CancellationToken.None);
        var plan = Pack.Plan(game, wanted, local, complete);
        var donors = (await Inventory(assets)).GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.First());
        Directory.CreateDirectory(Path.Combine(game, Pack.DownloadFolder));
        foreach (var file in plan.Downloads)
        {
            if (!donors.TryGetValue(file.Sha256, out var donor)) throw new InvalidDataException("Missing local asset " + file.Name);
            File.Copy(Path.Combine(assets, donor.Name), Path.Combine(game, Pack.DownloadFolder, file.Sha256), true);
        }
        complete = await Pack.VerifiedDownloadsAsync(Path.Combine(game, Pack.DownloadFolder), wanted, CancellationToken.None);
        plan = Pack.Plan(game, wanted, local, complete);
        if (plan.Downloads.Count != 0) throw new InvalidDataException("Local staging incomplete.");
        var result = Pack.Apply(game, plan, Path.Combine(work, "apply-plan.json"), "JohnsonMode-test", null, null);
        if (result.Interrupted) throw new IOException(result.Reason + ": " + string.Join(", ", result.Failed));
        await VerifyDirectory(Path.Combine(game, "data"), wanted);
    }

    static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[0] is not ("test" or "install" or "restore"))
                throw new ArgumentException("Usage: JohnsonMode.Check test|install|restore <build-directory> <game-directory> <test-state-directory>");
            var mode = args[0]; var build = Path.GetFullPath(args[1]); var game = Path.GetFullPath(args[2]); var state = Path.GetFullPath(args[3]);
            if (Game.IsRunning()) throw new InvalidOperationException("Helldivers 2 is running.");
            var backupPath = Path.Combine(state, "original-files.json");
            if (mode == "restore")
            {
                if (!string.Equals(File.ReadAllText(Path.Combine(state, "game-path.txt")), game, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Rollback belongs to a different game directory.");
                var original = JsonSerializer.Deserialize<List<PackFile>>(File.ReadAllText(backupPath), Json)!;
                await Apply(game, state, original, Path.Combine(state, "backup"));
                Console.WriteLine("RESTORED: exact original mod set. App settings were never changed.");
                return 0;
            }
            if (Directory.Exists(state)) throw new IOException("Use a fresh test-state directory; never overwrite a rollback record.");
            if (File.Exists(Settings.PlanPath) || ModFiles.HasStagedFiles(game)) throw new IOException("Finish the existing Clonedivers update first.");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "game-path.txt"), game);
            var source = Manifest.Parse(File.ReadAllText(Path.Combine(build, "source-manifest.json"))).Pack!;
            var candidateFiles = await Inventory(Path.Combine(build, "pack"));
            var expectedInventory = JsonSerializer.Deserialize<List<PackFile>>(File.ReadAllText(Path.Combine(build, "inventory.json")), Json)!;
            if (candidateFiles.Count != expectedInventory.Count || candidateFiles.Any(f => !expectedInventory.Any(e => e.Name == f.Name && e.Size == f.Size && e.Sha256 == f.Sha256)))
                throw new InvalidDataException("Candidate changed after verification.");
            foreach (var f in candidateFiles)
            {
                var baseName = f.Name.Replace(".gpu_resources", "").Replace(".stream", "");
                var parent = source.Files.First(p => p.Name == baseName && p.Option != "skinny");
                f.Option = parent.Option;
            }
            var pack = new PackManifest { Name = "JohnsonMode(tm)", Version = source.Version + "-johnson", GameBuild = source.GameBuild,
                Files = candidateFiles, Options = source.Options.Where(o => o.Id != "skinny").ToList() };
            for (int bits = 0; bits < (1 << pack.Options.Count); bits++)
            {
                int mask = bits;
                var choice = Pack.EffectiveFiles(pack, id => (mask & (1 << pack.Options.FindIndex(o => o.Id == id))) != 0);
                FileSafety.ValidateTargets(choice);
                foreach (var group in choice.GroupBy(f => f.Name.Split('.')[0]))
                {
                    var indices = group.Select(f => int.Parse(f.Name.Split(".patch_")[1].Split('.')[0])).Distinct().Order().ToArray();
                    if (!indices.SequenceEqual(Enumerable.Range(0, indices.Length))) throw new InvalidDataException("Optional-group numbering gap.");
                }
            }
            Console.WriteLine("PASS: every optional-group combination has unique, gap-free filenames.");
            File.WriteAllText(Path.Combine(build, "manifest-local.json"), JsonSerializer.Serialize(new Manifest { Pack = pack }, Json));
            var settings = Settings.Load();
            var wanted = Pack.EffectiveFiles(pack, id => settings.Options.TryGetValue(id, out var on) ? on : pack.Options.First(o => o.Id == id).Default);
            var originalFiles = await Inventory(Path.Combine(game, "data"));
            File.WriteAllText(backupPath, JsonSerializer.Serialize(originalFiles, Json));
            var backup = Path.Combine(state, "backup"); Directory.CreateDirectory(backup);
            foreach (var f in originalFiles) File.Copy(Path.Combine(game, "data", f.Name), Path.Combine(backup, f.Name));
            // Verify rollback bytes before the first mutation.
            await VerifyDirectory(backup, originalFiles);
            await Apply(game, state, wanted, Path.Combine(build, "pack"));
            if (mode == "test") await Apply(game, state, originalFiles, backup);
            Console.WriteLine(mode == "test" ? "PASS: installation and exact restoration completed." : "INSTALLED: JohnsonMode(tm). Restore using this same state directory.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
