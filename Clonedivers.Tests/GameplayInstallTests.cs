using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clonedivers;

namespace Clonedivers.Tests;

internal static class GameplayInstallTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var catalog = HellForgeGameplayCatalog.Pinned;
        check(catalog.Artifacts.Count == 8 && catalog.Artifacts.Sum(a => a.Bytes) == 7784074, "the shipped gameplay catalog covers all eight additional required artifacts");
        check(catalog.Artifacts.Count(a => a.Url.Contains("/owendavidgoode/clonedivers/")) == 3 && catalog.Artifacts.Count(a => !a.Url.Contains("/owendavidgoode/clonedivers/")) == 5, "only the two permitted compatibility packages and project-owned fix are mirrored; five dependencies come from authors");
        check(catalog.Artifacts.All(a => FileSafety.IsSha256(a.ContentHash) && FileSafety.IsSha256(a.Sha256)), "every gameplay dependency has independent archive and official content-hash pins");
        foreach (var entry in new[] { "../escape.dll", "C:/escape.dll", "safe/../../escape.dll", "safe/entry.dll" })
        {
            var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("fixture"); }
            var artifact = new GameplayArtifact("fixture", "fixture", "1", "https://example.invalid/fixture", new FileInfo(file).Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(), new string('a', 64), "fixture");
            var unsafePath = entry != "safe/entry.dll";
            try { await HellForgeGameplayCatalog.VerifyArchiveAsync(file, artifact); check(!unsafePath, "archive path is confined: " + entry); }
            catch (InvalidDataException) { check(unsafePath, "unsafe archive cannot reach the official importer: " + entry); }
            if (!unsafePath)
            {
                try { await HellForgeGameplayCatalog.VerifyArchiveAsync(file, artifact with { Sha256 = new string('b', 64) }); check(false, "wrong gameplay hash must fail"); }
                catch (InvalidDataException) { check(true, "size-correct wrong-hash gameplay artifacts fail before import"); }
            }
        }
    }

    internal static void RequireIsolated(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var local = @"D:\CodexScratch\hellforge\windows-onboarding-20261104\real-bootstrap";
        var temporary = full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("forge-gameplay-install-test-", StringComparison.Ordinal);
        if (!temporary && !full.Equals(local, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Real recovery trials require an explicitly isolated test installation.");
    }
    internal static async Task RealRecovery(string root)
    {
        RequireIsolated(root);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var official = new HellForgeCommands(full);
        var client = new HellForgeClient(full, official);
        if (client.InterruptedGameplayProblem() != null) await client.RecoverGameplayAsync();
        var catalog = HellForgeGameplayCatalog.Pinned;
        var before = await client.ModsAsync();
        if (before.Mods.Any(m => !catalog.Order.Contains(m.Id))) throw new InvalidOperationException("Test root contains unrelated mods; no trial changes were made.");
        foreach (var artifact in catalog.Artifacts)
            await HellForgeGameplayCatalog.VerifyArchiveAsync(Path.Combine(client.GameplayCache, artifact.FileName), artifact);
        var profiles = Path.Combine(full, "profiles");
        if (!Directory.EnumerateFiles(profiles, "*", SearchOption.AllDirectories).Any())
            await File.WriteAllTextAsync(Path.Combine(profiles, "preset-test-sentinel.txt"), "Isolated preservation fixture; no account or rewards.");
        var profileFiles = Directory.EnumerateFiles(profiles, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        // Only the known throwaway installation's eight artifacts are removed.
        foreach (var mod in before.Mods.Where(m => catalog.Artifacts.Any(a => a.Id == m.Id)))
            await official.RunAsync(["mod-remove", "--id", mod.Id]);
        var clean = await client.ModsAsync();
        if (clean.Mods.Count != 3) throw new InvalidDataException("Clean fixture must contain exactly three official bundled mods.");
        var fault = new FailOnce(official);
        try { await new HellForgeClient(full, fault).InstallGameplayAsync(client.GameplayCache); throw new Exception("Injected failure did not happen."); }
        catch (IOException error) when (error.Message.Contains("previous mod selection was restored", StringComparison.Ordinal)) { }
        var restored = await client.ModsAsync();
        if (restored.Mods.Count != 3 || client.InterruptedGameplayProblem() != null || (await client.MissingGameplayAsync()).Count != 8)
            throw new InvalidDataException("Fresh-import rollback failed.");
        Console.WriteLine("PASS: actual failed eight-mod installation restored all three bundled mods and cleared recovery");
        try { await new HellForgeClient(full, new LoseAcknowledgement(official)).InstallGameplayAsync(client.GameplayCache); throw new Exception("Lost acknowledgement was not injected."); }
        catch (HellForgeCommandOutcomeUnknownException) { }
        if (client.InterruptedGameplayProblem() == null) throw new InvalidDataException("An unknown import outcome did not preserve recovery intent.");
        var intentFile = client.PackIntentPath + ".gameplay.json";
        using (var intent = JsonDocument.Parse(File.ReadAllText(intentFile)))
        {
            var snapshotFile = Path.Combine(intent.RootElement.GetProperty("Backup").GetString()!, "before.json");
            var snapshotBytes = File.ReadAllBytes(snapshotFile);
            await File.WriteAllTextAsync(snapshotFile, "corrupted snapshot");
            try { await client.RecoverGameplayAsync(); throw new Exception("Corrupt recovery metadata was accepted."); }
            catch (InvalidDataException) { }
            if (client.InterruptedGameplayProblem() == null) throw new InvalidDataException("Invalid recovery metadata lost its pending state.");
            await File.WriteAllBytesAsync(snapshotFile, snapshotBytes);
        }
        await new HellForgeClient(full, official).RecoverGameplayAsync();
        if ((await client.ModsAsync()).Mods.Count != 3 || client.InterruptedGameplayProblem() != null) throw new InvalidDataException("Restart recovery did not restore the clean fixture.");
        Console.WriteLine("PASS: actual lost import acknowledgement stays pending, rejects damaged backup metadata and recovers through a fresh client");
        await client.InstallGameplayAsync(client.GameplayCache);
        if ((await client.MissingGameplayAsync()).Count != 0) throw new InvalidDataException("Actual retry left missing dependencies.");
        Console.WriteLine("PASS: actual retry installed and verified all eleven gameplay mods");
        var settings = Path.Combine(full, "mods", "hellforge-mods.json"); var originalSettings = File.ReadAllBytes(settings);
        await client.InstallGameplayAsync(client.GameplayCache);
        if (!File.ReadAllBytes(settings).SequenceEqual(originalSettings)) throw new InvalidDataException("An already complete preset was rewritten.");
        Console.WriteLine("PASS: actual repeated setup is a no-op with byte-identical mod settings");
        var installed = (await client.ModsAsync()).Mods.Single(m => m.Id == "pack.612eaf70-d682-43c7-9efd-16dcc695f977");
        var payload = Directory.EnumerateFiles(installed.Folder, "*", SearchOption.AllDirectories).First(p => Path.GetFileName(p) != "manifest.json" && new FileInfo(p).Length > 0);
        if (!Path.GetFullPath(payload).StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Test payload escaped isolated root.");
        using (var output = new FileStream(payload, FileMode.Append, FileAccess.Write)) output.WriteByte(0xff);
        if ((await client.MissingGameplayAsync()).Count != 1) throw new InvalidDataException("Changed unpacked data was not detected.");
        await client.InstallGameplayAsync(client.GameplayCache);
        if ((await client.MissingGameplayAsync()).Count != 0) throw new InvalidDataException("Official reimport reused the corrupt data cache.");
        Console.WriteLine("PASS: actual data-cache corruption is detected and repaired through official import into a fresh cache");
        foreach (var file in profileFiles)
            if (!File.Exists(file.Key) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Key))) != file.Value) throw new InvalidDataException("Gameplay setup changed a profile file.");
        Console.WriteLine("PASS: actual profile file bytes survived failure, retry and repair");
    }

    sealed class FailOnce(IHellForgeCommands inner) : IHellForgeCommands
    {
        bool failed;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (!failed && args[0] == "mod-pack") { failed = true; throw new IOException("Deliberate fixture configuration failure after imports."); }
            return inner.RunAsync(args, ct);
        }
    }
    sealed class LoseAcknowledgement(IHellForgeCommands inner) : IHellForgeCommands
    {
        bool failed;
        public async Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            var response = await inner.RunAsync(args, ct);
            if (!failed && args[0] == "mod-add") { failed = true; throw new HellForgeCommandOutcomeUnknownException("Fixture lost the successful import acknowledgement."); }
            return response;
        }
    }
}
