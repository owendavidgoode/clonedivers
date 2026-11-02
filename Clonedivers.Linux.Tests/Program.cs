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
        var open = Path.Combine(fakeBin, "xdg-open");
        File.WriteAllText(open, "#!/bin/sh\nprintf '%s' \"$1\" > \"$CLONEDIVERS_TEST_URI\"\n");
        File.SetUnixFileMode(open, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var uriFile = Path.Combine(root, "uri");
        async Task<string> Cli(string command, int expectedExit = 0, string? chosenGame = null, bool accept = true, string? extraArgument = null)
        {
            var binary = Environment.GetEnvironmentVariable("CLONEDIVERS_TEST_BINARY");
            var start = new ProcessStartInfo(binary ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            if (binary is null) start.ArgumentList.Add(launcherDll);
            foreach (var arg in new[] { "--game", chosenGame ?? cliGame, "--manifest", feedUrl }) start.ArgumentList.Add(arg);
            if (accept) start.ArgumentList.Add("--yes");
            foreach (var arg in command.Split(' ')) start.ArgumentList.Add(arg);
            if (extraArgument is not null) start.ArgumentList.Add(extraArgument);
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
            start.Environment["DOTNET_ROOT"] = Path.Combine(root, "no-runtime-installation");
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            start.Environment["PATH"] = fakeBin + ":" + Environment.GetEnvironmentVariable("PATH");
            start.Environment["CLONEDIVERS_TEST_URI"] = uriFile;
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
        Check(File.ReadAllText(uriFile) == "steam://rungameid/553850", "Linux launch hands the correct Steam URI to the desktop handler");
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
        File.WriteAllText(menuScript, "#!/bin/sh\nif [ -n \"$CLONEDIVERS_TEST_BINARY\" ]; then exec \"$CLONEDIVERS_TEST_BINARY\" --game \"$CLONEDIVERS_TEST_GAME\" --manifest \"$CLONEDIVERS_TEST_FEED\"; else exec dotnet \"$CLONEDIVERS_TEST_DLL\" --game \"$CLONEDIVERS_TEST_GAME\" --manifest \"$CLONEDIVERS_TEST_FEED\"; fi\n");
        File.SetUnixFileMode(menuScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var menuStart = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-q", "-e", "-c", "exec \"$CLONEDIVERS_TEST_MENU_SCRIPT\"", "/dev/null" }) menuStart.ArgumentList.Add(arg);
        menuStart.Environment["CLONEDIVERS_TEST_MENU_SCRIPT"] = menuScript;
        menuStart.Environment["CLONEDIVERS_TEST_GAME"] = cliGame;
        menuStart.Environment["CLONEDIVERS_TEST_FEED"] = feedUrl;
        menuStart.Environment["CLONEDIVERS_TEST_DLL"] = launcherDll;
        menuStart.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
        using (var menu = Process.Start(menuStart)!)
        {
            var menuOutput = menu.StandardOutput.ReadToEndAsync(); var menuError = menu.StandardError.ReadToEndAsync();
            await menu.StandardInput.WriteAsync("4\ny\ny\ny\n0\n"); await menu.StandardInput.FlushAsync();
            using var menuTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await menu.WaitForExitAsync(menuTimeout.Token); }
            catch (OperationCanceledException) { menu.Kill(entireProcessTree: true); throw new Exception("Interactive terminal menu timed out"); }
            Check(menu.ExitCode == 0, "real terminal menu opens, edits options and exits: " + await menuOutput + await menuError);
        }
        using var configured = JsonDocument.Parse(File.ReadAllText(Path.Combine(cliConfig, "config.json")));
        Check(!configured.RootElement.GetProperty("Options").GetProperty("droids").GetBoolean() &&
            configured.RootElement.GetProperty("Options").GetProperty("covenant").GetBoolean() &&
            configured.RootElement.GetProperty("TextureProfile").GetString() == "lighter", "interactive options persist droids, Covenant and potato profile choices");
        Check(File.ReadAllBytes(Path.Combine(cliGame, "data", clone.Name)).SequenceEqual(cloneBytes), "editing menu options leaves installed pack content unchanged until install/update");

        await Cli("record-setup");
        var wrapper = Path.Combine(cliGame, "mods_diagnostics", "steam-capture.sh");
        Check(File.Exists(wrapper) && LinuxRecording.ArmedSession(cliGame) is null, "recording setup creates a Steam wrapper without enabling recording");
        var fakeProtonDir = Path.Combine(root, "Fake Proton"); Directory.CreateDirectory(fakeProtonDir);
        var fakeProton = Path.Combine(fakeProtonDir, "proton");
        File.WriteAllText(Path.Combine(fakeProtonDir, "version"), "Proton fixture version 9\n");
        var environmentCapture = Path.Combine(root, "captured-environment"); var argumentCapture = Path.Combine(root, "captured-arguments");
        File.WriteAllText(fakeProton, "#!/bin/sh\nprintf '%s\\n' \"${MANGOHUD-unset}\" \"${PROTON_LOG-unset}\" \"${MANGOHUD_CONFIG-unset}\" > \"$TEST_ENV_FILE\"\nprintf '%s\\0' \"$@\" > \"$TEST_ARG_FILE\"\nif [ \"${MANGOHUD-}\" = 1 ]; then\n  output=$(sed -n 's/^output_folder=//p' \"$MANGOHUD_CONFIGFILE\")\n  printf 'fps,frametime\\n60,16.7\\n' > \"$output/fixture.csv\"\n  printf 'Proton fixture log\\nAuthorization: Bearer fixture-secret\\n' > \"$PROTON_LOG_DIR/steam-553850.log\"\nfi\nexit \"${TEST_EXIT_CODE:-0}\"\n");
        File.SetUnixFileMode(fakeProton, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var literalArgument = "spaces 'quotes' ; $(touch should-never-execute)";
        async Task RunWrapper(int expected)
        {
            var start = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { wrapper, fakeProton, "waitforexitandrun", literalArgument }) start.ArgumentList.Add(arg);
            start.Environment["MANGOHUD"] = "existing"; start.Environment["PROTON_LOG"] = "existing";
            start.Environment["MANGOHUD_CONFIG"] = "existing-config";
            start.Environment["TEST_ENV_FILE"] = environmentCapture; start.Environment["TEST_ARG_FILE"] = argumentCapture;
            start.Environment["TEST_EXIT_CODE"] = expected.ToString();
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
    public void Remove(string route) => content.Remove("/" + route);
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
                var found = content.TryGetValue(first.Split(' ')[1].Split('?')[0], out var bytes);
                bytes ??= Encoding.UTF8.GetBytes("fixture feed unavailable");
                var headers = $"HTTP/1.1 {(!found ? "404 Not Found" : offset > 0 ? "206 Partial Content" : "200 OK")}\r\nContent-Length: {bytes.Length - offset}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n";
                if (offset > 0) headers += $"Content-Range: bytes {offset}-{bytes.Length - 1}/{bytes.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"));
                await stream.WriteAsync(bytes.AsMemory(offset));
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); serving.GetAwaiter().GetResult(); stop.Dispose(); }
}
