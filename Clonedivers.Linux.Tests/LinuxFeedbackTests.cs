using System.Text;
using System.Text.Json;
using Clonedivers;

internal static class LinuxFeedbackTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "linux-feedback"); Directory.CreateDirectory(folder);
        foreach (var reportedLength in new long[] { 0, 4096, -1 })
        {
            using var stream = new VirtualFile("0x1002\n", reportedLength);
            check(LinuxSystemInfo.Head(stream, 128) == "0x1002", "OS metadata reads content without seeking or trusting virtual length " + reportedLength);
        }
        using (var longStream = new VirtualFile(new string('x', 10000), 0))
            check(LinuxSystemInfo.Head(longStream, 17) == new string('x', 17) && longStream.Position == 17, "metadata read stops at its byte budget without a log-tail marker");
        var cpu = Path.Combine(folder, "cpuinfo"); File.WriteAllText(cpu, "processor : 0\nmodel name\t: AMD Ryzen fixture\n");
        check(LinuxSystemInfo.Cpu(cpu) == "AMD Ryzen fixture", "CPU report contains the first model value");
        var device = Path.Combine(folder, "drm", "card0", "device"); Directory.CreateDirectory(device);
        File.WriteAllText(Path.Combine(device, "vendor"), "0x1002\n"); File.WriteAllText(Path.Combine(device, "device"), "0x7550\n");
        var gpu = JsonSerializer.SerializeToElement(LinuxSystemInfo.Gpus(Path.Combine(folder, "drm")))[0];
        check(gpu.GetProperty("Vendor").GetString() == "0x1002" && gpu.GetProperty("PciDevice").GetString() == "0x7550" && gpu.GetProperty("Driver").GetString() == "unknown", "GPU IDs survive a missing driver link and never contain truncation prose");
        File.WriteAllText(Path.Combine(device, "vendor"), "[Only the end of this file is included.]");
        check(LinuxSystemInfo.PciId(Path.Combine(device, "vendor")) == "unknown", "malformed PCI metadata is not reported as a hardware identifier");
        check(LinuxPlatform.HostLibraryPath("/var/lib/snapd/hostfs/mnt/SSD/SteamLibrary") == "/mnt/SSD/SteamLibrary", "Snap namespace library paths map to the host mount");
        check(LinuxPlatform.HostLibraryPath("/var/lib/snapd/hostfs-other/mnt/SSD") == "/var/lib/snapd/hostfs-other/mnt/SSD", "similarly named paths are never stripped");
        var home = Path.Combine(folder, "home");
        var native = LinuxPlatform.SteamRoots(home).First();
        var snap = Path.Combine(home, "snap/steam/common/.local/share/Steam");
        foreach (var steam in new[] { native, snap }) Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        var library = Path.Combine(folder, "External SSD");
        var game = Path.Combine(library, "steamapps", "common", "Helldivers 2");
        Directory.CreateDirectory(Path.Combine(game, "data")); Directory.CreateDirectory(Path.Combine(game, "bin"));
        File.WriteAllText(Path.Combine(game, "bin/helldivers2.exe"), "fixture");
        File.WriteAllText(Path.Combine(library, "steamapps/appmanifest_553850.acf"), "\"buildid\" \"25480438\"");
        var recordedLibrary = OperatingSystem.IsLinux() ? "/var/lib/snapd/hostfs" + library : library;
        File.WriteAllText(Path.Combine(snap, "steamapps/libraryfolders.vdf"), "\"path\" \"" + recordedLibrary.Replace("\\", "\\\\") + "\"");
        check(LinuxPlatform.CandidateGameDirs(home).Contains(game), "external Snap-owned HD2 remains discoverable alongside an empty native Steam");
        var target = LinuxPlatform.SteamLibraryForGame(game, home);
        check(target.Client == "Snap Steam" && target.Command.FileName == "snap" && target.Command.ArgumentList.SequenceEqual(new[] { "run", "steam", "steam://nav/games/details/553850" }), "Official HD2 button opens the owning Snap client library without the desktop URI handler or launching the game");
        check(LinuxPlatform.ForgeSteamRoot(game, null, home) == Path.GetFullPath(snap), "Forge wrapper receives the owner rather than the first empty native client");
        check(LinuxPlatform.ForgeSteamRoot(null, snap, home) == Path.GetFullPath(snap), "an explicitly configured Steam client remains authoritative");
        try { LinuxPlatform.ForgeSteamRoot(null, "relative-root", home); check(false, "relative Steam root must fail"); }
        catch (InvalidOperationException) { check(true, "invalid explicit Steam root fails before any wrapper starts"); }
        var setupHome = Path.Combine(folder, "setup-home");
        var setupSteam = LinuxPlatform.SteamRoots(setupHome).First(); Directory.CreateDirectory(Path.Combine(setupSteam, "steamapps"));
        check(LinuxPlatform.ForgeSteamRoot(null, null, setupHome) == Path.GetFullPath(setupSteam), "a single Steam client supports Forge setup without a live HD2 installation");
        Directory.CreateDirectory(Path.Combine(setupHome, "snap/steam/common/.local/share/Steam/steamapps"));
        try { LinuxPlatform.ForgeSteamRoot(null, null, setupHome); check(false, "ambiguous setup clients must fail"); }
        catch (InvalidOperationException) { check(true, "multiple clients without ownership evidence require explicit selection instead of bootstrapping native Steam"); }
        var plan = new LinuxForgePlan("install", "prefix", "C:\\HellForge", "bridge", "script") { SteamClientRoot = snap };
        var start = plan.StartInfo();
        check(start.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] == snap && start.Environment["STEAM_COMPAT_DATA_PATH"] == "prefix" && !start.Environment.ContainsKey("HELLFORGE_LINUX_NETNS"), "wrapper receives supported Steam selection while keeping its dedicated prefix and namespace marker ownership");
        if (OperatingSystem.IsLinux())
        {
            var alias = Path.Combine(home, "snap/steam/common/.steam/steam"); Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
            Directory.CreateSymbolicLink(alias, snap);
            check(LinuxPlatform.SteamLaunchForGame(game, home).Client == "Snap Steam", "Snap primary and .steam alias resolve to one owner after a library move");
            var os = Path.Combine(folder, "real-os-release"); File.WriteAllText(os, "PRETTY_NAME=\"Fixture distro\"\n");
            var osLink = Path.Combine(folder, "os-release"); File.CreateSymbolicLink(osLink, os);
            check(LinuxSystemInfo.Head(osLink, 8192).Contains("Fixture distro"), "normal os-release symlink is accepted only by the read-only metadata collector");
            check(LinuxSystemInfo.Head("/proc/sys/kernel/osrelease", 256) != "unknown" && LinuxSystemInfo.Head("/etc/os-release", 8192).Contains("PRETTY_NAME=") && LinuxSystemInfo.Cpu() != "unknown", "real Linux kernel, distribution and procfs CPU metadata are nonempty");
        }
    }

    sealed class VirtualFile(string content, long reportedLength) : MemoryStream(Encoding.UTF8.GetBytes(content))
    {
        public override long Length => reportedLength < 0 ? throw new NotSupportedException() : reportedLength;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException("Virtual metadata must never seek.");
    }
}
