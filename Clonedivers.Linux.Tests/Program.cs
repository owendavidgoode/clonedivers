using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Clonedivers;

var root = Path.Combine(Path.GetTempPath(), "clonedivers-linux-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}
try
{
    Game.IsRunningCheck = () => false;
    var home = Path.Combine(root, "home");
    var library = Path.Combine(root, "External Library");
    var steam = LinuxPlatform.SteamRoots(home).First();
    var flatpak = LinuxPlatform.SteamRoots(home).Last();
    Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
    Directory.CreateDirectory(Path.Combine(flatpak, "steamapps"));
    File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"path\" \"" + library.Replace("\\", "\\\\") + "\"");
    var game = Path.Combine(library, "steamapps", "common", "Helldivers 2");
    Directory.CreateDirectory(Path.Combine(game, "data"));
    Directory.CreateDirectory(Path.Combine(game, "bin"));
    File.WriteAllText(Path.Combine(game, "bin", "helldivers2.exe"), "fixture");
    File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_553850.acf"), "\"buildid\" \"25480438\"");
    Check(LinuxPlatform.CandidateGameDirs(home).First() == game, "Steam discovery prefers the external installed library and preserves spaces");
    Check(LinuxPlatform.CandidateGameDirs(home).Any(p => p.StartsWith(flatpak)), "Flatpak Steam is discovered alongside native Steam");
    Check(GameLocator.NormalizeGameDir(Path.Combine(game, "data")) == game, "game folder normalization accepts data child");
    var proc = Path.Combine(root, "proc");
    Directory.CreateDirectory(Path.Combine(proc, "123"));
    File.WriteAllText(Path.Combine(proc, "123", "comm"), "wine64");
    File.WriteAllText(Path.Combine(proc, "123", "cmdline"), "wine64\0Z:\\games\\Helldivers 2\\bin\\helldivers2.exe\0");
    Check(LinuxPlatform.IsGameRunning(proc), "Proton/Wine executable is detected through argv");
    File.WriteAllText(Path.Combine(proc, "123", "cmdline"), "steam\0");
    Check(!LinuxPlatform.IsGameRunning(proc), "Steam alone does not block pack changes");
    File.WriteAllText(Path.Combine(proc, "123", "comm"), "helldivers2.exe\n");
    Check(LinuxPlatform.IsGameRunning(proc), "game process name is detected directly");
    Throws<DirectoryNotFoundException>(() => LinuxPlatform.IsGameRunning(Path.Combine(root, "missing-proc")), "missing proc does not claim the game is closed");

    var user = Path.Combine(library, "steamapps", "compatdata", "553850", "pfx", "drive_c", "users", "steamuser");
    Directory.CreateDirectory(user);
    var previousCompat = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH");
    Environment.SetEnvironmentVariable("STEAM_COMPAT_DATA_PATH", null);
    Check(LinuxPlatform.FpvValuesPath(game) == Path.Combine(user, "AppData", "Local", "CowboyBingus", "Helldivers2", "Logs", "ModOptionsMenu.values"), "FPV options resolve inside the game's Proton prefix");
    Environment.SetEnvironmentVariable("STEAM_COMPAT_DATA_PATH", previousCompat);

    using var server = new FixtureServer();
    PackFile Entry(string name, byte[] bytes, string route) => new() { Name = name, Size = bytes.Length,
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Url = server.Add(route, bytes) };
    var cloneBytes = Encoding.UTF8.GetBytes("Republic archive fixture");
    var empireBytes = Encoding.UTF8.GetBytes("Imperial archive fixture");
    var clone = Entry("abc.patch_0", cloneBytes, "clone");
    var empire = Entry("abc.patch_0", empireBytes, "empire");
    var empty = new PackFile { Name = "abc.patch_0.stream", Size = 0, Sha256 = PackFile.EmptySha };
    var partial = Path.Combine(root, "partial");
    File.WriteAllBytes(partial, cloneBytes[..5]);
    await Pack.DownloadAsync(clone, partial, null, default);
    Check(server.SawRange && File.ReadAllBytes(partial).SequenceEqual(cloneBytes), "interrupted HTTP download resumes with Range and verifies SHA-256");
    var corrupt = Entry("bad.patch_0", cloneBytes, "bad"); corrupt.Sha256 = new string('0', 64);
    try { await Pack.DownloadAsync(corrupt, partial, null, default); throw new Exception("bad hash accepted"); }
    catch (InvalidDataException) { Check(!File.Exists(partial), "hash mismatch removes the untrusted download"); }
    Throws<InvalidDataException>(() => FileSafety.ValidatePatchName("../escape.patch_0"), "manifest path traversal is rejected");

    var state = Path.Combine(root, "state");
    var planPath = Path.Combine(state, "plan.json"); var receiptPath = Path.Combine(state, "receipt.json");
    File.WriteAllText(Path.Combine(game, "data", "base-archive"), "untouched");
    RecoveryTarget Target(PackFile file, string version) => new() { PackVersion = version, GameBuild = "25480438", TextureProfile = "full", TargetActive = true,
        Files = new() { file, empty }, Options = new() { ["empire"] = version == "empire" } };
    using (LinuxInstaller.Lock(game))
        Throws<IOException>(() => { using var second = LinuxInstaller.Lock(game); }, "two launchers cannot mutate the same installation");
    var result = await LinuxInstaller.ApplyAsync(game, Target(clone, "clone"), planPath, receiptPath, _ => true, null, default);
    Check(!result.Interrupted && File.ReadAllBytes(Path.Combine(game, "data", clone.Name)).SequenceEqual(cloneBytes), "clean install downloads and enables verified pack bytes");
    Check(new FileInfo(Path.Combine(game, "data", empty.Name)).Length == 0 && !File.Exists(planPath), "zero-byte companions finalize and recovery plan clears");
    var receipt = InstallReceipt.Load(receiptPath, game)!;
    Check(receipt.MatchesLayout(game), "successful install writes a matching receipt");
    Game.IsRunningCheck = () => true;
    try { await LinuxInstaller.ApplyAsync(game, Target(empire, "empire"), planPath, receiptPath, _ => true, null, default); throw new Exception("running game accepted"); }
    catch (InvalidOperationException) { Check(File.ReadAllBytes(Path.Combine(game, "data", clone.Name)).SequenceEqual(cloneBytes), "running-game guard preserves installed files"); }
    Game.IsRunningCheck = () => false;

    var plan = await LinuxInstaller.PlanAsync(game, Target(empire, "empire").Files, true, default);
    await Pack.DownloadPlanAsync(plan, Path.Combine(game, Pack.DownloadFolder), null, default);
    plan = await LinuxInstaller.PlanAsync(game, Target(empire, "empire").Files, true, default);
    var observations = 0; Game.IsRunningCheck = () => ++observations >= 2;
    result = Pack.Apply(game, plan, planPath, "empire", null, null, "25480438", new() { ["empire"] = true }, "full", true, receiptPath);
    Check(result.Interrupted && File.Exists(planPath), "game starting during apply preserves a durable recovery target");
    Game.IsRunningCheck = () => false;
    result = Pack.Replay(game, planPath, null, null, receiptPath)!;
    Check(!result.Interrupted && File.ReadAllBytes(Path.Combine(game, "data", empire.Name)).SequenceEqual(empireBytes), "interrupted mode change replays to the exact saved Empire target");
    Check(File.ReadAllBytes(Path.Combine(game, Pack.OldFolder, clone.Name)).SequenceEqual(cloneBytes), "mode change keeps replaced Republic bytes for recovery/reuse");
    var requests = server.Requests;
    result = await LinuxInstaller.ApplyAsync(game, Target(clone, "clone"), planPath, receiptPath, _ => true, null, default);
    Check(!result.Interrupted && requests == server.Requests, "switching back reuses cached bytes without HTTP downloads");
    ModFiles.MoveAllPatchFiles(Path.Combine(game, "data"), Path.Combine(game, ModFiles.OffFolder));
    Check(ModFiles.GetState(game) == ModState.Off && File.ReadAllText(Path.Combine(game, "data", "base-archive")) == "untouched", "vanilla parks mods and leaves base archives untouched");
    var parked = InstallReceipt.Load(receiptPath, game)!.ToRecoveryTarget(false);
    File.WriteAllText(Path.Combine(game, ModFiles.OffFolder, clone.Name), "damaged");
    result = await LinuxInstaller.ApplyAsync(game, parked, planPath, receiptPath, _ => true, null, default);
    Check(!result.Interrupted && ModFiles.GetState(game) == ModState.Off && File.ReadAllBytes(Path.Combine(game, ModFiles.OffFolder, clone.Name)).SequenceEqual(cloneBytes), "repair rehashes corrupt content while preserving vanilla mode");
    if (OperatingSystem.IsLinux())
    {
        Check(!PlatformPaths.Identity(game).Equals(PlatformPaths.Identity(game.ToUpperInvariant())), "Linux installation identity is case-sensitive");
        var linked = Path.Combine(root, "link.patch_0"); File.CreateSymbolicLink(linked, Path.Combine(game, "bin", "helldivers2.exe"));
        Throws<InvalidDataException>(() => FileSafety.RejectLink(linked), "Linux symlink targets are rejected");
        // On a case-sensitive filesystem, matching content with wrong name casing must be renamed.
        var upper = Path.Combine(game, "data", "ABC.patch_0"); File.WriteAllBytes(upper, cloneBytes);
        plan = await LinuxInstaller.PlanAsync(game, new[] { clone }, true, default);
        Check(plan.InPlaceCount == 0, "case-different filenames do not count as the correct installed target");
        var fpvValues = Path.Combine(root, "fpv", "ModOptionsMenu.values");
        FpvDefaults.EnsureForInstalledPack(game, () => fpvValues, clone.Size, clone.Sha256);
        Check(File.ReadAllText(fpvValues).Contains("firstperson.enabled\tfalse"), "FPV seeding uses Linux mutex/filesystem operations and defaults off");
        File.WriteAllText(fpvValues, "firstperson.enabled\ttrue\nfirstperson.bridge2\t2\ncustom\tvalue\n");
        var choices = File.ReadAllBytes(fpvValues);
        FpvDefaults.EnsureForInstalledPack(game, () => fpvValues, clone.Size, clone.Sha256);
        Check(File.ReadAllBytes(fpvValues).SequenceEqual(choices), "FPV preparation preserves existing Proton player choices exactly");

        // Exercise the actual command-line entry point in a separate Linux process.
        var cliGame = Path.Combine(root, "cli", "steamapps", "common", "Helldivers 2");
        Directory.CreateDirectory(Path.Combine(cliGame, "data")); Directory.CreateDirectory(Path.Combine(cliGame, "bin"));
        File.WriteAllText(Path.Combine(cliGame, "bin", "helldivers2.exe"), "fixture");
        File.WriteAllText(Path.Combine(cliGame, "..", "..", "appmanifest_553850.acf"), "\"buildid\" \"25480438\"");
        var feed = new Manifest { Format = 3, Pack = new PackManifest { Version = "fixture", GameBuild = "25480438", CombinedRoster = true,
            Options = new() { new() { Id = "empire", Default = false } }, TextureProfiles = new() { new() { Id = "full", Name = "Full" } },
            Files = new() { clone, empty } } };
        var feedUrl = server.Add("manifest", JsonSerializer.SerializeToUtf8Bytes(feed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var launcherDll = typeof(LinuxInstaller).Assembly.Location;
        var fakeBin = Path.Combine(root, "fake-bin"); Directory.CreateDirectory(fakeBin);
        var open = Path.Combine(fakeBin, "xdg-open");
        File.WriteAllText(open, "#!/bin/sh\nprintf '%s' \"$1\" > \"$CLONEDIVERS_TEST_URI\"\n");
        File.SetUnixFileMode(open, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var uriFile = Path.Combine(root, "uri");
        async Task<string> Cli(string command)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            foreach (var arg in new[] { launcherDll, "--game", cliGame, "--manifest", feedUrl, "--yes" }.Concat(command.Split(' '))) start.ArgumentList.Add(arg);
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
            start.Environment["PATH"] = fakeBin + ":" + Environment.GetEnvironmentVariable("PATH");
            start.Environment["CLONEDIVERS_TEST_URI"] = uriFile;
            using var child = Process.Start(start)!;
            child.StandardInput.Close();
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            var output = await stdout + await stderr;
            Check(child.ExitCode == 0, "CLI " + command + " exits successfully: " + output.Trim());
            return output;
        }
        await Cli("install clone");
        await Cli("status");
        await Cli("launch");
        for (var i = 0; i < 30 && !File.Exists(uriFile); i++) await Task.Delay(100);
        Check(File.ReadAllText(uriFile) == "steam://rungameid/553850", "Linux launch hands the correct Steam URI to the desktop handler");
        await Cli("vanilla");
        await Cli("repair");
        Check(ModFiles.GetState(cliGame) == ModState.Off, "actual CLI repair preserves a vanilla selection");
    }
    var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    var published = Manifest.Parse(File.ReadAllText(Path.Combine(repository, "manifest-v3-current.json"))).Pack!;
    foreach (var mode in new[] { LauncherMode.Clonedivers, LauncherMode.EmpireDivers })
    foreach (var profile in PackProfiles.Supported(published))
    {
        var options = LauncherModes.OptionsFor(new Settings(), published, mode);
        var files = Pack.EffectiveFiles(published, id => options.GetValueOrDefault(id), profile.Id);
        Check(files.Count > 0 && files.Select(f => f.Name).Distinct().Count() == files.Count, $"published {mode}/{profile.Id} selection has unique patch names");
    }
    Console.WriteLine($"ALL {passed} CROSS-PLATFORM CHECKS PASSED");
    return 0;
}
finally
{
    Game.IsRunningCheck = () => false;
    // Only the freshly-created unique fixture tree is removed.
    Directory.Delete(root, true);
}

sealed class FixtureServer : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly Dictionary<string, byte[]> content = new();
    readonly CancellationTokenSource stop = new();
    readonly Task serving;
    public bool SawRange { get; private set; }
    public int Requests { get; private set; }
    public FixtureServer() { listener.Start(); serving = Serve(); }
    public string Add(string route, byte[] bytes)
    {
        content["/" + route] = bytes;
        return $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{route}";
    }
    async Task Serve()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var first = await reader.ReadLineAsync() ?? "";
                var offset = 0; string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) { offset = int.Parse(line[13..].TrimEnd('-')); SawRange = true; }
                Requests++;
                var bytes = content[first.Split(' ')[1].Split('?')[0]];
                var headers = $"HTTP/1.1 {(offset > 0 ? "206 Partial Content" : "200 OK")}\r\nContent-Length: {bytes.Length - offset}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n";
                if (offset > 0) headers += $"Content-Range: bytes {offset}-{bytes.Length - 1}/{bytes.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"));
                await stream.WriteAsync(bytes.AsMemory(offset));
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); serving.GetAwaiter().GetResult(); stop.Dispose(); }
}
