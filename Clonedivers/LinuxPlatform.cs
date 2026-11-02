using System.Diagnostics;

namespace Clonedivers;

public sealed record SteamLaunchTarget(string Client, string SteamRoot, ProcessStartInfo Command);

public static class PlatformPaths
{
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static string Identity(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }
    public static string ConfigDirectory => OperatingSystem.IsLinux()
        ? Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { } xdg && Path.IsPathFullyQualified(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "clonedivers")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers");
}

public static class LinuxPlatform
{
    public static string FpvValuesPath(string gameDir)
    {
        var configured = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH");
        var compat = !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured : Path.GetFullPath(Path.Combine(gameDir, "..", "..", "compatdata", GameLocator.SteamAppId));
        var user = Path.Combine(compat, "pfx", "drive_c", "users", "steamuser");
        if (!Directory.Exists(user)) throw new DirectoryNotFoundException("Proton prefix not found. Launch vanilla Helldivers 2 through Steam once, close it, then retry. For a custom prefix, set STEAM_COMPAT_DATA_PATH.");
        return Path.Combine(user, "AppData", "Local", "CowboyBingus", "Helldivers2", "Logs", "ModOptionsMenu.values");
    }

    public static IEnumerable<string> SteamRoots(string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".local/share/Steam");
        yield return Path.Combine(home, ".steam/steam");
        yield return Path.Combine(home, ".steam/root");
        yield return Path.Combine(home, ".steam/debian-installation");
        yield return Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam");
        yield return Path.Combine(home, "snap/steam/common/.local/share/Steam");
        yield return Path.Combine(home, "snap/steam/common/.steam/steam");
    }

    // Read-only discovery accepts Steam's usual symlink aliases, including symlinked libraries.
    static string CanonicalDirectory(string path)
    {
        var full = Path.GetFullPath(path); var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null) current = directory.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unresolved Steam folder link.");
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    static bool Within(string path, string parent) => path.Equals(parent, PlatformPaths.Comparison) || path.StartsWith(parent + Path.DirectorySeparatorChar, PlatformPaths.Comparison);

    public static SteamLaunchTarget SteamLaunchForGame(string gameDir, string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!GameLocator.IsGameDir(gameDir) || !File.Exists(SteamAcf.AcfPath(gameDir)))
            throw new InvalidOperationException("Choose the installed Helldivers 2 folder from Steam → Manage → Browse local files before launching.");
        var game = CanonicalDirectory(gameDir);
        var snapHome = CanonicalDirectory(Path.Combine(home, "snap/steam/common"));
        var flatpakHome = CanonicalDirectory(Path.Combine(home, ".var/app/com.valvesoftware.Steam"));
        var owners = new List<(string client, string root)>();
        foreach (var root in SteamRoots(home).Where(Directory.Exists))
        {
            var resolved = CanonicalDirectory(root);
            var libraries = new List<string> { root };
            try { libraries.AddRange(GameLocator.ParseLibraryPaths(File.ReadAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"))).Where(Path.IsPathFullyQualified)); }
            catch (IOException) { /* A missing list does not hide a game in this root's own library. */ }
            catch (UnauthorizedAccessException) { }
            if (!libraries.Any(l => CanonicalDirectory(Path.Combine(l, "steamapps", "common", GameLocator.GameFolderName)).Equals(game, PlatformPaths.Comparison))) continue;
            var client = Within(resolved, snapHome) || Within(Path.GetFullPath(root), Path.GetFullPath(Path.Combine(home, "snap/steam/common"))) ? "Snap Steam"
                : Within(resolved, flatpakHome) || Within(Path.GetFullPath(root), Path.GetFullPath(Path.Combine(home, ".var/app/com.valvesoftware.Steam"))) ? "Flatpak Steam" : "Native Steam";
            if (!owners.Any(o => o.client == client && o.root.Equals(resolved, PlatformPaths.Comparison))) owners.Add((client, resolved));
        }
        if (owners.Count != 1)
            throw new InvalidOperationException((owners.Count == 0 ? "Cannot determine which Steam installation owns this game folder." : "More than one Steam installation lists this game folder.") + " Open your usual Steam and launch Helldivers 2 there. The installed mods still apply.");
        var owner = owners[0];
        var start = new ProcessStartInfo { UseShellExecute = false };
        if (owner.client == "Snap Steam") { start.FileName = "snap"; start.ArgumentList.Add("run"); start.ArgumentList.Add("steam"); }
        else if (owner.client == "Flatpak Steam") { start.FileName = "flatpak"; start.ArgumentList.Add("run"); start.ArgumentList.Add("com.valvesoftware.Steam"); }
        else
        {
            start.FileName = Path.Combine(owner.root, "steam.sh"); start.WorkingDirectory = owner.root;
            if (!File.Exists(start.FileName)) throw new InvalidOperationException("The owning native Steam installation has no steam.sh launcher. Open your usual Steam and launch Helldivers 2 there.");
        }
        start.ArgumentList.Add("steam://rungameid/" + GameLocator.SteamAppId);
        return new SteamLaunchTarget(owner.client, owner.root, start);
    }

    public static string LaunchGame(string gameDir)
    {
        var target = SteamLaunchForGame(gameDir);
        // Process.Start can prefer a sibling of the apphost over PATH (notably /usr/bin).
        // Resolve explicitly so source, portable and installed builds use the same launcher.
        if (!Path.IsPathFullyQualified(target.Command.FileName))
        {
            var name = target.Command.FileName;
            target.Command.FileName = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Where(Path.IsPathFullyQualified).Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException(target.Client + " launcher is unavailable on PATH. Open your usual Steam and launch Helldivers 2 there.");
        }
        using var process = Process.Start(target.Command) ?? throw new IOException("Steam could not be started.");
        return target.Client;
    }

    public static IEnumerable<string> CandidateGameDirs(string? home = null)
    {
        var libraries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var steam in SteamRoots(home).Where(Directory.Exists))
        {
            libraries.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            string text;
            try { text = File.ReadAllText(vdf); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var library in GameLocator.ParseLibraryPaths(text))
                if (Path.IsPathFullyQualified(library)) libraries.Add(library);
        }
        return libraries.OrderByDescending(l => File.Exists(Path.Combine(l, "steamapps", "appmanifest_553850.acf")))
            .Select(l => Path.Combine(l, "steamapps", "common", GameLocator.GameFolderName));
    }

    public static bool IsGameRunning(string procRoot = "/proc")
    {
        // Wine's process name can be truncated; argv retains the full Windows executable name.
        // A missing /proc must stop an install rather than claim the game is closed.
        foreach (var dir in Directory.EnumerateDirectories(procRoot))
        {
            if (!int.TryParse(Path.GetFileName(dir), out _)) continue;
            try
            {
                if (IsGameExecutable(File.ReadAllText(Path.Combine(dir, "comm")).Trim())) return true;
                if (File.ReadAllText(Path.Combine(dir, "cmdline")).Split('\0').Any(IsGameExecutable)) return true;
            }
            catch (IOException) { /* A process exited during the scan. */ }
            catch (UnauthorizedAccessException) { /* Processes owned by other users. */ }
        }
        return false;
    }

    public static bool IsGameExecutable(string argument) =>
        argument.Replace('\\', '/').Split('/').Last() is var name &&
        (name.Equals("helldivers2.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("helldivers2", StringComparison.OrdinalIgnoreCase));
}
