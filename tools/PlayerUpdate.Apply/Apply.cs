using System.Diagnostics;
using System.Text.Json;
using Clonedivers;

static class PlayerUpdateApply
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static void Save(string path, object data) => File.WriteAllText(path, JsonSerializer.Serialize(data, Json));
    static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length is < 4 or > 5 || args[0] is not ("plan" or "apply")) throw new ArgumentException("plan|apply candidate-folder game-folder receipt-folder [loopback-feed-url]");
            var feedUrl = args.Length == 5 ? args[4] : "http://127.0.0.1:8767/manifest.json";
            if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var feedUri) || feedUri.Host != "127.0.0.1" || feedUri.Scheme != "http" || feedUri.AbsolutePath != "/manifest.json" || feedUri.Query.Length != 0 || feedUri.UserInfo.Length != 0)
                throw new ArgumentException("A loopback manifest URL is required.");
            var feed = Path.GetFullPath(args[1]); var game = Path.GetFullPath(args[2]); var audit = Path.GetFullPath(args[3]);
            if (!GameLocator.IsGameDir(game) || Game.IsRunning() || Process.GetProcessesByName("Clonedivers").Length != 0)
                throw new InvalidOperationException("Valid game required; close game and launcher before applying.");
            if (Pack.HasPendingUpdate(game, Settings.PlanPath)) throw new InvalidOperationException("An existing installer recovery must finish first.");
            var manifestPath = Path.Combine(feed, "manifest.json");
            var readinessPath = Path.Combine(Path.GetDirectoryName(feed)!, "test-readiness.json");
            using var readiness = JsonDocument.Parse(File.ReadAllText(readinessPath));
            if (await Pack.Sha256Async(manifestPath, CancellationToken.None) != readiness.RootElement.GetProperty("manifestSha256").GetString())
                throw new InvalidDataException("Candidate changed since verification.");
            var pack = Manifest.Parse(File.ReadAllText(manifestPath)).Pack!;
            var settings = Settings.Load();
            var options = settings.OptionsFor(pack);
            options["droids"] = true; options["aimpoints"] = true;
            var profile = settings.ProfileFor(pack);
            options["skinny"] = profile != "full";
            var wanted = Pack.EffectiveFiles(pack, id => options[id], profile);
            var cache = HashCache.Load(Settings.HashCachePath);
            Directory.CreateDirectory(audit);
            Console.WriteLine($"Checking current files for {profile}; Delta={options["commandos"]}.");
            var local = await Pack.InventoryAsync(game, wanted, cache, true, null, CancellationToken.None);
            var downloads = Path.Combine(game, Pack.DownloadFolder);
            var complete = await Pack.VerifiedDownloadsAsync(downloads, wanted, CancellationToken.None);
            var plan = Pack.Plan(game, wanted, local, complete, true);
            foreach (var file in plan.Downloads)
            {
                var source = FileSafety.Child(Path.Combine(feed, "assets"), file.Sha256);
                if (!File.Exists(source) || new FileInfo(source).Length != file.Size || await Pack.Sha256Async(source, CancellationToken.None) != file.Sha256)
                    throw new InvalidDataException("Required bytes unavailable locally: " + file.Name);
            }
            if (new DriveInfo(Path.GetPathRoot(game)!).AvailableFreeSpace < plan.BytesToDownload + plan.BytesToCopy + (512L << 20))
                throw new IOException("Insufficient space for the reversible candidate install.");
            Save(Path.Combine(audit, "plan-summary.json"), new { manifestPath, game, profile, options, wanted = wanted.Count, plan.BytesToDownload, plan.BytesToCopy, plan.ParkCount, plan.RenameCount, plan.InPlaceCount, plan.Actions });
            Console.WriteLine($"Ready: {wanted.Count} files; {plan.Downloads.Count} local staged assets ({plan.BytesToDownload / 1048576.0:F1} MiB); {plan.ParkCount} old files retained in mods_old.");
            if (args[0] == "plan") return 0;
            if (File.Exists(Path.Combine(audit, "settings-before.json"))) throw new IOException("Use a fresh receipt folder; an earlier install record already exists.");
            File.Copy(Settings.FilePath, Path.Combine(audit, "settings-before.json"));
            var receipt = Settings.ReceiptPathFor(game);
            if (File.Exists(receipt)) File.Copy(receipt, Path.Combine(audit, "receipt-before.json"));
            File.Copy(manifestPath, Path.Combine(audit, "candidate-manifest.json"));
            Save(Path.Combine(audit, "inventory-before.json"), local);
            Directory.CreateDirectory(downloads);
            foreach (var file in plan.Downloads)
            {
                var source = FileSafety.Child(Path.Combine(feed, "assets"), file.Sha256);
                var target = FileSafety.Child(downloads, file.Sha256);
                if (File.Exists(target)) throw new IOException("Unverified download collision; nothing applied: " + target);
                File.Copy(source, target);
                if (await Pack.Sha256Async(target, CancellationToken.None) != file.Sha256) throw new InvalidDataException("Staged copy failed verification.");
            }
            complete = await Pack.VerifiedDownloadsAsync(downloads, wanted, CancellationToken.None);
            plan = Pack.Plan(game, wanted, local, complete, true);
            if (plan.Downloads.Count != 0) throw new InvalidDataException("Incomplete staged downloads.");
            if (Game.IsRunning() || Process.GetProcessesByName("Clonedivers").Length != 0) throw new InvalidOperationException("Game or launcher started during preparation.");
            var result = Pack.Apply(game, plan, Settings.PlanPath, pack.Version, cache, Settings.HashCachePath,
                pack.GameBuild, options, profile, true, receipt);
            Save(Path.Combine(audit, "apply-result.json"), result);
            if (result.Interrupted) throw new IOException("Installer interrupted; preserve apply-plan.json for launcher recovery.");
            settings.RecordInstall(result);
            settings.ManifestUrl = feedUrl;
            settings.Save();
            var after = await Pack.InventoryAsync(game, wanted, new HashCache(), true, null, CancellationToken.None);
            if (!Pack.Plan(game, wanted, after, new HashSet<string>(), true).IsNoOp) throw new InvalidDataException("Installed inventory differs from candidate.");
            Save(Path.Combine(audit, "verified-install.json"), new { pack.Version, profile, options, files = wanted.Count,
                manifestSha256 = await Pack.Sha256Async(manifestPath, CancellationToken.None), utc = DateTimeOffset.UtcNow, runtimeTested = false });
            Console.WriteLine("APPLIED AND VERIFIED. Previous mod bytes are parked, settings/receipt backed up; gameplay validation pending.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
