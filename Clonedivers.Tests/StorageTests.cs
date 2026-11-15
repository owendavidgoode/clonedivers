using System.Security.Cryptography;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.Tests;

internal static class StorageTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var area = Path.Combine(root, "storage"); Directory.CreateDirectory(area);
        Space(check);
        await Installers(Path.Combine(area, "installers"), check);
        await Appearance(Path.Combine(area, "appearance"), check);
    }
    static void Space(Action<bool, string> check)
    {
        foreach (var free in new[] { 50L << 30, (70L << 30) - 1 })
        {
            bool install = false, game = false;
            try { HellForgeSetup.CheckSetupSpace(free, @"Q:\"); }
            catch (IOException e) { install = e.Message.Contains(@"Q:\") && e.Message.Contains("70 GB"); }
            try { HellForgeSetup.CheckGameSpace(free, 0, @"Q:\"); }
            catch (IOException e) { game = e.Message.Contains(@"Q:\") && e.Message.Contains("70 GB"); }
            check(install && game, "both setup checks reject " + free + " bytes and name the drive and 70 GB budget");
        }
        HellForgeSetup.CheckSetupSpace(70L << 30, @"Q:\"); HellForgeSetup.CheckGameSpace(70L << 30, 0, @"Q:\");
        HellForgeSetup.CheckGameSpace(22L << 30, 48L << 30, @"Q:\");
        check(true, "70 GB is sufficient and resumed game bytes count towards the same budget");
    }
    static void FakeInstall(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "app", "cli")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "hellforge-install.json"), "{\"schemaVersion\":1,\"buildId\":\"25480438\"}");
        File.WriteAllText(Path.Combine(root, "HellForge.exe"), "fixture"); File.WriteAllText(Path.Combine(root, "app", "cli", "HellForge.Cli.exe"), "fixture");
    }
    static async Task Installers(string area, Action<bool, string> check)
    {
        var body = new byte[] { 1, 2, 3, 4, 5, 6 };
        var artifact = new PackFile { Name = "fixture.exe", Size = body.Length, Sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), Url = "https://invalid.example/never" };
        var legacy = Path.Combine(area, "legacy"); Directory.CreateDirectory(legacy);
        var target = Path.Combine(area, "new", "Forge"); var cache = HellForgeSetup.InstallCache(target);
        var old = HellForgeSetup.InstallerPath(legacy, artifact); File.WriteAllBytes(old, body);
        bool unpacked = false;
        var installed = await HellForgeSetup.InstallAsync(target, null, default, artifact: artifact,
            unpack: (installer, staging) => { unpacked = installer == HellForgeSetup.InstallerPath(cache, artifact); FakeInstall(staging); return Task.CompletedTask; },
            spaceCheck: _ => { }, cacheAvailableBytes: _ => long.MaxValue, legacyCache: legacy);
        check(installed == target && unpacked && !File.Exists(old) && File.ReadAllBytes(HellForgeSetup.InstallerPath(cache, artifact)).SequenceEqual(body),
            "default-cache install moves and runs the verified legacy installer without a network request or duplicate copy");
        File.Delete(HellForgeSetup.InstallerPath(cache, artifact)); File.WriteAllBytes(old, body[..3]);
        bool stopped = false;
        try { await HellForgeSetup.InstallAsync(Path.Combine(area, "new", "OtherForge"), null, default, artifact: artifact,
            spaceCheck: _ => { }, cacheAvailableBytes: _ => 0, legacyCache: legacy); }
        catch (IOException) { stopped = true; }
        check(stopped && !File.Exists(old) && File.ReadAllBytes(HellForgeSetup.InstallerPath(cache, artifact)).SequenceEqual(body[..3]),
            "default-cache install moves a legacy partial before capacity preflight so its exact bytes remain resumable");
        File.WriteAllBytes(old, body[..2]);
        await HellForgeSetup.ReuseInstallerAsync(legacy, cache, artifact, default);
        check(!File.Exists(old) && new FileInfo(HellForgeSetup.InstallerPath(cache, artifact)).Length == 3,
            "installer migration keeps the longer target partial and removes the redundant legacy copy");
        File.WriteAllBytes(old, new byte[body.Length]);
        await HellForgeSetup.ReuseInstallerAsync(legacy, cache, artifact, default);
        check(!File.Exists(old) && new FileInfo(HellForgeSetup.InstallerPath(cache, artifact)).Length == 3,
            "a corrupt full legacy installer cannot replace a useful target partial");
        File.WriteAllBytes(old, body);
        await HellForgeSetup.ReuseInstallerAsync(legacy, cache, artifact, default);
        check(!File.Exists(old) && File.ReadAllBytes(HellForgeSetup.InstallerPath(cache, artifact)).SequenceEqual(body),
            "a verified legacy installer replaces a shorter partial without retaining two copies");
    }
    static async Task Appearance(string area, Action<bool, string> check)
    {
        var root = Path.Combine(area, "Forge"); FakeInstall(root);
        var commands = new Commands(root); var client = new HellForgeClient(root, commands) { AvailablePackBytes = _ => long.MaxValue, RequireCliQuiescent = () => { } };
        var body = new byte[300000]; body[0] = 7;
        var source = Path.Combine(area, "source"); Directory.CreateDirectory(source); var local = Path.Combine(source, "asset"); File.WriteAllBytes(local, body);
        var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var file = new PackFile { Name = "appearance.patch_0", Size = body.Length, Sha256 = hash };
        var selection = new HellForgePack.Selection("clonedivers", "full", "current", HellForgePack.SupportedBuild, hash, new(), [file]);
        async Task<string> Export(string mode, string version, int age)
        {
            var output = Path.Combine(client.CacheRoot, "exports", mode + "-" + Guid.NewGuid().ToString("N"));
            var selected = selection with { Mode = mode, Version = version };
            await HellForgePack.ExportAsync(new(selected, output, new() { [hash] = local }), false);
            Directory.SetLastWriteTimeUtc(output, DateTime.UtcNow.AddMinutes(age)); return output;
        }
        var obsolete = await Export("clonedivers", "obsolete", -10);
        var current = await Export("clonedivers", "current", -9);
        var newest = await Export("clonedivers", "newest", -8);
        var empireOld = await Export("empire", "old", -7); var empireNewest = await Export("empire", "newest", -6);
        var manual = Path.Combine(client.CacheRoot, "exports", "manual-export"); Directory.CreateDirectory(manual); File.WriteAllText(Path.Combine(manual, "sentinel"), "keep");
        var unfinished = Path.Combine(client.CacheRoot, "exports", "empire-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(unfinished);
        string Backup(string state)
        {
            var folder = Path.Combine(client.CacheRoot, "backups", "20260101-000000-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            if (state.Length > 0) File.WriteAllText(Path.Combine(folder, "outcome.json"), JsonSerializer.Serialize(new { outcome = state }));
            return folder;
        }
        var oldApplied = Backup("applied"); var oldRestored = Backup("restored"); var incomplete = Backup("");
        await client.InstallPackAsync(current, selection);
        using var firstIntent = JsonDocument.Parse(File.ReadAllText(client.PackIntentPath)); var firstBackup = firstIntent.RootElement.GetProperty("Backup").GetString()!;
        check(!Directory.Exists(oldApplied) && !Directory.Exists(oldRestored) && Directory.Exists(firstBackup) && Directory.Exists(incomplete),
            "successful apply prunes older completed backups and keeps the current and incomplete backups");
        check(!Directory.Exists(obsolete) && !Directory.Exists(empireOld) && Directory.Exists(current) && Directory.Exists(newest) && Directory.Exists(empireNewest),
            "successful apply keeps installed selections and the newest export per mode while pruning superseded receipts");
        check(File.Exists(Path.Combine(manual, "sentinel")) && Directory.Exists(unfinished), "storage pruning preserves manual folders and unreceipted exports");
        var pendingText = File.ReadAllText(client.PackIntentPath).Replace("\"applied\"", "\"pending\""); File.WriteAllText(client.PackIntentPath, pendingText);
        var pendingBackup = Backup("applied"); var pendingExport = await Export("empire", "superseded", -20);
        client.PrunePackStorage(firstBackup, new(commands.Mods, [], null));
        check(Directory.Exists(pendingBackup) && Directory.Exists(pendingExport), "pending appearance intent prevents all completed-storage pruning");
        File.WriteAllText(client.PackIntentPath, pendingText.Replace("\"pending\"", "\"applied\""));
        commands.FailSwitch = true;
        bool restored = false;
        try { await client.InstallPackAsync(newest, selection with { Version = "newest" }); }
        catch (IOException e) { restored = e.Message.Contains("were restored"); }
        using var restoredIntent = JsonDocument.Parse(File.ReadAllText(client.PackIntentPath)); var restoredBackup = restoredIntent.RootElement.GetProperty("Backup").GetString()!;
        check(restored && restoredIntent.RootElement.GetProperty("State").GetString() == "restored" && Directory.Exists(restoredBackup) && !Directory.Exists(firstBackup) && !Directory.Exists(pendingBackup) && !Directory.Exists(pendingExport),
            "successful automatic restore prunes completed history only after its terminal recovery record and keeps its own backup");
        var blocked = Backup("recovery-needed"); var retained = Backup("applied");
        client.PrunePackStorage(restoredBackup, new(commands.Mods, [], null));
        check(Directory.Exists(retained) && Directory.Exists(blocked), "recovery-needed outcome prevents cleanup even when the latest intent is terminal");
        var progress = new List<(long Done, long Total)>();
        var duplicate = file.Clone(); duplicate.Name = "duplicate.patch_0";
        var plan = new HellForgePack.Plan(selection with { Files = [file, duplicate] }, Path.Combine(area, "progress-export"), new() { [hash] = local });
        await HellForgePack.ExportAsync(plan, false, bytes: new Bytes(progress.Add));
        check(progress.Count > 4 && progress.Any(p => p.Done > 0 && p.Done < body.Length) && progress.Any(p => p.Done > body.Length && p.Done < body.Length * 2) &&
            progress.Last() == ((long)body.Length * 2, (long)body.Length * 2) && progress.All(p => p.Total == body.Length * 2) && progress.Zip(progress.Skip(1)).All(p => p.First.Done <= p.Second.Done),
            "optional public byte callback tracks cumulative local and duplicate assets in chunks through the full export");
    }
    sealed class Bytes(Action<(long Done, long Total)> report) : IProgress<(long Done, long Total)>
    { public void Report((long Done, long Total) value) => report(value); }
    sealed class Commands(string root) : IHellForgeCommands
    {
        internal List<HellForgeMod> Mods = [new(HellForgeClient.DifficultyId, "Difficulty", true, Path.Combine(root, "mods", "difficulty"), [])];
        internal bool FailSwitch;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            object result;
            switch (args[0])
            {
                case "status": result = new { install = root, hellforge = HellForgeRelease.Version, engine = HellForgeRelease.Engine, modSdk = HellForgeRelease.Sdk, gameBuild = HellForgePack.SupportedBuild, networkBlock = "wfp",
                    ready = new { gameCopy = true, sessionRunner = true }, session = new { running = false }, settings = new { multiplayer = true, localPlayFab = true, sandbox = true } }; break;
                case "mods": result = new { mods = Mods.Select(m => new { id = m.Id, name = m.Name, enabled = m.Enabled, folder = m.Folder, tags = m.Tags, version = m.Version }), problems = Array.Empty<string>() }; break;
                case "mod-add":
                    using (var manifest = JsonDocument.Parse(File.ReadAllText(args[2])))
                    {
                        var id = "pack." + manifest.RootElement.GetProperty("Guid").GetString(); var folder = Path.Combine(root, "mods", "selected"); Directory.CreateDirectory(folder);
                        foreach (var path in Directory.GetFiles(folder)) File.Delete(path);
                        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(args[2])!)) File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
                        Mods.RemoveAll(m => m.Id == id); Mods.Add(new(id, "Selected", true, folder, [])); result = new { id };
                    }
                    break;
                case "mod-switch":
                    if (FailSwitch) { FailSwitch = false; throw new IOException("fixture appearance switch failure"); }
                    var index = Mods.FindIndex(m => m.Id == args[2]); Mods[index] = Mods[index] with { Enabled = args[4] == "yes" }; result = new { ok = true }; break;
                case "settings": result = new { ok = true }; break;
                default: throw new InvalidOperationException("Unexpected storage fixture command: " + args[0]);
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
