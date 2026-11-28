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

        // Issue #44: Idempotent hf-inner.sh self_cmdline subshell-exclusion patch against verbatim 0.5.0-beta hf-inner.sh + LinuxStaleSessionGuard orphan cleanup
        var innerRoot = Path.Combine(root, "hf-inner-patch", "pfx", "drive_c", "HellForge");
        Directory.CreateDirectory(Path.Combine(innerRoot, "app", "linux"));
        var upstreamUnpatchedInner = """
            #!/usr/bin/env bash
            # Processes of this installation only. The wrapper and its parents have the Windows exe path in their arguments, so leave
            # them out. Wine shows Windows paths (with backslashes) in command lines; the environment works for both forms. This is
            # also the scan a Proton timeout uses to end what timeout(1) left behind (H.9).
            wrapper_pids=" $$"
            parent=$PPID
            while [[ "$parent" =~ ^[0-9]+$ ]] && [ "$parent" -gt 1 ]; do
              wrapper_pids+=" $parent"
              parent="$(ps -o ppid= -p "$parent" 2>/dev/null | tr -d '[:space:]')"
            done
            windows_install="Z:${INSTALL//\//\\}"
            case "$INSTALL" in
              */pfx/drive_c/*)
                windows_install="C:\\${INSTALL#*/pfx/drive_c/}"
                windows_install="${windows_install//\//\\}"
                ;;
            esac
            of_this_install() {
              local pid=$1 text
              case " $wrapper_pids " in *" $pid "*) return 1 ;; esac
              text="$({ tr '\0' '\n' < "/proc/$pid/cmdline"; } 2>/dev/null || true)"
              case "$text" in *"$INSTALL"*|*"$windows_install"*) return 0 ;; esac
              text="$({ tr '\0' '\n' < "/proc/$pid/environ"; } 2>/dev/null || true)"
              case $'\n'"$text"$'\n' in *$'\n'"INSTALL=$INSTALL"$'\n'*) return 0 ;; esac
              case "$(readlink "/proc/$pid/cwd" 2>/dev/null || true)" in "$INSTALL"|"$INSTALL"/*) return 0 ;; esac
              return 1
            }
            matching_pids() {
              local pattern=$1 pid
              while IFS= read -r pid; do
                [ -n "$pid" ] && of_this_install "$pid" && printf '%s\n' "$pid"
              done < <(pgrep -u "$(id -u)" -f "$pattern" 2>/dev/null || true)
              return 0
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var innerScript = Path.Combine(innerRoot, "app", "linux", "hf-inner.sh");
        File.WriteAllBytes(innerScript, System.Text.Encoding.UTF8.GetBytes(upstreamUnpatchedInner));
        int firstPatch = HellForgeRelease.PatchLinuxInnerWrapper(innerRoot);
        var patchedText = File.ReadAllText(innerScript);
        int secondPatch = HellForgeRelease.PatchLinuxInnerWrapper(innerRoot);
        check(firstPatch == 1 &&
              secondPatch == 0 &&
              !patchedText.Contains('\r') &&
              patchedText.Contains("  parent=\"$(ps -o ppid= -p \"$parent\" 2>/dev/null | tr -d '[:space:]')\"\ndone\nself_cmdline=\"$({ tr '\\0' '\\n' < \"/proc/$$/cmdline\"; } 2>/dev/null || true)\"\nwindows_install=\"Z:${INSTALL//\\//\\\\}\"", StringComparison.Ordinal) &&
              patchedText.Contains("  text=\"$({ tr '\\0' '\\n' < \"/proc/$pid/cmdline\"; } 2>/dev/null || true)\"\n  [ -n \"$self_cmdline\" ] && [ \"$text\" = \"$self_cmdline\" ] && return 1\n  case \"$text\" in *\"$INSTALL\"*|*\"$windows_install\"*) return 0 ;; esac", StringComparison.Ordinal),
              "Issue #44: HellForgeRelease.PatchLinuxInnerWrapper idempotently patches verbatim 0.5.0-beta hf-inner.sh with LF line endings");

        var futureUpstreamScript = Path.Combine(innerRoot, "hf-inner.sh");
        const string futureUpstream = "#!/usr/bin/env bash\n# Upstream fixed with $BASHPID\n[ \"$pid\" = \"$BASHPID\" ] && return 1\n";
        File.WriteAllText(futureUpstreamScript, futureUpstream);
        check(!HellForgeRelease.PatchLinuxInnerScriptFile(futureUpstreamScript) &&
              File.ReadAllText(futureUpstreamScript) == futureUpstream,
              "Issue #44: PatchLinuxInnerScriptFile leaves upstream BASHPID-fixed scripts untouched");

        // Synthetic /proc test for LinuxStaleSessionGuard: cleans orphaned hf-inner.sh (even when its cmdline holds HellForge.Session.exe) & steam-bridge inner,
        // preserves BOTH steam-bridge host AND Clonedivers' own live ClonediversBridge child, and aborts if helldivers2.exe or HellForge.Session.exe is alive
        var fakeProc = Path.Combine(root, "fake-proc-44");
        void WriteProc(int pid, string comm, string[] cmdline, string[] environ, int ppid = 1, string? cwdPath = null)
        {
            var pDir = Path.Combine(fakeProc, pid.ToString());
            Directory.CreateDirectory(pDir);
            File.WriteAllText(Path.Combine(pDir, "comm"), comm + "\n");
            File.WriteAllText(Path.Combine(pDir, "status"), $"Name:\t{comm}\nPPid:\t{ppid}\n");
            File.WriteAllText(Path.Combine(pDir, "cmdline"), string.Join("\0", cmdline) + "\0");
            File.WriteAllText(Path.Combine(pDir, "environ"), string.Join("\0", environ) + "\0");
            if (cwdPath != null && OperatingSystem.IsLinux())
                File.CreateSymbolicLink(Path.Combine(pDir, "cwd"), cwdPath);
        }
        var normRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(innerRoot));
        WriteProc(9999, "Clonedivers", ["/opt/clonedivers/Clonedivers"], [], ppid: 1000);
        WriteProc(4100, "HellForge.Launc", [Path.Combine(root, "ClonediversLauncher", "HellForge.Launcher.ClonediversBridge"), "--install", normRoot], [$"INSTALL={normRoot}"], ppid: 9999, cwdPath: normRoot);
        WriteProc(4101, "hf-inner.sh", ["bash", Path.Combine(normRoot, "app", "linux", "hf-inner.sh"), @"C:\HellForge\app\HellForge.Session.exe", "play"], [$"INSTALL={normRoot}"]);
        WriteProc(4102, "HellForge.Cli", [Path.Combine(normRoot, "app", "cli-linux", "HellForge.Cli"), "steam-bridge", "inner"], [$"INSTALL={normRoot}"]);
        WriteProc(4103, "HellForge.Cli", [Path.Combine(normRoot, "app", "cli-linux", "HellForge.Cli"), "steam-bridge", "host"], [$"INSTALL={normRoot}"]);
        var killedPids = new List<int>();
        var cleanedLabels = LinuxStaleSessionGuard.CleanupStaleOrphans(normRoot, fakeProc, selfPid: 9999, killOverride: killedPids.Add);
        check(killedPids.OrderBy(x => x).SequenceEqual(new[] { 4101, 4102 }) &&
              !killedPids.Contains(4100) &&
              !killedPids.Contains(4103) &&
              cleanedLabels.Count == 2,
              "Issue #44: LinuxStaleSessionGuard cleans orphaned hf-inner.sh and steam-bridge inner while preserving steam-bridge host and live ClonediversBridge child");

        // When helldivers2.exe is live for this install, CleanupStaleOrphans MUST NOT kill anything
        WriteProc(4104, "Main", [@"C:\HellForge\game\bin\helldivers2.exe"], [$"INSTALL={normRoot}"]);
        killedPids.Clear();
        var liveScan = LinuxStaleSessionGuard.CleanupStaleOrphans(normRoot, fakeProc, selfPid: 9999, killOverride: killedPids.Add);
        check(liveScan.Count == 0 && killedPids.Count == 0,
              "Issue #44: LinuxStaleSessionGuard aborts without killing anything when helldivers2.exe is alive");

        // Crashed session result.json missing completedUtc detection
        var crashSessDir = Path.Combine(normRoot, "logs", "sessions", "0da73d9f111122223333444455556666");
        Directory.CreateDirectory(crashSessDir);
        File.WriteAllText(Path.Combine(crashSessDir, "result.json"), """{"exitCode":139}""");
        check(LinuxStaleSessionGuard.HasCrashedIncompleteSession(normRoot),
              "Issue #44: HasCrashedIncompleteSession detects crashed session result.json lacking completedUtc");
        File.WriteAllText(Path.Combine(crashSessDir, "result.json"), """{"exitCode":0,"completedUtc":"2026-11-28T05:00:00Z"}""");
        check(!LinuxStaleSessionGuard.HasCrashedIncompleteSession(normRoot),
              "Issue #44: HasCrashedIncompleteSession returns false once completedUtc is recorded");

        // Issues #43/#45/#46: Ethan's RX 9070 (16 GB VRAM / 30.2 GiB RAM + 8 GiB swap) Auto -> High (lighter) + /proc DRM VRAM/GTT & TTM sampler
        var ethanRec = PerformanceTierAdvisor.Recommend("AMD Radeon RX 9070", 16.0, 30.21, isSoftwareOrIntegrated: false, swapGb: 8.0, isLinux: true);
        var linuxLowSwap4090 = PerformanceTierAdvisor.Recommend("NVIDIA GeForce RTX 4090", 24.0, 40.0, isSoftwareOrIntegrated: false, swapGb: 4.0, isLinux: true);
        var linuxGood4090 = PerformanceTierAdvisor.Recommend("NVIDIA GeForce RTX 4090", 24.0, 40.0, isSoftwareOrIntegrated: false, swapGb: 8.0, isLinux: true);
        check(ethanRec.RecommendedTierId == PerformanceTierAdvisor.High &&
              PerformanceTierAdvisor.ResolvePackTexture(PerformanceTierAdvisor.Auto, ethanRec) == "lighter" &&
              linuxLowSwap4090.RecommendedTierId == PerformanceTierAdvisor.High &&
              linuxGood4090.RecommendedTierId == PerformanceTierAdvisor.Ultra,
              "Issues #45/#46: Linux AutoPerformanceTier selects High (lighter) on 16 GB VRAM / 30.2 GiB RAM and requires >18 GB VRAM + >=40 GiB RAM + >=48 GiB RAM+Swap for Ultra");
    }
}
