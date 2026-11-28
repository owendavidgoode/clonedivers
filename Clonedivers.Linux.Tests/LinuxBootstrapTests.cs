using System.Formats.Tar;
using System.IO.Compression;
using Clonedivers;

internal static class LinuxBootstrapTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        check(LinuxForgeBootstrap.Installer.Size == 451363890 && LinuxForgeBootstrap.Installer.Sha256 == "2688d0382086839f540046cce68c1d16dd44ccf5b476f4b5d3ee80de7cf06532",
            "native Linux setup uses the released 0.5.0-beta author artifact's independent byte/hash pins");
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

        // HellForge 0.5: TryRestoreModListAfter05Upgrade restores custom hellforge-mods.json (including UTF-8 BOM), sets importDone=true, and syncs app/bundled-mods
        var upgLinuxRoot = Path.Combine(root, "bootstrap-05-modlist", "pfx", "drive_c", "HellForge");
        Directory.CreateDirectory(Path.Combine(upgLinuxRoot, "app", "bundled-mods", "hell-forge"));
        Directory.CreateDirectory(Path.Combine(upgLinuxRoot, "mods"));
        File.WriteAllText(Path.Combine(upgLinuxRoot, "app", "app-build.json"), """{"version":"0.5.0-beta","engine":"0.1.58"}""");
        File.WriteAllText(Path.Combine(upgLinuxRoot, "app", "bundled-mods", "hell-forge", "hellforge-mod.json"), """{"id":"skyeshade.hell-forge","version":"0.16.1"}""");
        File.WriteAllText(Path.Combine(upgLinuxRoot, "mods", "hellforge-mods.json"), """{"format":1,"share":true,"order":["hellforge.editor"]}""");
        var bomSaved = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("""{"format":1,"share":false,"order":["clonedivers.chaos-invasions"]}""")).ToArray();
        LinuxForgeBootstrap.TryRestoreModListAfter05Upgrade(upgLinuxRoot, bomSaved);
        using var restoredLinuxDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(upgLinuxRoot, "mods", "hellforge-mods.json")));
        check(!restoredLinuxDoc.RootElement.GetProperty("share").GetBoolean() &&
              restoredLinuxDoc.RootElement.GetProperty("importDone").GetBoolean() &&
              restoredLinuxDoc.RootElement.GetProperty("order")[0].GetString() == "clonedivers.chaos-invasions" &&
              File.Exists(Path.Combine(upgLinuxRoot, "mods", "hell-forge", "hellforge-mod.json")),
              "LinuxForgeBootstrap.TryRestoreModListAfter05Upgrade restores BOM-prefixed mod list with importDone=true and syncs app/bundled-mods on 0.5.0-beta");
    }
}
