using System.Diagnostics;

namespace Clonedivers;

internal static class LinuxProgram
{
    static Settings settings = new();
    static string? gameDir, source;
    static Manifest? manifest;
    static bool yes;
    static string PlanPath => LinuxInstaller.PlanPathFor(gameDir!);
    static readonly CancellationTokenSource cancellation = new();

    public static async Task<int> RunAsync(string[] args)
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        try
        {
            var words = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help": case "-h": Help(); return 0;
                    case "--version": Console.WriteLine($"Clonedivers Linux {AppInfo.Version}"); return 0;
                    case "--yes": yes = true; break;
                    case "--terminal": break;
                    case "--game": gameDir = Value(args, ref i); break;
                    case "--manifest": source = Value(args, ref i); break;
                    default:
                        if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option: {args[i]}");
                        words.Add(args[i]); break;
                }
            }
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run this launcher on Linux. Use Clonedivers.exe on Windows.");
            settings = Settings.Load();
            gameDir = GameLocator.NormalizeGameDir(gameDir ?? settings.GamePath) ?? (gameDir is null ? GameLocator.AutoDetect() : null);
            if (gameDir is null && words.FirstOrDefault() != "report")
            {
                if (words.Count > 0 || Console.IsInputRedirected) throw new DirectoryNotFoundException("Game not found. Pass --game '/path/to/steamapps/common/Helldivers 2'.");
                Console.Write("Helldivers 2 folder (containing data and bin): ");
                gameDir = GameLocator.NormalizeGameDir(Console.ReadLine());
                if (gameDir is null) throw new DirectoryNotFoundException("That folder does not contain data and bin/helldivers2.exe.");
            }
            if (gameDir is not null) settings.GamePath = gameDir;
            if (words.Count > 0)
            {
                if (words.Count > (words[0] is "install" or "report" ? 2 : 1)) throw new ArgumentException("Too many command arguments. Use --help.");
                await Command(words[0], words.ElementAtOrDefault(1));
            }
            else
            {
                if (Console.IsInputRedirected) throw new ArgumentException("Specify a command when input is redirected. Use --help.");
                await Menu();
            }
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Stopped. Downloads are kept; retry the same command to resume."); return 130; }
        catch (Exception ex) { LinuxSupport.Record("error", ex.GetType().Name + ": " + ex.Message); Console.Error.WriteLine(ex.Message); return 1; }
        finally { cancellation.Dispose(); }
    }

    static string Value(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
    static void Help() => Console.WriteLine($"""
        Clonedivers Linux {AppInfo.Version} — terminal launcher
        Usage: ./clonedivers [--game '/path/to/Helldivers 2'] [--yes] [command]
        No command: open the desktop window. --terminal opens the optional terminal menu.
          status                  Show local installation state
          install [clone|empire]  Download/update and enable the chosen mode
          vanilla                 Park mods for ordinary Helldivers
          repair                  Hash-check and repair the installed selection
          finish                  Recover an interrupted update
          launch                  Ask Steam to launch Helldivers 2
          report [file.zip]       Save local support report, even if game detection fails
          record-setup            Print the one-time Steam recording launch option
          record                  Arm MangoHud + Proton logs for the next Steam launch
          record-cancel           Cancel the pending recording
          record-status           Show recording location and last capture result
        --yes accepts download and untested-game-build prompts.
        --manifest URL selects a separate test feed/cache for this run.
        Ctrl+C keeps downloads for resuming. Windows performance sharing is unavailable.
        """);

    static bool Confirm(string message)
    {
        Console.WriteLine(message);
        if (yes) return true;
        if (Console.IsInputRedirected) throw new InvalidOperationException("Use --yes to accept this operation with redirected input.");
        Console.Write("Continue? [y/N] ");
        return Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
    }

    static async Task Menu()
    {
        while (!cancellation.IsCancellationRequested)
        {
            Status();
            Console.WriteLine("\n1  Install/update Clonedivers\n2  Install/update EmpireDivers\n3  Vanilla Helldivers\n4  Options\n5  Check and repair\n6  Finish interrupted update\n7  Launch through Steam\n8  Save support report\n9  Session recording\n0  Quit");
            Console.Write("Choose: ");
            var choice = Console.ReadLine()?.Trim();
            if (choice is null or "0") return;
            try
            {
                switch (choice)
                {
                    case "1": await Command("install", "clone"); break;
                    case "2": await Command("install", "empire"); break;
                    case "3": await Command("vanilla", null); break;
                    case "4": await Options(); break;
                    case "5": await Command("repair", null); break;
                    case "6": await Command("finish", null); break;
                    case "7": await Command("launch", null); break;
                    case "8": await Command("report", null); break;
                    case "9": await RecordingMenu(); break;
                    default: Console.WriteLine("Choose a number from the menu."); break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { LinuxSupport.Record("error", ex.GetType().Name + ": " + ex.Message); Console.Error.WriteLine(ex.Message); }
            Console.WriteLine();
        }
        cancellation.Token.ThrowIfCancellationRequested();
    }

    static void Status()
    {
        Console.WriteLine($"\nClonedivers Linux {AppInfo.Version}\nGame: {gameDir}");
        Console.WriteLine($"Mods: {ModFiles.GetState(gameDir)} | Game: {(Game.IsRunning() ? "running" : "closed")}");
        var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!);
        Console.WriteLine(receipt is not null && receipt.MatchesLayout(gameDir!)
            ? $"Installed pack: {receipt.PackVersion} | textures: {receipt.TextureProfile} | universe: {(receipt.Options.GetValueOrDefault("empire") ? "EmpireDivers" : "Clonedivers")}"
            : "Installed selection is unrecorded or changed; use Check and repair.");
        if (Pack.HasPendingUpdate(gameDir, PlanPath)) Console.WriteLine("An interrupted update needs Finish before switching modes or launching.");
    }

    static async Task<PackManifest> Feed()
    {
        if (manifest?.Pack is { } existing) return existing;
        var cache = ManifestStore.CachePath(source, PlatformPaths.ConfigDirectory);
        try
        {
            Console.WriteLine("Checking pack feed…");
            var fresh = await ManifestStore.FetchAsync(source, cancellation.Token);
            ManifestStore.ValidateForUse(fresh);
            manifest = ManifestStore.PreserveProfiles(ManifestStore.Load(cache), fresh);
            ManifestStore.Save(cache, manifest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            manifest = ManifestStore.Load(cache);
            if (manifest is null) throw new IOException("Cannot load pack information. Connect to the internet and retry.", ex);
            Console.WriteLine("Using saved pack information; the online feed is unavailable.");
        }
        return manifest.Pack!;
    }

    static async Task Options()
    {
        var pack = await Feed();
        Console.WriteLine("Options apply on your next Install/update; they do not change installed files yet.");
        foreach (var option in PackExtras.Visible(pack))
        {
            var current = settings.Options.GetValueOrDefault(option.Id, option.Default);
            Console.Write($"{option.Name}: {(current ? "ON" : "OFF")}. Toggle? [y/N] ");
            if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true) settings.Options[option.Id] = !current;
        }
        var potato = PackProfiles.Potato(pack);
        if (potato is not null)
        {
            Console.Write($"{PackProfiles.PotatoName} ON? [y/N] ");
            settings.TextureProfile = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true ? potato.Id : "full";
        }
        settings.Save();
    }

    static async Task Command(string command, string? mode)
    {
        if (command == "report")
        {
            var report = LinuxSupport.SaveReport(gameDir, settings, mode);
            Console.WriteLine($"Support report saved: {report}\nSaved locally; review the ZIP before sharing. No data was uploaded."); return;
        }
        if (command.StartsWith("record", StringComparison.Ordinal)) { RecordingCommand(command); return; }
        if (command == "status") { Status(); return; }
        if (command is not ("install" or "vanilla" or "repair" or "finish" or "launch")) throw new ArgumentException($"Unknown command: {command}");
        LinuxInstaller.RequireClosed();
        using var gameLock = LinuxInstaller.Lock(gameDir!);
        if (command == "finish")
        {
            var saved = RecoveryTarget.Read(gameDir!, PlanPath) ?? throw new InvalidOperationException("No valid saved update target. Use repair to rebuild an unrecorded staged installation.");
            await Apply(saved); return;
        }
        if (Pack.HasPendingUpdate(gameDir, PlanPath) &&
            (RecoveryTarget.Read(gameDir!, PlanPath) is not null || command is not ("repair" or "install")))
            throw new InvalidOperationException("An interrupted update needs 'finish' before switching, repairing or launching. For orphaned staged files, use repair or install.");
        if (command == "vanilla")
        {
            LinuxInstaller.RequireClosed();
            var count = ModFiles.MoveAllPatchFiles(Path.Combine(gameDir!, ModFiles.DataFolder), Path.Combine(gameDir!, ModFiles.OffFolder));
            var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!);
            if (receipt is not null && receipt.MatchesLayout(gameDir!))
            {
                var parked = receipt.ToApplyResult(); parked.TargetActive = false;
                InstallReceipt.Write(Settings.ReceiptPathFor(gameDir!), gameDir!, receipt.Files.Select(f => f.File).ToList(), parked);
            }
            Console.WriteLine($"Vanilla selected. Parked {count} mod files.");
            settings.Save(); RefreshRecordingSelection(); return;
        }
        if (command == "launch")
        {
            if (ModFiles.GetState(gameDir) == ModState.On)
            {
                var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!);
                if (receipt is null || !receipt.MatchesLayout(gameDir!)) throw new InvalidOperationException("Run repair or install before launching this unrecorded/changed mod selection.");
                CheckBuild(receipt.GameBuild);
            }
            // Desktop URI handlers work with native and Flatpak Steam, without spawning Wine ourselves.
            FpvDefaults.EnsureForInstalledPack(gameDir);
            Game.LaunchViaSteam();
            LinuxSupport.Record("launch", "Steam launch requested. " + ModFiles.GetState(gameDir));
            Console.WriteLine("Launch request sent to Steam."); return;
        }
        if (command == "repair")
        {
            var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!);
            if (receipt is null) throw new InvalidOperationException("No installed selection recorded. Use install clone or install empire to verify and repair existing files.");
            await Apply(LinuxInstaller.RepairTarget(gameDir!, receipt)); return;
        }
        var selected = mode switch
        {
            null or "clone" or "clonedivers" => LauncherMode.Clonedivers,
            "empire" => LauncherMode.EmpireDivers,
            _ => throw new ArgumentException("Choose install clone or install empire.")
        };
        var pack = await Feed();
        if (pack.IsBroken) throw new InvalidOperationException($"Pack updates are paused: {pack.StatusNotes}");
        var options = LauncherModes.OptionsFor(settings, pack, selected);
        var profile = settings.ProfileFor(pack);
        await Apply(new RecoveryTarget { PackVersion = pack.Version, GameBuild = pack.GameBuild, Options = options,
            TextureProfile = profile, TargetActive = true, Files = Pack.EffectiveFiles(pack, id => options.GetValueOrDefault(id), profile) });
    }

    static void CheckBuild(string? tested)
    {
        var actual = SteamAcf.Read(gameDir);
        LinuxInstaller.RequireNoSteamUpdate(gameDir!);
        if (tested is null || actual?.BuildId != tested)
            if (!Confirm($"Game build {actual?.BuildId ?? "unknown"}; pack tested against {tested ?? "unknown"}. Compatibility with this build is unverified."))
                throw new OperationCanceledException();
    }

    static async Task Apply(RecoveryTarget target)
    {
        CheckBuild(target.GameBuild);
        var last = Stopwatch.StartNew();
        var progress = new SyncProgress<(long done, long total, int file, int files)>(p =>
        {
            if (last.ElapsedMilliseconds < 1000 && p.done < p.total) return;
            Console.WriteLine($"Download {p.file}/{p.files}: {Pack.FormatBytes(p.done)} / {Pack.FormatBytes(p.total)}"); last.Restart();
        });
        var result = await LinuxInstaller.ApplyAsync(gameDir!, target, PlanPath, Settings.ReceiptPathFor(gameDir!), plan =>
            Confirm($"Pack {target.PackVersion}, textures {target.TextureProfile}. Download {Pack.FormatBytes(plan.BytesToDownload)}, copy {Pack.FormatBytes(plan.BytesToCopy)}, park {plan.ParkCount} files. Replaced files are kept in mods_old."), progress, cancellation.Token);
        if (result.Interrupted) throw new IOException($"Update interrupted: {result.Reason} {string.Join(", ", result.Failed)}. Close the game and run finish.");
        settings.RecordInstall(result);
        settings.Save();
        RefreshRecordingSelection();
        LinuxSupport.Record("installed", $"Pack {result.PackVersion}, texture profile {result.TextureProfile}.");
        Console.WriteLine($"Pack {result.PackVersion} ready. Use launch to start through Steam.");
    }

    static void RefreshRecordingSelection()
    {
        try { LinuxRecording.RefreshSelection(gameDir!, settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { LinuxSupport.Record("error", "Recording metadata could not be refreshed: " + ex.Message); }
    }

    static async Task RecordingMenu()
    {
        Console.WriteLine("1  One-time Steam setup\n2  Record next session\n3  Cancel pending recording\n4  Show capture result\n0  Back");
        Console.Write("Choose: ");
        var command = Console.ReadLine()?.Trim() switch
        {
            "1" => "record-setup", "2" => "record", "3" => "record-cancel", "4" => "record-status", _ => null
        };
        if (command is not null) await Command(command, null);
    }

    static void RecordingCommand(string command)
    {
        if (command == "record-status")
        {
            Console.WriteLine(LinuxRecording.ArmedSession(gameDir!) is { } armed ? "Next recording is armed: " + armed : "No recording is armed.");
            var last = LinuxRecording.LatestSession(gameDir!);
            Console.WriteLine(last is null ? "No recordings yet." : "Latest session: " + last + "\n" + LinuxSupport.Tail(Path.Combine(last, "status.txt"), 8192)); return;
        }
        using var gameLock = LinuxInstaller.Lock(gameDir!);
        if (command == "record-cancel") { LinuxRecording.Cancel(gameDir!); Console.WriteLine("Pending recording cancelled. Existing recordings retained."); return; }
        if (command is not ("record" or "record-setup")) throw new ArgumentException("Unknown recording command. Use --help.");
        var launchOption = LinuxRecording.Setup(gameDir!, settings);
        Console.WriteLine("One-time setup: Steam → Helldivers 2 → Properties → Launch Options.\nFor an empty Launch Options field, paste:\n" + launchOption);
        Console.WriteLine("Keep existing custom options: place this wrapper before their command. Install MangoHud's Vulkan layer in the same native/Flatpak environment as Steam. Recording targets desktop sessions; Gamescope/Gaming Mode requires separate mangoapp integration.");
        if (LinuxPlatform.SteamRoots().Any(p => p.Replace('\\', '/').Contains("/snap/steam/", StringComparison.Ordinal) && Directory.Exists(p)))
            Console.WriteLine("Snap Steam detected. Its bundled MangoHud support is experimental and Vulkan capture may be unavailable. A host MangoHud package does not establish that the Snap can load it; verify the CSV with record-status.");
        if (command == "record")
        {
            var session = LinuxRecording.Arm(gameDir!, settings);
            Console.WriteLine($"Armed for the next Steam launch. Use Launch or start the game in Steam.\nSession: {session}\nFrame recording runs for up to 10 minutes. Use record-status afterward to check that a CSV was produced.");
        }
    }
}
