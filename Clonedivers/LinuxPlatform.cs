namespace Clonedivers;

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
        yield return Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam");
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
