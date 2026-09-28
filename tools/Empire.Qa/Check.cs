using System.Diagnostics;
using System.Text.Json;
using Clonedivers;

// Uses the launcher's real selection, inventory and installer code. Plan is read-only.
static class EmpireQa
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    static void RequireClosed() => Require(!Game.IsRunning() && Process.GetProcessesByName("Clonedivers").Length == 0, "Close game and launcher first.");

    static async Task<int> Main(string[] args)
    {
        try
        {
            Require(args.Length == 4 && args[0] is "plan" or "apply", "plan|apply manifest game-folder fresh-audit-folder");
            var manifestPath = Path.GetFullPath(args[1]);
            var game = Path.GetFullPath(args[2]);
            var audit = Path.GetFullPath(args[3]);
            Require(GameLocator.IsGameDir(game), "Invalid game directory.");
            RequireClosed();
            Require(!Directory.Exists(audit), "Use a fresh audit folder.");
            Require(!Pack.HasPendingUpdate(game, Settings.PlanPath), "Finish pending recovery first.");
            var manifestHash = await Pack.Sha256Async(manifestPath, CancellationToken.None);
            var pack = Manifest.Parse(File.ReadAllText(manifestPath)).Pack!;
            var selections = new List<object>();
            foreach (var profileChoice in PackProfiles.Supported(pack))
            foreach (var droids in new[] { false, true })
            foreach (var covenant in new[] { false, true })
            foreach (var mode in new[] { LauncherMode.Clonedivers, LauncherMode.EmpireDivers })
            {
                var selection = new Settings { TextureProfile = profileChoice.Id, Options = new() { ["droids"] = droids, ["covenant"] = covenant } };
                var selectedOptions = LauncherModes.OptionsFor(selection, pack, mode);
                var files = Pack.EffectiveFiles(pack, id => selectedOptions[id], profileChoice.Id);
                FileSafety.ValidateTargets(files);
                selections.Add(new { mode = mode.ToString(), profile = profileChoice.Id, droids, covenant, files });
            }
            Directory.CreateDirectory(audit);
            Save(Path.Combine(audit, "selections.json"), selections);
            var settings = Settings.Load();
            var options = LauncherModes.OptionsFor(settings, pack, LauncherMode.EmpireDivers);
            var profile = settings.ProfileFor(pack);
            var wanted = Pack.EffectiveFiles(pack, id => options[id], profile);
            var cache = new HashCache();
            var local = await Pack.InventoryAsync(game, wanted, cache, true, null, CancellationToken.None);
            var downloads = Path.Combine(game, Pack.DownloadFolder);
            var complete = await Pack.VerifiedDownloadsAsync(downloads, wanted, CancellationToken.None);
            var plan = Pack.Plan(game, wanted, local, complete, true);
            Save(Path.Combine(audit, "plan.json"), new { manifestHash, pack.Version, profile, options, wanted = wanted.Count, plan.BytesToDownload, plan.BytesToCopy, plan.ParkCount, plan.RenameCount, plan.Downloads });
            Save(Path.Combine(audit, "inventory.json"), local);
            Console.WriteLine($"{selections.Count} selections; Empire {profile}: {wanted.Count} files, {plan.Downloads.Count} downloads ({plan.BytesToDownload / 1048576.0:F1} MiB), {plan.ParkCount} parked, {plan.RenameCount} renamed.");
            if (args[0] == "plan") return 0;
            Require(new DriveInfo(Path.GetPathRoot(game)!).AvailableFreeSpace >= plan.BytesToDownload + plan.BytesToCopy + (512L << 20), "Insufficient disk space.");
            File.Copy(manifestPath, Path.Combine(audit, "manifest.json"));
            if (File.Exists(Settings.FilePath)) File.Copy(Settings.FilePath, Path.Combine(audit, "settings-before.json"));
            var receipt = Settings.ReceiptPathFor(game);
            if (File.Exists(receipt)) File.Copy(receipt, Path.Combine(audit, "receipt-before.json"));
            RequireClosed();
            await Pack.DownloadPlanAsync(plan, downloads, null, CancellationToken.None);
            RequireClosed();
            Require(await Pack.Sha256Async(manifestPath, CancellationToken.None) == manifestHash, "Manifest changed during preparation.");
            var result = Pack.Apply(game, plan, Settings.PlanPath, pack.Version, cache, Settings.HashCachePath,
                pack.GameBuild, options, profile, true, receipt);
            Save(Path.Combine(audit, "apply-result.json"), result);
            Require(!result.Interrupted, "Interrupted; preserve recovery plan and use Finish update.");
            settings.RecordInstall(result);
            settings.Save();
            var installed = await Pack.InventoryAsync(game, wanted, new HashCache(), true, null, CancellationToken.None);
            Require(Pack.Plan(game, wanted, installed, new HashSet<string>(), true).IsNoOp, "Installed files do not match the selection.");
            Save(Path.Combine(audit, "verified.json"), new { manifestHash, pack.Version, profile, options, files = wanted.Count, utc = DateTimeOffset.UtcNow, gameplayTested = false });
            Console.WriteLine("PASS installed inventory freshly hashed; repair is a no-op. Gameplay remains untested.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
