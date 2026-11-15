using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Clonedivers;

namespace Clonedivers.Tests;

internal static class DownloadCacheTests
{
    internal static int RunOnly()
    {
        int failures = 0, checks = 0; Game.IsRunningCheck = () => false;
        var root = Path.Combine(Path.GetTempPath(), "download-cache-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        void Check(bool ok, string name) { checks++; if (!ok) failures++; Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name); }
        try { Run(root, Check).GetAwaiter().GetResult(); StorageTests.Run(root, Check).GetAwaiter().GetResult(); }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"{checks} download/cache checks, {failures} failures"); return failures == 0 ? 0 : 1;
    }
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var area = Path.Combine(root, "download-cache"); Directory.CreateDirectory(area);
        await Appearance(area, check);
        await InstallerSpace(area, check);
    }
    static async Task Appearance(string area, Action<bool, string> check)
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 2 * 1024 * 1024));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var file = new PackFile { Name = "appearance.patch_0", Size = bytes.Length, Sha256 = hash,
            Url = "https://github.com/owendavidgoode/clonedivers/releases/download/test/appearance" };
        var selection = new HellForgePack.Selection("empire", "full", "test", HellForgePack.SupportedBuild, hash, new(), [file]);
        string Output() => Path.Combine(area, "exports", "empire-" + Guid.NewGuid().ToString("N"));
        using var server = new HalfAssetServer(bytes);
        using var cancel = new CancellationTokenSource();
        var first = new HellForgePack.Plan(selection, Output(), new()); bool unpublished = false, stopped = false;
        Task Download(PackFile asset, string path, CancellationToken ct)
        {
            var local = asset.Clone(); local.Url = server.Url;
            return Pack.DownloadAsync(local, path, new SyncProgress(p => {
                if (p.done == bytes.Length / 2 && !cancel.IsCancellationRequested)
                {
                    unpublished = !File.Exists(Path.Combine(first.Output, file.Name)) && !File.Exists(Path.Combine(first.Output, HellForgePack.ReceiptName));
                    cancel.Cancel();
                }
            }), ct);
        }
        try { await HellForgePack.ExportAsync(first, true, null, cancel.Token, _ => long.MaxValue, Download); }
        catch (OperationCanceledException) { stopped = true; }
        var cache = HellForgePack.DownloadCache(first.Output); var partial = Path.Combine(cache, hash + ".partial");
        check(stopped && unpublished && new FileInfo(partial).Length == bytes.Length / 2 && !Directory.Exists(first.Output),
            "cancelling halfway through the first appearance asset retains exactly half outside the empty export without publishing content or a receipt");
        var old = Path.Combine(cache, new string('a', 64) + ".partial"); File.WriteAllText(old, "obsolete");
        var second = await HellForgePack.PlanAsync(selection, Output(), []);
        var reports = new List<(long done, long total)>();
        await HellForgePack.ExportAsync(second, true, null, CancellationToken.None, _ => long.MaxValue, Download, new SyncProgress(reports.Add));
        await HellForgePack.VerifyAsync(second.Output, selection);
        check(server.Offsets.SequenceEqual(new[] { 0, bytes.Length / 2 }) && File.ReadAllBytes(Path.Combine(second.Output, file.Name)).SequenceEqual(bytes),
            "a new GUID export resumes the retained first asset with HTTP Range for its exact byte count and verifies the completed export");
        check(!File.Exists(partial) && !File.Exists(old) && Path.GetDirectoryName(cache) == area,
            "completed cached bytes and obsolete hash entries are pruned outside completed export folders");
        check(reports.Contains((bytes.Length / 2, bytes.Length)) && reports.Last() == ((long)bytes.Length, (long)bytes.Length) &&
            reports.Zip(reports.Skip(1)).All(p => p.First.done <= p.Second.done),
            "export byte progress includes the resumed partial and reaches the cumulative total without going backwards");

        var bad = file.Clone(); bad.Sha256 = new string('b', 64);
        var failed = new HellForgePack.Plan(selection with { Files = [bad] }, Output(), new());
        bool rejected = false;
        try { await HellForgePack.ExportAsync(failed, true, null, CancellationToken.None, _ => long.MaxValue,
            (_, path, ct) => File.WriteAllBytesAsync(path, bytes, ct)); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && !Directory.Exists(failed.Output), "unverified cached content cannot be copied into an export or publish a receipt");
    }
    static async Task InstallerSpace(string area, Action<bool, string> check)
    {
        var target = Path.Combine(area, "roomy-target", "Forge");
        var cache = Path.Combine(area, "full-cache-volume"); Directory.CreateDirectory(cache);
        var artifact = new PackFile { Name = "fixture.exe", Size = 100, Sha256 = new string('c', 64), Url = "https://invalid.example/never" };
        var path = HellForgeSetup.InstallerPath(cache, artifact); File.WriteAllBytes(path, new byte[40]);
        string? checkedTarget = null, checkedCache = null; bool unpacked = false;
        long? Free(string folder) { checkedCache = folder; return HellForgeSetup.CacheReserve + 59; }
        IOException? failure = null;
        try { await HellForgeSetup.InstallAsync(target, null, CancellationToken.None, cache, artifact,
            (_, _) => { unpacked = true; return Task.CompletedTask; }, folder => checkedTarget = folder, Free); }
        catch (IOException error) { failure = error; }
        check(failure != null && checkedTarget == target && checkedCache == cache && failure.Message.Contains(cache) && failure.Message.Contains(Path.GetPathRoot(cache)!) &&
            !unpacked && !Directory.Exists(target) && new FileInfo(path).Length == 40,
            "roomy target does not bypass a separate full installer cache; preflight names the cache path/drive before downloading or unpacking");
        await HellForgeSetup.RequireCacheSpaceAsync(cache, [artifact], 0, CancellationToken.None, _ => HellForgeSetup.CacheReserve + 60);
        check(true, "installer capacity subtracts retained resumable bytes exactly");
        File.WriteAllBytes(path, new byte[100]); bool corruptCounted = false;
        try { await HellForgeSetup.RequireCacheSpaceAsync(cache, [artifact], 0, CancellationToken.None, _ => HellForgeSetup.CacheReserve + 99); }
        catch (IOException) { corruptCounted = true; }
        check(corruptCounted, "a full-size corrupt cached installer still requires its missing verified bytes");
        checkedCache = null; failure = null; unpacked = false;
        try { await HellForgeSetup.InstallAsync(target, null, CancellationToken.None, cache: null, artifact: artifact,
            unpack: (_, _) => { unpacked = true; return Task.CompletedTask; }, spaceCheck: _ => { },
            cacheAvailableBytes: folder => { checkedCache = folder; return 0; }); }
        catch (IOException error) { failure = error; }
        var adjacent = Path.Combine(Path.GetDirectoryName(target)!, "Clonedivers-HellForge-cache", "installers");
        check(checkedCache == adjacent && failure?.Message.Contains(adjacent) == true && !unpacked,
            "InstallAsync with no cache checks the cache beside the chosen Forge target and names it before unpacking");
        var otherRoot = Path.GetPathRoot(target)!.Equals(@"C:\", StringComparison.OrdinalIgnoreCase) ? @"D:\" : @"C:\";
        var otherDrive = Path.Combine(otherRoot, "Clonedivers-capacity-fixture-" + Guid.NewGuid().ToString("N"), "installers");
        bool separateVolume = false;
        try { await HellForgeSetup.RequireCacheSpaceAsync(otherDrive, [artifact], 0, CancellationToken.None, folder => folder == otherDrive ? 0 : long.MaxValue); }
        catch (IOException error) { separateVolume = error.Message.Contains(otherDrive) && error.Message.Contains(otherRoot); }
        check(Path.GetPathRoot(target) != Path.GetPathRoot(otherDrive) && separateVolume,
            "a cache on another volume fails its own capacity check even when the selected target has room");

        var upgrade = Path.Combine(area, "upgrade"); FakeInstall(upgrade);
        var profile = Path.Combine(upgrade, "profiles", "default.json"); Directory.CreateDirectory(Path.GetDirectoryName(profile)!); File.WriteAllBytes(profile, new byte[300]);
        var previous = artifact.Clone(); previous.Size = 200; previous.Sha256 = new string('d', 64);
        File.WriteAllBytes(path, new byte[40]);
        var backup = HellForgeSetup.ProtectedBackupBytes(upgrade); checkedCache = null; failure = null; int reports = 0;
        Task<HellForgeStatus> Status(CancellationToken _) => Task.FromResult(new HellForgeStatus(upgrade, "0.3.0-beta", HellForgePack.SupportedBuild,
            true, true, false, true, true, null, null, Engine: "0.1.41", ModSdk: 15));
        try { await HellForgeSetup.UpdateAsync(upgrade, new SetupProgress(_ => reports++), CancellationToken.None, cache, artifact, previous,
            (_, _) => { unpacked = true; return Task.CompletedTask; }, Status, _ => { }, folder => {
                checkedCache = folder; return HellForgeSetup.CacheReserve + 60 + 200 + backup - 1;
            }); }
        catch (IOException error) { failure = error; }
        check(failure != null && failure.Message.Contains(cache) && checkedCache == cache && !unpacked && reports == 0 && !Directory.Exists(Path.Combine(cache, "backups")),
            "upgrade preflights missing bytes for both installers plus the actual protected-state backup on the cache volume before any download");
        var body = Encoding.UTF8.GetBytes("verified installer");
        var verified = artifact.Clone(); verified.Size = body.Length; verified.Sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        File.WriteAllBytes(HellForgeSetup.InstallerPath(cache, verified), body);
        await HellForgeSetup.RequireCacheSpaceAsync(cache, [verified, verified], backup, CancellationToken.None, _ => HellForgeSetup.CacheReserve + backup);
        check(true, "verified retained installers need no extra download capacity while protected backup and metadata space remain reserved");
    }
    static void FakeInstall(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "app", "cli")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "hellforge-install.json"), "{\"schemaVersion\":1,\"buildId\":\"25480438\"}");
        File.WriteAllText(Path.Combine(root, "HellForge.exe"), "fixture"); File.WriteAllText(Path.Combine(root, "app", "cli", "HellForge.Cli.exe"), "fixture");
    }
    sealed class SyncProgress(Action<(long done, long total)> report) : IProgress<(long done, long total)> { public void Report((long done, long total) value) => report(value); }
    sealed class SetupProgress(Action<ForgeSetupProgress> report) : IProgress<ForgeSetupProgress> { public void Report(ForgeSetupProgress value) => report(value); }
    sealed class HalfAssetServer : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0); readonly CancellationTokenSource stopping = new();
        readonly byte[] bytes; readonly Task accepting;
        internal readonly System.Collections.Concurrent.ConcurrentQueue<int> Offsets = new();
        internal string Url { get; }
        internal HalfAssetServer(byte[] bytes)
        {
            this.bytes = bytes; listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/asset"; accepting = Accept();
        }
        async Task Accept()
        {
            try { while (!stopping.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stopping.Token); _ = Respond(client); } }
            catch (OperationCanceledException) { } catch (SocketException) { }
        }
        async Task Respond(TcpClient client)
        {
            using (client)
            try
            {
                using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                int offset = 0; string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(stopping.Token)))
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) offset = int.Parse(line[13..].Split('-')[0]);
                Offsets.Enqueue(offset);
                var header = $"HTTP/1.1 {(offset > 0 ? "206 Partial Content" : "200 OK")}\r\nContent-Length: {bytes.Length - offset}\r\nConnection: close\r\n";
                if (offset > 0) header += $"Content-Range: bytes {offset}-{bytes.Length - 1}/{bytes.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + "\r\n"), stopping.Token);
                await stream.WriteAsync(bytes.AsMemory(offset, offset == 0 ? bytes.Length / 2 : bytes.Length - offset), stopping.Token);
                await stream.FlushAsync(stopping.Token);
                if (offset == 0) await Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        public void Dispose() { stopping.Cancel(); listener.Stop(); accepting.GetAwaiter().GetResult(); }
    }
}
