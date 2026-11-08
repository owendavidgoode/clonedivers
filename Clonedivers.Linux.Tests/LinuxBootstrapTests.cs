using System.Formats.Tar;
using System.IO.Compression;
using Clonedivers;

internal static class LinuxBootstrapTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        check(LinuxForgeBootstrap.Installer.Size == 439876336 && LinuxForgeBootstrap.Installer.Sha256 == "62e599c39cf9f102c4efecfc73311e8bf02dc74d1d559cfe6506ed023ac5b5d5",
            "native Linux setup uses the released author artifact's independent byte/hash pins");
        var home = Path.Combine(root, "bootstrap-home");
        var install = Path.Combine(home, ".local", "share", "hellforge-prefix", "pfx", "drive_c", "users", "steamuser", "Documents", "HellForge");
        Directory.CreateDirectory(install); File.WriteAllText(Path.Combine(install, "hellforge-install.json"), "{}");
        check(LinuxForgeBootstrap.Detect(home: home) == install && LinuxForgeBootstrap.Detect(install, home) == install,
            "default-prefix setup reconnects without Locate and an existing selected root wins over discovery");
        check(LinuxForgeBootstrap.PrefixFor(install).EndsWith("hellforge-prefix"), "an existing player's prefix is identified before offering an application update");
        var loose = Path.Combine(root, "loose-forge"); Directory.CreateDirectory(loose); File.WriteAllText(Path.Combine(loose, "hellforge-install.json"), "{}");
        check(LinuxForgeBootstrap.ExistingRoot("") == null && LinuxForgeBootstrap.ExistingRoot("  ") == null && LinuxForgeBootstrap.ExistingRoot(home) == null &&
            LinuxForgeBootstrap.ExistingRoot(loose) == null && LinuxForgeBootstrap.ExistingRoot(" " + install + " ") == install,
            "setup treats a blank, unmarked or prefix-less saved folder as a fresh install instead of failing after the download");
        var emptyHome = Path.Combine(root, "bootstrap-empty-home"); Directory.CreateDirectory(emptyHome);
        check(LinuxForgeBootstrap.StartupRoot("", home) == install && LinuxForgeBootstrap.StartupRoot(Path.Combine(root, "moved"), home) == install &&
            LinuxForgeBootstrap.StartupRoot(install, emptyHome) == install && LinuxForgeBootstrap.StartupRoot(" ", emptyHome) == null,
            "a blank or stale saved Forge folder never disables startup discovery");
        var staging = Path.Combine(root, "bootstrap-staging"); Directory.CreateDirectory(Path.Combine(staging, "unpacked-a", "nested")); Directory.CreateDirectory(Path.Combine(staging, "unpacked-b"));
        File.WriteAllText(Path.Combine(staging, "unpacked-a", "nested", "HellForge-Linux.zip"), "fixture"); File.WriteAllText(Path.Combine(staging, LinuxForgeBootstrap.Installer.Name), "archive");
        LinuxForgeBootstrap.SweepStaging(staging);
        check(!Directory.EnumerateDirectories(staging, "unpacked-*").Any() && File.Exists(Path.Combine(staging, LinuxForgeBootstrap.Installer.Name)),
            "repeated setup runs remove earlier extracted installers and keep the verified archive");
        var tools = Path.Combine(root, "bootstrap-tools"); Directory.CreateDirectory(tools);
        foreach (var name in new[] { "ip", "unshare", "flock", "pgrep", "python3" }) File.WriteAllText(Path.Combine(tools, name), "fixture");
        check(LinuxForgeBootstrap.MissingTools(tools).SequenceEqual(new[] { "pasta" }), "co-op preflight identifies the missing relay program instead of accepting a solo-only setup");
        foreach (var unsafeEntry in new[] { "../outside", "/outside", "link", "" })
        {
            var archive = Path.Combine(root, "bootstrap-" + Guid.NewGuid().ToString("N") + ".tgz");
            using (var output = File.Create(archive))
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            using (var writer = new TarWriter(gzip))
            {
                foreach (var name in new[] { "./HellForge.Launcher", "./HellForge-Linux.zip" })
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { Mode = UnixFileMode.UserRead | UnixFileMode.UserExecute, DataStream = new MemoryStream("fixture"u8.ToArray()) });
                if (unsafeEntry.Length > 0)
                    writer.WriteEntry(unsafeEntry == "link" ? new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "outside" } :
                        new PaxTarEntry(TarEntryType.RegularFile, unsafeEntry) { DataStream = new MemoryStream("bad"u8.ToArray()) });
            }
            var target = Path.Combine(root, "bootstrap-extract-" + Guid.NewGuid().ToString("N"));
            try
            {
                var program = await LinuxForgeBootstrap.ExtractAsync(archive, target, CancellationToken.None);
                check(unsafeEntry.Length == 0 && File.Exists(program), "complete native installer stages without running any program");
                if (OperatingSystem.IsLinux()) check((File.GetUnixFileMode(program) & UnixFileMode.UserExecute) != 0, "native installer executable mode survives archive staging");
            }
            catch (InvalidDataException) { check(unsafeEntry.Length > 0 && !File.Exists(Path.Combine(root, "outside")), "unsafe archive entry cannot escape staging: " + unsafeEntry); }
        }
    }
}
