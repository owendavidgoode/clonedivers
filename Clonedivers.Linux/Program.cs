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

    public static async Task<int> Main(string[] args)
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
            if (gameDir is null)
            {
                if (words.Count > 0 || Console.IsInputRedirected) throw new DirectoryNotFoundException("Game not found. Pass --game '/path/to/steamapps/common/Helldivers 2'.");
                Console.Write("Helldivers 2 folder (containing data and bin): ");
                gameDir = GameLocator.NormalizeGameDir(Console.ReadLine());
                if (gameDir is null) throw new DirectoryNotFoundException("That folder does not contain data and bin/helldivers2.exe.");
            }
            settings.GamePath = gameDir;
            if (words.Count > 0)
            {
                if (words.Count > (words[0] == "install" ? 2 : 1)) throw new ArgumentException("Too many command arguments. Use --help.");
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
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { cancellation.Dispose(); }
    }

    static string Value(string[] args, ref int i) => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
    static void Help() => Console.WriteLine($"""
        Clonedivers Linux {AppInfo.Version} — terminal launcher
        Usage: ./clonedivers [--game '/path/to/Helldivers 2'] [--yes] [command]
        No command: open the menu.
          status                  Show local installation state
          install [clone|empire]  Download/update and enable the chosen mode
          vanilla                 Park mods for ordinary Helldivers
          repair                  Hash-check and repair the installed selection
          finish                  Recover an interrupted update
          launch                  Ask Steam to launch Helldivers 2
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
            Console.WriteLine("\n1  Install/update Clonedivers\n2  Install/update EmpireDivers\n3  Vanilla Helldivers\n4  Options\n5  Check and repair\n6  Finish interrupted update\n7  Launch through Steam\n0  Quit");
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
                    default: Console.WriteLine("Choose a number from the menu."); break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Console.Error.WriteLine(ex.Message); }
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
            settings.Save(); return;
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
        Console.WriteLine($"Pack {result.PackVersion} ready. Use launch to start through Steam.");
    }
}
