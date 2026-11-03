using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
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
    Check(!DesktopProgram.UseTerminal(Array.Empty<string>(), true) && DesktopProgram.UseTerminal(Array.Empty<string>(), false), "desktop stays default with a display; headless sessions route to the TUI");
    Check(DesktopProgram.UseTerminal(new[] { "--tui" }, true) && !DesktopProgram.UseTerminal(new[] { "--gui" }, false), "explicit GUI and TUI choices override environment detection");
    Check(!DesktopProgram.UseTerminal(new[] { "--game", "status", "--manifest", "report" }, true), "option values are never mistaken for command names during interface routing");
    var view = new TerminalView("TEST", new[] { "\u001b]52;c;clipboard-payload\aSafe status\u001b[2J" }, Enumerable.Range(0, 30).Select(i => new TerminalItem(i.ToString(), "Choice " + i)).ToArray());
    var frame = TerminalUi.Render(view, 29, 30, 12, false);
    Check(frame.Contains("Choice 29") && frame.Split('\n', StringSplitOptions.RemoveEmptyEntries).All(l => l.Length <= 30) && frame.Split('\n').Length <= 12, "narrow terminal viewports keep the selected action visible without overflowing");
    Check(!frame.Contains('\u001b') && !frame.Contains("clipboard-payload"), "terminal rendering strips untrusted ANSI and clipboard-control sequences");
    Check(TerminalUi.NormalizePathInput("\u001b[200~'~/Game files'\u001b[201~", root) == Path.Combine(root, "Game files"), "folder input accepts quoted home paths and removes bracketed-paste artifacts");
    var longPath = "'/mnt/" + new string('a', 130) + "/Game folder/steam-capture.sh' %command%";
    var wrappedPath = TerminalUi.Wrap(longPath, 30).ToArray();
    Check(string.Concat(wrappedPath) == longPath && wrappedPath.All(line => line.Length <= 30), "long Steam launch commands wrap without losing path characters");
    var supportHome = Path.Combine(root, "support-home"); Directory.CreateDirectory(supportHome);
    var supportConfig = Path.Combine(root, "support-config");
    File.WriteAllText(Path.Combine(supportHome, "steam-553850.log"), $"Proton fixture\n{supportHome}/private/file\nhttps://example.invalid/log?token=secret-query\nAuthorization: Bearer secret-bearer\nSteam user 76561198000000000\n");
    for (var i = 0; i < 45; i++) LinuxSupport.Record("error", $"fixture {i}: password=secret-history", supportConfig);
    var supportSettings = new Settings { InstalledPackVersion = "test-pack", Telemetry = new() { Token = "secret-telemetry" } };
    var supportZip = LinuxSupport.SaveReport(null, supportSettings, Path.Combine(root, "support.zip"), supportConfig, supportHome);
    using (var zip = ZipFile.OpenRead(supportZip))
    {
        string Contents(string name) { using var reader = new StreamReader(zip.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
        using var json = JsonDocument.Parse(Contents("report.json"));
        Check(!json.RootElement.GetProperty("Selection").GetProperty("GameFound").GetBoolean(), "support report works without a detected game");
        using var history = JsonDocument.Parse(Contents("launcher-history.json"));
        Check(history.RootElement.GetArrayLength() == 40, "launcher error history is bounded and exported as valid JSON");
        var all = string.Join("\n", zip.Entries.Select(e => Contents(e.FullName)));
        Check(!all.Contains("secret-") && !all.Contains(supportHome) && !all.Contains("76561198000000000"), "report redacts home paths, Steam IDs, credentials and URL queries without exporting settings tokens");
        Check(Contents("session.txt").Contains("unknown"), "missing Proton version is reported as unknown");
    }
    Throws<IOException>(() => LinuxSupport.SaveReport(null, supportSettings, supportZip, supportConfig, supportHome), "support export never overwrites an existing report");
    var oversized = Path.Combine(root, "large.log"); File.WriteAllText(oversized, new string('x', 100000));
    Check(LinuxSupport.Tail(oversized, 64).Length < 128, "log collection reads a bounded tail instead of loading a full large log");
    var home = Path.Combine(root, "home");
    var library = Path.Combine(root, "External Library");
    var steam = LinuxPlatform.SteamRoots(home).First();
    var flatpak = LinuxPlatform.SteamRoots(home).Single(p => p.Contains("com.valvesoftware.Steam"));
    var snap = LinuxPlatform.SteamRoots(home).Single(p => p.Replace('\\', '/').EndsWith("/snap/steam/common/.local/share/Steam", StringComparison.Ordinal));
    Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
    Directory.CreateDirectory(Path.Combine(flatpak, "steamapps"));
    Directory.CreateDirectory(Path.Combine(snap, "steamapps"));
    File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"path\" \"" + library.Replace("\\", "\\\\") + "\"");
    var game = Path.Combine(library, "steamapps", "common", "Helldivers 2");
    Directory.CreateDirectory(Path.Combine(game, "data"));
    Directory.CreateDirectory(Path.Combine(game, "bin"));
    File.WriteAllText(Path.Combine(game, "bin", "helldivers2.exe"), "fixture");
    File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_553850.acf"), "\"buildid\" \"25480438\"");
    Check(LinuxPlatform.CandidateGameDirs(home).First() == game, "Steam discovery prefers the external installed library and preserves spaces");
    Check(LinuxPlatform.CandidateGameDirs(home).Any(p => p.StartsWith(flatpak)), "Flatpak Steam is discovered alongside native Steam");
    Check(LinuxPlatform.CandidateGameDirs(home).Any(p => p.StartsWith(snap)), "Ubuntu Snap Steam's shared data directory is discovered");
    var snapLibrary = Path.Combine(root, "Snap External Library");
    File.WriteAllText(Path.Combine(snap, "steamapps", "libraryfolders.vdf"), "\"path\" \"" + snapLibrary.Replace("\\", "\\\\") + "\"");
    Check(LinuxPlatform.CandidateGameDirs(home).Contains(Path.Combine(snapLibrary, "steamapps", "common", "Helldivers 2")), "Snap Steam external libraries are read from its own libraryfolders.vdf");
    void Libraries(string steamRoot, params string[] paths) => File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), string.Join('\n', paths.Select(p => "\"path\" \"" + p.Replace("\\", "\\\\") + "\"")));
    var nativeScript = Path.Combine(steam, "steam.sh"); File.WriteAllText(nativeScript, "fixture");
    var nativeLaunch = LinuxPlatform.SteamLaunchForGame(game, home);
    Check(nativeLaunch.Client == "Native Steam" && nativeLaunch.Command.FileName == Path.GetFullPath(nativeScript) && nativeLaunch.Command.ArgumentList.Single() == "steam://rungameid/553850" && !nativeLaunch.Command.UseShellExecute, "native launch uses the owning Steam root directly, preserving paths with spaces");
    Libraries(snap, library);
    Throws<InvalidOperationException>(() => LinuxPlatform.SteamLaunchForGame(game, home), "two Steam clients claiming one library cannot silently pick a default client");
    Libraries(steam);
    var snapLaunch = LinuxPlatform.SteamLaunchForGame(game, home);
    Check(snapLaunch.Client == "Snap Steam" && snapLaunch.Command.FileName == "snap" && snapLaunch.Command.ArgumentList.SequenceEqual(new[] { "run", "steam", "steam://rungameid/553850" }), "an external Snap library launches Snap Steam even with native Steam present");
    Libraries(snap); Libraries(flatpak, library);
    var flatpakLaunch = LinuxPlatform.SteamLaunchForGame(game, home);
    Check(flatpakLaunch.Client == "Flatpak Steam" && flatpakLaunch.Command.FileName == "flatpak" && flatpakLaunch.Command.ArgumentList.SequenceEqual(new[] { "run", "com.valvesoftware.Steam", "steam://rungameid/553850" }), "an external Flatpak library launches its application explicitly");
    Libraries(flatpak);
    Throws<InvalidOperationException>(() => LinuxPlatform.SteamLaunchForGame(game, home), "unregistered game folders cannot bootstrap a different Steam through a URI handler");
    Libraries(steam, library); File.Delete(nativeScript);
    Throws<InvalidOperationException>(() => LinuxPlatform.SteamLaunchForGame(game, home), "a missing native Steam launcher does not fall back to another installation");
    File.WriteAllText(nativeScript, "fixture");
    if (OperatingSystem.IsLinux())
    {
        var alias = Path.Combine(home, ".steam", "root"); Directory.CreateDirectory(Path.GetDirectoryName(alias)!); Directory.CreateSymbolicLink(alias, steam);
        Check(LinuxPlatform.SteamLaunchForGame(game, home).Client == "Native Steam", "Steam root symlink aliases resolve to one owner");
        var libraryAlias = Path.Combine(root, "library-alias"); Directory.CreateSymbolicLink(libraryAlias, library); Libraries(steam, libraryAlias);
        Check(LinuxPlatform.SteamLaunchForGame(game, home).Client == "Native Steam", "symlinked library paths match the selected real game folder");
        Libraries(steam); Libraries(snap, library); Directory.Delete(alias); Directory.CreateSymbolicLink(alias, snap);
        Check(LinuxPlatform.SteamLaunchForGame(game, home).Client == "Snap Steam", "a generic Steam root alias pointing inside Snap never becomes a native launch");
        Directory.Delete(alias); Libraries(steam, library); Libraries(snap, snapLibrary);
    }
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
    object Release(string tag, string arch = "x64", bool draft = false, string state = "uploaded") => new
    {
        tag_name = tag, draft, prerelease = true, html_url = "https://untrusted.invalid/ignored",
        assets = new[] { new { name = $"Clonedivers-linux-{arch}.tar.gz", state } }
    };
    var releaseJson = JsonSerializer.Serialize(new[] { Release("v99.0.0"), Release("linux-v1.9.0"), Release("linux-v1.10.0"), Release("linux-v2.0.0", draft: true), Release("linux-v4.0.0", "arm64"), Release("linux-v5.0.0", state: "new"), Release("linux-v6.0.0-beta") });
    var selectedRelease = LauncherUpdates.SelectRelease(releaseJson, "x64");
    Check(selectedRelease?.Version == "1.10.0" && selectedRelease.Url == "https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v1.10.0", "update notices choose numeric Linux versions with uploaded matching assets and ignore Windows, drafts and untrusted URLs");
    Check(LauncherUpdates.SelectRelease(releaseJson, "arm64")?.Version == "4.0.0", "launcher updates follow the current package architecture");
    var releaseFeed = server.Add("launcher-releases", Encoding.UTF8.GetBytes(releaseJson));
    var updateDirectory = Path.Combine(root, "update-config");
    var updateTime = DateTimeOffset.UtcNow;
    using var updateClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var updateService = new LauncherUpdates(updateClient, releaseFeed, updateDirectory, "x64", () => updateTime);
    var update = await updateService.CheckAsync();
    Check(update.CheckSucceeded && update.Available?.Version == "1.10.0", "a release check reports a newer Linux launcher without downloading a package");
    var releaseRequests = server.Requests; server.Remove("launcher-releases");
    Check((await updateService.CheckAsync()).Available?.Version == "1.10.0" && server.Requests == releaseRequests, "successful release checks are cached for six hours without another request");
    updateTime += TimeSpan.FromHours(7);
    var offlineUpdate = await updateService.CheckAsync();
    Check(!offlineUpdate.CheckSucceeded && offlineUpdate.Available?.Version == "1.10.0", "a failed refresh retains a previously known update without blocking the launcher");
    releaseRequests = server.Requests;
    await updateService.CheckAsync();
    Check(server.Requests == releaseRequests, "failed release checks are throttled for fifteen minutes");
    server.Add("launcher-releases", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { Release("linux-v1.11.0") })));
    Check((await updateService.CheckAsync(force: true)).Available?.Version == "1.11.0", "manual update checks bypass the automatic cache");
    server.Add("invalid-releases", Encoding.UTF8.GetBytes("not JSON"));
    var unavailableUpdate = await new LauncherUpdates(updateClient, releaseFeed.Replace("launcher-releases", "invalid-releases"), Path.Combine(root, "invalid-update-config")).CheckAsync();
    Check(!unavailableUpdate.CheckSucceeded && unavailableUpdate.Available is null, "invalid release responses remain an optional unavailable notice");
    File.WriteAllText(Path.Combine(updateDirectory, LauncherUpdates.CacheName), JsonSerializer.Serialize(new LauncherUpdates.Cache(updateTime, "https://untrusted.invalid", true)));
    Check((await updateService.CheckAsync()).Available?.Version == "1.11.0", "an invalid cached version cannot inject an external download link");
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
    var beforeDecline = server.Requests;
    try { await LinuxInstaller.ApplyAsync(game, Target(empire, "empire"), planPath, receiptPath, _ => false, null, default); throw new Exception("declined install accepted"); }
    catch (OperationCanceledException) { Check(server.Requests == beforeDecline && !File.Exists(planPath), "declining a plan leaves downloads and installed files untouched"); }
    try
    {
        await LinuxInstaller.ApplyAsync(game, Target(clone, "clone"), planPath, receiptPath, _ =>
        {
            File.WriteAllText(SteamAcf.AcfPath(game), "\"buildid\" \"99999999\""); return true;
        }, null, default);
        throw new Exception("changed game build accepted");
    }
    catch (IOException ex) { Check(ex.Message.Contains("changed the game build") && !File.Exists(planPath), "a Steam build change during preparation stops before writing a recovery plan or changing mods"); }
    File.WriteAllText(SteamAcf.AcfPath(game), "\"buildid\" \"25480438\"");
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
        var cloneVariant = clone.Clone(); cloneVariant.Modes = new() { "clonedivers", "commandos" };
        var empireVariant = empire.Clone(); empireVariant.Modes = new() { "empire" };
        var feed = new Manifest { Format = 3, Pack = new PackManifest { Version = "fixture", GameBuild = "25480438", CombinedRoster = true,
            Options = new() { new() { Id = "empire", Default = false }, new() { Id = "droids", Name = "Droid skins", Default = true }, new() { Id = "covenant", Name = "Covenant squids", Default = false } },
            TextureProfiles = new() { new() { Id = "full", Name = "Full" }, new() { Id = "lighter", Name = "Streamed" } },
            Files = new() { cloneVariant, empireVariant, empty } } };
        var feedUrl = server.Add("manifest", JsonSerializer.SerializeToUtf8Bytes(feed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var launcherDll = typeof(LinuxInstaller).Assembly.Location;
        var fakeBin = Path.Combine(root, "fake-bin"); Directory.CreateDirectory(fakeBin);
        var launchHome = Path.Combine(root, "launch-home");
        var nativeRoot = LinuxPlatform.SteamRoots(launchHome).First();
        var snapRoot = LinuxPlatform.SteamRoots(launchHome).Single(p => p.Replace('\\', '/').EndsWith("/snap/steam/common/.local/share/Steam", StringComparison.Ordinal));
        var flatpakRoot = LinuxPlatform.SteamRoots(launchHome).Single(p => p.Contains("com.valvesoftware.Steam"));
        foreach (var steamRoot in new[] { nativeRoot, snapRoot, flatpakRoot }) Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        var cliLibrary = Path.GetFullPath(Path.Combine(cliGame, "..", "..", "..")); Libraries(nativeRoot, cliLibrary);
        var clientFile = Path.Combine(root, "launched-client");
        var nativeLauncher = Path.Combine(nativeRoot, "steam.sh");
        File.WriteAllText(nativeLauncher, "#!/bin/sh\nprintf native > \"$CLONEDIVERS_TEST_CLIENT\"\nprintf '%s' \"$1\" > \"$CLONEDIVERS_TEST_URI\"\n");
        File.SetUnixFileMode(nativeLauncher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var client in new[] { "snap", "flatpak" })
        {
            var command = Path.Combine(fakeBin, client);
            File.WriteAllText(command, "#!/bin/sh\nprintf '" + client + "' > \"$CLONEDIVERS_TEST_CLIENT\"\nprintf '%s' \"$3\" > \"$CLONEDIVERS_TEST_URI\"\n");
            File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var open = Path.Combine(fakeBin, "xdg-open");
        File.WriteAllText(open, "#!/bin/sh\ncase \"$1\" in https://github.com/owendavidgoode/clonedivers/releases*) printf '%s' \"$1\" > \"$CLONEDIVERS_TEST_UPDATE_LINK\" ;; *) printf wrong-default-handler > \"$CLONEDIVERS_TEST_URI\" ;; esac\n");
        File.SetUnixFileMode(open, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var uriFile = Path.Combine(root, "uri");
        async Task<string> Cli(string command, int expectedExit = 0, string? chosenGame = null, bool accept = true, string? extraArgument = null, string? chosenFeed = null)
        {
            var binary = Environment.GetEnvironmentVariable("CLONEDIVERS_TEST_BINARY");
            var start = new ProcessStartInfo(binary ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            if (binary is null) start.ArgumentList.Add(launcherDll);
            foreach (var arg in new[] { "--game", chosenGame ?? cliGame, "--manifest", chosenFeed ?? feedUrl }) start.ArgumentList.Add(arg);
            if (accept) start.ArgumentList.Add("--yes");
            foreach (var arg in command.Split(' ')) start.ArgumentList.Add(arg);
            if (extraArgument is not null) start.ArgumentList.Add(extraArgument);
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
            start.Environment["HOME"] = launchHome;
            start.Environment["DOTNET_ROOT"] = Path.Combine(root, "no-runtime-installation");
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            start.Environment["PATH"] = fakeBin + ":" + Environment.GetEnvironmentVariable("PATH");
            start.Environment["CLONEDIVERS_TEST_URI"] = uriFile;
            start.Environment["CLONEDIVERS_TEST_CLIENT"] = clientFile;
            using var child = Process.Start(start)!;
            child.StandardInput.Close();
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { child.Kill(entireProcessTree: true); throw new Exception("CLI command timed out: " + command); }
            var output = await stdout + await stderr;
            Check(child.ExitCode == expectedExit, $"CLI {command} exits {expectedExit}: " + output.Trim());
            return output;
        }
        await Cli("install clone");
        await Cli("status");
        await Cli("launch");
        for (var i = 0; i < 30 && !File.Exists(uriFile); i++) await Task.Delay(100);
        Check(File.ReadAllText(uriFile) == "steam://rungameid/553850" && File.ReadAllText(clientFile) == "native", "real CLI launches the owning native Steam without using the wrong desktop handler");
        Libraries(nativeRoot); Libraries(snapRoot, cliLibrary); File.Delete(uriFile);
        await Cli("launch");
        for (var i = 0; i < 30 && !File.Exists(uriFile); i++) await Task.Delay(100);
        Check(File.ReadAllText(uriFile) == "steam://rungameid/553850" && File.ReadAllText(clientFile) == "snap", "real CLI dispatches a Snap-owned library through snap run steam");
        Libraries(snapRoot); Libraries(flatpakRoot, cliLibrary); File.Delete(uriFile);
        await Cli("launch");
        for (var i = 0; i < 30 && !File.Exists(uriFile); i++) await Task.Delay(100);
        Check(File.ReadAllText(uriFile) == "steam://rungameid/553850" && File.ReadAllText(clientFile) == "flatpak", "real CLI dispatches a Flatpak-owned library through flatpak run");
        Libraries(nativeRoot, cliLibrary);
        Check((await Cli("launch", 1)).Contains("More than one Steam installation"), "CLI explains ambiguous ownership instead of launching the desktop default");
        Libraries(nativeRoot); Libraries(flatpakRoot);
        Check((await Cli("launch", 1)).Contains("Cannot determine which Steam installation"), "CLI explains unknown ownership instead of bootstrapping another client");
        Libraries(nativeRoot, cliLibrary);
        await Cli("vanilla");
        await Cli("repair");
        Check(ModFiles.GetState(cliGame) == ModState.Off, "actual CLI repair preserves a vanilla selection");
        await Cli("install empire");
        Check(File.ReadAllBytes(Path.Combine(cliGame, "data", empire.Name)).SequenceEqual(empireBytes), "CLI Empire mode installs distinct Imperial content");
        await Cli("install clone");
        foreach (var path in ModFiles.ListPatchFiles(Path.Combine(cliGame, "data"))) File.Delete(path);
        await Cli("repair");
        Check(ModFiles.GetState(cliGame) == ModState.On, "repair restores active selection when every active patch was deleted");
        await Cli("vanilla");
        foreach (var path in ModFiles.ListPatchFiles(Path.Combine(cliGame, ModFiles.OffFolder))) File.Delete(path);
        await Cli("repair");
        Check(ModFiles.GetState(cliGame) == ModState.Off, "repair keeps vanilla selected even when every parked patch was deleted");

        var cliConfig = Path.Combine(root, "config", "clonedivers");
        File.WriteAllText(Path.Combine(cliConfig, LauncherUpdates.CacheName), JsonSerializer.Serialize(new LauncherUpdates.Cache(DateTimeOffset.UtcNow, "99.0.0", true)));
        var cliReceipt = Directory.EnumerateFiles(Path.Combine(cliConfig, "receipts")).Single();
        var saved = new { Format = 2, GameDir = cliGame, PackVersion = "fixture", GameBuild = "25480438", TargetActive = true,
            TextureProfile = "full", Options = new Dictionary<string, bool> { ["empire"] = false }, TargetFiles = new[] { clone, empty }, Actions = Array.Empty<object>() };
        var legacyPlan = Path.Combine(cliConfig, "apply-plan.json");
        var legacyBytes = JsonSerializer.SerializeToUtf8Bytes(saved); File.WriteAllBytes(legacyPlan, legacyBytes);
        var acf = SteamAcf.AcfPath(cliGame);
        File.WriteAllText(acf, "\"buildid\" \"25480438\"\n\"TargetBuildID\" \"99999999\"");
        Check((await Cli("finish", 1)).Contains("update queued"), "finish refuses a queued Steam update");
        Check(File.ReadAllBytes(legacyPlan).SequenceEqual(legacyBytes), "blocked recovery preserves the exact saved target");
        File.Delete(legacyPlan);
        Check((await Cli("repair", 1)).Contains("update queued"), "repair refuses a queued Steam update");
        File.WriteAllText(acf, "\"buildid\" \"25480438\"");

        File.WriteAllBytes(legacyPlan, legacyBytes);
        var otherGame = Path.Combine(root, "other-game");
        Directory.CreateDirectory(Path.Combine(otherGame, "data")); Directory.CreateDirectory(Path.Combine(otherGame, "bin"));
        File.WriteAllText(Path.Combine(otherGame, "bin", "helldivers2.exe"), "fixture");
        await Cli("install clone", chosenGame: otherGame);
        Check(File.ReadAllBytes(legacyPlan).SequenceEqual(legacyBytes), "installing into a second game folder cannot overwrite the first recovery target");
        await Cli("finish");
        Check(!File.Exists(legacyPlan) && ModFiles.GetState(cliGame) == ModState.On, "0.1.0 legacy recovery remains finishable after another installation");

        feed.Pack.Status = "broken";
        server.Add("manifest", JsonSerializer.SerializeToUtf8Bytes(feed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Check((await Cli("install clone", 1)).Contains("updates are paused"), "broken pack feed blocks installation");
        feed.Pack.Status = "ok";
        server.Add("manifest", JsonSerializer.SerializeToUtf8Bytes(feed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Check((await Cli("install clone", 1, accept: false)).Contains("--yes"), "noninteractive operations require explicit prompt acceptance");
        await Cli("install clone");
        server.Remove("manifest");
        Check((await Cli("install clone")).Contains("Using saved pack information"), "feed outage falls back to previously validated metadata");
        server.Add("manifest", JsonSerializer.SerializeToUtf8Bytes(feed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        // The default user journey is a menu on a terminal, so exercise it with a real PTY.
        var menuScript = Path.Combine(fakeBin, "launch-menu");
        File.WriteAllText(menuScript, """
            #!/bin/sh
            stty cols "${CLONEDIVERS_TEST_COLS:-100}" rows "${CLONEDIVERS_TEST_ROWS:-32}"
            if [ -n "$CLONEDIVERS_TEST_AUTO" ]; then set --; unset DISPLAY WAYLAND_DISPLAY; else set -- --tui; fi
            if [ -n "$CLONEDIVERS_TEST_BINARY" ]; then
              exec "$CLONEDIVERS_TEST_BINARY" "$@" --game "$CLONEDIVERS_TEST_GAME" --manifest "$CLONEDIVERS_TEST_FEED"
            else
              exec dotnet "$CLONEDIVERS_TEST_DLL" "$@" --game "$CLONEDIVERS_TEST_GAME" --manifest "$CLONEDIVERS_TEST_FEED"
            fi
            """);
        File.SetUnixFileMode(menuScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var menuStart = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-q", "-e", "-c", "exec \"$CLONEDIVERS_TEST_MENU_SCRIPT\"", "/dev/null" }) menuStart.ArgumentList.Add(arg);
        menuStart.Environment["CLONEDIVERS_TEST_MENU_SCRIPT"] = menuScript;
        menuStart.Environment["CLONEDIVERS_TEST_GAME"] = cliGame;
        menuStart.Environment["CLONEDIVERS_TEST_FEED"] = feedUrl;
        menuStart.Environment["CLONEDIVERS_TEST_DLL"] = launcherDll;
        menuStart.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
        menuStart.Environment["TERM"] = "dumb";
        using (var menu = Process.Start(menuStart)!)
        {
            var menuOutput = menu.StandardOutput.ReadToEndAsync(); var menuError = menu.StandardError.ReadToEndAsync();
            await menu.StandardInput.WriteAsync("4\n1\n2\n3\ns\n0\n"); await menu.StandardInput.FlushAsync();
            using var menuTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await menu.WaitForExitAsync(menuTimeout.Token); }
            catch (OperationCanceledException) { menu.Kill(entireProcessTree: true); throw new Exception("Interactive terminal menu timed out"); }
            var transcript = await menuOutput + await menuError;
            Check(menu.ExitCode == 0, "real terminal menu opens, edits options and exits" + (menu.ExitCode == 0 ? "" : ": " + transcript));
        }
        using var configured = JsonDocument.Parse(File.ReadAllText(Path.Combine(cliConfig, "config.json")));
        Check(!configured.RootElement.GetProperty("Options").GetProperty("droids").GetBoolean() &&
            configured.RootElement.GetProperty("Options").GetProperty("covenant").GetBoolean() &&
            configured.RootElement.GetProperty("TextureProfile").GetString() == "lighter", "interactive options persist droids, Covenant and potato profile choices");
        Check(File.ReadAllBytes(Path.Combine(cliGame, "data", clone.Name)).SequenceEqual(cloneBytes), "editing menu options leaves installed pack content unchanged until install/update");

        async Task<string> Terminal(string term, string chosenGame, (string text, int delay)[] input, bool automatic = false, string? chosenFeed = null, bool cancelTransfer = false, bool small = false)
        {
            var start = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-q", "-e", "-c", "exec \"$CLONEDIVERS_TEST_MENU_SCRIPT\"", "/dev/null" }) start.ArgumentList.Add(arg);
            start.Environment["CLONEDIVERS_TEST_MENU_SCRIPT"] = menuScript;
            start.Environment["CLONEDIVERS_TEST_GAME"] = chosenGame;
            start.Environment["CLONEDIVERS_TEST_FEED"] = chosenFeed ?? feedUrl;
            start.Environment["CLONEDIVERS_TEST_AUTO"] = automatic ? "1" : "";
            start.Environment["CLONEDIVERS_TEST_DLL"] = launcherDll;
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
            start.Environment["TERM"] = term; start.Environment["NO_COLOR"] = "1";
            if (small) { start.Environment["CLONEDIVERS_TEST_COLS"] = "40"; start.Environment["CLONEDIVERS_TEST_ROWS"] = "16"; }
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
            try
            {
                foreach (var step in input) { await child.StandardInput.WriteAsync(step.text); await child.StandardInput.FlushAsync(); await Task.Delay(step.delay); }
                if (cancelTransfer)
                {
                    await server.SlowStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Task.Delay(200);
                    await child.StandardInput.WriteAsync("\u0003"); await child.StandardInput.FlushAsync(); await Task.Delay(700);
                    await child.StandardInput.WriteAsync("0\n"); await child.StandardInput.FlushAsync();
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await child.WaitForExitAsync(timeout.Token);
            }
            catch { if (!child.HasExited) child.Kill(true); throw; }
            var transcript = await output + await errors;
            Check(child.ExitCode == 0, "interactive terminal exits cleanly" + (child.ExitCode == 0 ? "" : ": " + transcript));
            return transcript;
        }
        var beforeDiscard = File.ReadAllBytes(Path.Combine(cliConfig, "config.json"));
        var rich = await Terminal("xterm-256color", cliGame, new[] { ("\u001b[B\u001b[B\u001b[B\n", 300), (" ", 200), ("\u001b", 300), ("\u001b[F\n", 0) });
        Check(rich.Contains("PACK OPTIONS") && rich.Contains("Space or Enter") && rich.Contains("Option edits discarded"), "arrow keys, Space, Escape and End navigate the live TUI and discard option edits");
        Check(File.ReadAllBytes(Path.Combine(cliConfig, "config.json")).SequenceEqual(beforeDiscard), "discarding TUI edits leaves saved choices byte-identical");
        Check(!System.Text.RegularExpressions.Regex.IsMatch(rich, "\u001b\\[[0-9;]*m"), "NO_COLOR keeps keyboard navigation without emitting color sequences");
        var updateFlow = await Terminal("dumb", cliGame, new[] { ("u\n0\n0\n", 0) });
        Check(updateFlow.Contains("LAUNCHER UPDATES") && updateFlow.Contains("Launcher v99.0.0 available") && updateFlow.Contains("/tag/linux-v99.0.0"), "terminal update notices show the newer Linux release and manual upgrade instructions");
        var paged = await Terminal("xterm-256color", cliGame, new[] { ("9\n1\n", 500), ("n\n", 200), ("p\n", 200), ("0\n0\n", 0) }, small: true);
        Check(paged.Contains("Lines 1–3") && paged.Contains("Lines 4–6") && paged.Contains("Previous page"), "recording results can page forward and back in a small real terminal");
        var folderFlow = await Terminal("dumb", Path.Combine(root, "not-installed"), new[] { ("f\n" + cliGame + "\n0\n", 0) });
        Check(folderFlow.Contains("Not found") && folderFlow.Contains("Game folder saved"), "TUI starts without game detection and can select a folder without restarting");
        using (var selectedFolder = JsonDocument.Parse(File.ReadAllText(Path.Combine(cliConfig, "config.json"))))
            Check(selectedFolder.RootElement.GetProperty("GamePath").GetString() == cliGame, "TUI folder picker persists the confirmed game path");
        var transcriptDestination = Environment.GetEnvironmentVariable("CLONEDIVERS_TUI_TRANSCRIPT");
        if (!string.IsNullOrWhiteSpace(transcriptDestination)) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(transcriptDestination))!); File.WriteAllText(transcriptDestination, rich); }
        Check((await Terminal("dumb", cliGame, new[] { ("0\n", 0) }, automatic: true)).Contains("YOUR SQUAD LAUNCHER"), "a real headless terminal automatically opens the TUI without a mode flag");
        await Cli("status", extraArgument: "--terminal");

        var slowGame = Path.Combine(root, "Slow Library", "steamapps", "common", "Helldivers 2");
        Directory.CreateDirectory(Path.Combine(slowGame, "data")); Directory.CreateDirectory(Path.Combine(slowGame, "bin"));
        File.WriteAllText(Path.Combine(slowGame, "bin", "helldivers2.exe"), "fixture"); File.WriteAllText(SteamAcf.AcfPath(slowGame), "\"buildid\" \"25480438\"");
        var slowBytes = new byte[1024 * 1024]; Random.Shared.NextBytes(slowBytes);
        var slowFile = Entry("slow.patch_0", slowBytes, "slow-file"); server.MarkSlow("slow-file");
        var slowManifest = new Manifest { Format = 2, Pack = new PackManifest { Version = "slow-fixture", GameBuild = "25480438", Files = new() { slowFile } } };
        var slowFeed = server.Add("slow-manifest", JsonSerializer.SerializeToUtf8Bytes(slowManifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var cancelled = await Terminal("xterm-256color", slowGame, new[] { ("1\ny\n", 0) }, chosenFeed: slowFeed, cancelTransfer: true);
        Check(cancelled.Contains("Cancelled. Partial downloads") && cancelled.Contains("YOUR SQUAD LAUNCHER"), "Ctrl+C during a real TUI download returns to a usable dashboard");
        var retained = Path.Combine(slowGame, Pack.DownloadFolder, slowFile.Sha256 + Pack.PartialSuffix);
        Check(File.Exists(retained) && new FileInfo(retained).Length > 0 && new FileInfo(retained).Length < slowFile.Size && ModFiles.GetState(slowGame) == ModState.NoModFiles, "cancelled TUI transfer keeps partial bytes and leaves installed files untouched");
        await Cli("install clone", chosenGame: slowGame, chosenFeed: slowFeed);
        Check(server.ResumedSlow && File.ReadAllBytes(Path.Combine(slowGame, "data", slowFile.Name)).SequenceEqual(slowBytes), "a cancelled TUI download resumes by HTTP Range and installs verified bytes");

        await Cli("record-setup");
        var wrapper = Path.Combine(cliGame, "mods_diagnostics", "steam-capture.sh");
        Check(File.Exists(wrapper) && LinuxRecording.ArmedSession(cliGame) is null, "recording setup creates a Steam wrapper without enabling recording");
        var fakeProtonDir = Path.Combine(root, "Fake Proton"); Directory.CreateDirectory(fakeProtonDir);
        var fakeProton = Path.Combine(fakeProtonDir, "proton");
        File.WriteAllText(Path.Combine(fakeProtonDir, "version"), "Proton fixture version 9\n");
        var environmentCapture = Path.Combine(root, "captured-environment"); var argumentCapture = Path.Combine(root, "captured-arguments");
        File.WriteAllText(fakeProton, "#!/bin/sh\nprintf '%s\\n' \"${MANGOHUD-unset}\" \"${PROTON_LOG-unset}\" \"${MANGOHUD_CONFIG-unset}\" > \"$TEST_ENV_FILE\"\nprintf '%s\\0' \"$@\" > \"$TEST_ARG_FILE\"\nif [ \"${MANGOHUD-}\" = 1 ] && [ \"${TEST_NO_CSV:-0}\" != 1 ]; then\n  output=$(sed -n 's/^output_folder=//p' \"$MANGOHUD_CONFIGFILE\")\n  printf 'fps,frametime\\n60,16.7\\n' > \"$output/fixture.csv\"\n  printf 'Proton fixture log\\nAuthorization: Bearer fixture-secret\\n' > \"$PROTON_LOG_DIR/steam-553850.log\"\nfi\nexit \"${TEST_EXIT_CODE:-0}\"\n");
        File.SetUnixFileMode(fakeProton, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var literalArgument = "spaces 'quotes' ; $(touch should-never-execute)";
        async Task RunWrapper(int expected, bool produceMetrics = true)
        {
            var start = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { wrapper, fakeProton, "waitforexitandrun", literalArgument }) start.ArgumentList.Add(arg);
            start.Environment["MANGOHUD"] = "existing"; start.Environment["PROTON_LOG"] = "existing";
            start.Environment["MANGOHUD_CONFIG"] = "existing-config";
            start.Environment["TEST_ENV_FILE"] = environmentCapture; start.Environment["TEST_ARG_FILE"] = argumentCapture;
            start.Environment["TEST_EXIT_CODE"] = expected.ToString();
            start.Environment["TEST_NO_CSV"] = produceMetrics ? "0" : "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await child.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { child.Kill(true); throw new Exception("Steam capture wrapper timed out"); }
            Check(child.ExitCode == expected, $"Steam wrapper preserves command exit code {expected}: " + await output + await error);
            Check(File.ReadAllText(argumentCapture) == "waitforexitandrun\0" + literalArgument + "\0", "Steam wrapper forwards argument boundaries and shell metacharacters literally");
        }
        await RunWrapper(0);
        Check(File.ReadAllLines(environmentCapture).SequenceEqual(new[] { "existing", "existing", "existing-config" }), "unarmed Steam wrapper leaves the user's environment unchanged");
        await Cli("record");
        var armed = LinuxRecording.ArmedSession(cliGame)!;
        await Cli("record", 1);
        Check(LinuxRecording.ArmedSession(cliGame) == armed, "duplicate recording request preserves the already-armed session");
        await RunWrapper(7);
        Check(File.ReadAllLines(environmentCapture).SequenceEqual(new[] { "1", "1", "unset" }), "armed launch enables MangoHud and Proton logs only in the child command");
        Check(LinuxRecording.ArmedSession(cliGame) is null && File.Exists(Path.Combine(armed, "mangohud", "fixture.csv")), "recording is consumed once and creates session-local metrics");
        Check(File.ReadAllText(Path.Combine(armed, "proton-version.txt")).Contains("version 9") && File.Exists(Path.Combine(armed, "selection-at-launch.json")), "recording captures actual Proton tool version and pack selection at launch");
        Check((await Cli("record-status")).Contains("exit code: 7"), "record-status exposes the recorded command's actual outcome");
        await RunWrapper(0);
        Check(File.ReadAllLines(environmentCapture).SequenceEqual(new[] { "existing", "existing", "existing-config" }), "following launch returns to ordinary environment settings automatically");
        var reportPath = Path.Combine(root, "support report from CLI.zip");
        await Cli("report", extraArgument: reportPath);
        using (var zip = ZipFile.OpenRead(reportPath))
        {
            Check(zip.GetEntry("session/mangohud-1.csv") is not null && zip.GetEntry("session/proton-version.txt") is not null, "CLI support report includes captured performance data and runtime version");
            using var reader = new StreamReader(zip.GetEntry("session/proton.log")!.Open());
            Check(!reader.ReadToEnd().Contains("fixture-secret"), "captured Proton log credentials are redacted in the support ZIP");
        }
        await Cli("report", chosenGame: Path.Combine(root, "not-installed"), extraArgument: Path.Combine(root, "undetected.zip"));
        await Cli("record"); await Cli("record-cancel");
        Check(LinuxRecording.ArmedSession(cliGame) is null && Directory.Exists(armed), "cancelling an armed recording retains previous session data");
        // Unsafe session directories must not pull unrelated files into a support report.
        Directory.Delete(Path.Combine(armed, "proton"), true);
        Directory.CreateSymbolicLink(Path.Combine(armed, "proton"), supportHome);
        var unsafeReport = Path.Combine(root, "unsafe-session.zip");
        await Cli("report", extraArgument: unsafeReport);
        using (var zip = ZipFile.OpenRead(unsafeReport)) Check(zip.GetEntry("collection-note.txt") is not null && zip.GetEntry("session/proton.log") is null, "support collection skips redirected log folders and still saves the report");
        await Cli("record");
        await RunWrapper(0, produceMetrics: false);
        Check((await Cli("record-status")).Contains("No MangoHud CSV was produced"), "missing MangoHud layer is reported without claiming that performance capture succeeded");

        var guiGame = Path.Combine(root, "GUI Library", "steamapps", "common", "Helldivers 2");
        Directory.CreateDirectory(Path.Combine(guiGame, "data")); Directory.CreateDirectory(Path.Combine(guiGame, "bin"));
        File.WriteAllText(Path.Combine(guiGame, "bin", "helldivers2.exe"), "fixture");
        File.WriteAllText(Path.Combine(guiGame, ".clonedivers-ui-fixture"), "explicit synthetic UI test");
        File.WriteAllText(Path.Combine(guiGame, "data", "base-archive"), "untouched");
        File.WriteAllText(SteamAcf.AcfPath(guiGame), "\"buildid\" \"25480438\"");
        Libraries(snapRoot, Path.GetFullPath(Path.Combine(guiGame, "..", "..", ".."))); File.Delete(uriFile);
        var screenshot = Environment.GetEnvironmentVariable("CLONEDIVERS_GUI_SCREENSHOT") ?? Path.Combine(root, "gui.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
        var guiStart = new ProcessStartInfo("xvfb-run") { RedirectStandardOutput = true, RedirectStandardError = true };
        guiStart.ArgumentList.Add("-a");
        var guiBinary = Environment.GetEnvironmentVariable("CLONEDIVERS_TEST_BINARY");
        guiStart.ArgumentList.Add(guiBinary ?? "dotnet");
        if (guiBinary is null) guiStart.ArgumentList.Add(launcherDll);
        foreach (var arg in new[] { "--gui", "--game", guiGame, "--manifest", feedUrl, "--ui-smoke", screenshot }) guiStart.ArgumentList.Add(arg);
        guiStart.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
        guiStart.Environment["HOME"] = launchHome;
        guiStart.Environment["XDG_DATA_HOME"] = Path.Combine(root, "desktop-data");
        guiStart.Environment["PATH"] = fakeBin + ":" + Environment.GetEnvironmentVariable("PATH");
        guiStart.Environment["CLONEDIVERS_TEST_URI"] = uriFile;
        guiStart.Environment["CLONEDIVERS_TEST_CLIENT"] = clientFile;
        var updateLinkFile = Path.Combine(root, "update-link"); guiStart.Environment["CLONEDIVERS_TEST_UPDATE_LINK"] = updateLinkFile;
        using (var gui = Process.Start(guiStart)!)
        {
            var stdout = gui.StandardOutput.ReadToEndAsync(); var stderr = gui.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await gui.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { gui.Kill(true); throw new Exception("Native GUI smoke test timed out"); }
            Check(gui.ExitCode == 0, "native graphical launcher buttons, install/switch/repair/launch and shortcut: " + await stdout + await stderr);
        }
        Check(File.Exists(screenshot) && new FileInfo(screenshot).Length > 10000, "graphical launcher renders a real Linux window screenshot");
        Check(File.ReadAllText(updateLinkFile) == "https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v99.0.0", "desktop update button opens only the selected Linux release page");
        Check(File.ReadAllText(uriFile) == "steam://rungameid/553850" && File.ReadAllText(clientFile) == "snap", "native GUI Launch button uses the game's Snap Steam client despite a different default handler");
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
    readonly HashSet<string> slow = new();
    readonly CancellationTokenSource stop = new();
    readonly Task serving;
    public bool SawRange { get; private set; }
    public int Requests { get; private set; }
    public bool ResumedSlow { get; private set; }
    public TaskCompletionSource<bool> SlowStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public FixtureServer() { listener.Start(); serving = Serve(); }
    public string Add(string route, byte[] bytes)
    {
        content["/" + route] = bytes;
        return $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{route}";
    }
    public void Remove(string route) => content.Remove("/" + route);
    public void MarkSlow(string route) => slow.Add("/" + route);
    async Task Serve()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                try
                {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var first = await reader.ReadLineAsync() ?? "";
                var offset = 0; string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) { offset = int.Parse(line[13..].TrimEnd('-')); SawRange = true; }
                Requests++;
                var route = first.Split(' ')[1].Split('?')[0];
                var found = content.TryGetValue(route, out var bytes);
                bytes ??= Encoding.UTF8.GetBytes("fixture feed unavailable");
                var headers = $"HTTP/1.1 {(!found ? "404 Not Found" : offset > 0 ? "206 Partial Content" : "200 OK")}\r\nContent-Length: {bytes.Length - offset}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n";
                if (offset > 0) headers += $"Content-Range: bytes {offset}-{bytes.Length - 1}/{bytes.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"));
                if (slow.Contains(route))
                {
                    if (offset > 0) ResumedSlow = true;
                    SlowStarted.TrySetResult(true);
                    for (var at = offset; at < bytes.Length; at += 16384)
                    { await stream.WriteAsync(bytes.AsMemory(at, Math.Min(16384, bytes.Length - at)), stop.Token); await Task.Delay(35, stop.Token); }
                }
                else await stream.WriteAsync(bytes.AsMemory(offset));
                }
                catch (IOException) { /* A cancelled client may close during its transfer. */ }
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); serving.GetAwaiter().GetResult(); stop.Dispose(); }
}
