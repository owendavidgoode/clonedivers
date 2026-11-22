using System.Security.Cryptography;
using System.Text.Json;
using Clonedivers;

internal static class LinuxForgePlayerTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        const string hello = "{\"bridge\":\"clonedivers-forge\",\"format\":1}\n";
        check(HellForgeGameplayCatalog.Pinned.Id == "build25480438-r7" && HellForgeGameplayCatalog.Pinned.Artifacts.Count == 14 &&
            HellForgeGameplayCatalog.Pinned.Artifacts.Where(a => a.Optional).Select(a => a.Id).ToHashSet(StringComparer.Ordinal).SetEquals(HellForgePreset.Optional),
            "Linux embeds and validates the r7 gameplay catalog with all six optional mods");
        var brandNewLinux = LinuxForgePreferences.Load(Path.Combine(root, "brand-new-linux", "forge.json"));
        check(brandNewLinux.Mode == "clonedivers" && brandNewLinux.EnemySkin == "mixed-droids" && brandNewLinux.Covenant &&
            brandNewLinux.SquidSkin == "mixed-covenant" && brandNewLinux.DeathTrooperVoice &&
            brandNewLinux.DeltaSquad && brandNewLinux.AutoPerformanceTier && brandNewLinux.PerformanceTier == "auto" &&
            brandNewLinux.SoundEffectsEnabled && brandNewLinux.DiscordRpcEnabled && !brandNewLinux.MinimizeToTray &&
            brandNewLinux.HostChaosIntensity == "standard" && brandNewLinux.HostHeavySeaf == "standard" && brandNewLinux.HostFasterCorpseCleanup,
            "brand-new Linux installs default to the full-variety mix (mixed-droids, SquidSkin=mixed-covenant, DeathTrooperVoice=true, DeltaSquad=true) and AutoPerformanceTier=true");
        var legacyLinuxPath = Path.Combine(root, "legacy-linux-forge.json");
        File.WriteAllText(legacyLinuxPath, """{"Format":1,"Mode":"clonedivers","Texture":"lighter"}""");
        var legacyLinux = LinuxForgePreferences.Load(legacyLinuxPath);
        check(legacyLinux.EnemySkin == "all-droids" && !legacyLinux.Covenant && legacyLinux.SquidSkin == "native-squids" &&
            !legacyLinux.AutoPerformanceTier && legacyLinux.PerformanceTier == "high",
            "existing saved Linux forge.json files preserve legacy EnemySkin, Covenant=false (native-squids), and AutoPerformanceTier=false");

        var legacyLinuxCovTruePath = Path.Combine(root, "legacy-linux-cov-true.json");
        File.WriteAllText(legacyLinuxCovTruePath, """{"Format":1,"Mode":"clonedivers","Covenant":true}""");
        var legacyLinuxCovTrue = LinuxForgePreferences.Load(legacyLinuxCovTruePath);
        check(legacyLinuxCovTrue.SquidSkin == "all-covenant" && legacyLinuxCovTrue.Covenant && legacyLinuxCovTrue.DeathTrooperVoice,
            "existing Linux forge.json with Covenant=true (no SquidSkin) upgrades cleanly to SquidSkin='all-covenant'");

        // M4: Upgrading v2.0.3 manual PerformanceTier ('balanced'+'lighter' and 'ultra-potato'+'reduced512') reconciles PerformanceTier on Linux
        var linuxM4BalPath = Path.Combine(root, "linux-m4-balanced.json");
        File.WriteAllText(linuxM4BalPath, """{"Format":1,"Mode":"clonedivers","AutoPerformanceTier":false,"PerformanceTier":"balanced","Texture":"lighter"}""");
        var linuxM4Bal = LinuxForgePreferences.Load(linuxM4BalPath);
        var linuxM4PotPath = Path.Combine(root, "linux-m4-potato.json");
        File.WriteAllText(linuxM4PotPath, """{"Format":1,"Mode":"empire","AutoPerformanceTier":false,"PerformanceTier":"ultra-potato","Texture":"reduced512"}""");
        var linuxM4Pot = LinuxForgePreferences.Load(linuxM4PotPath);
        check(!linuxM4Bal.AutoPerformanceTier && linuxM4Bal.Texture == "lighter" && linuxM4Bal.PerformanceTier == "high" &&
              !linuxM4Pot.AutoPerformanceTier && linuxM4Pot.Texture == "reduced512" && linuxM4Pot.PerformanceTier == "low-512",
              "M4: LinuxForgePreferences.Load reconciles v2.0.3 manual PerformanceTier with saved Texture");

        // Milestone 2: all 5 texture profiles + 3-way SquidSkin + DeathTrooperVoice + SelectionCommand flags on Linux
        foreach (var tex in new[] { "full", "lighter", "reduced1024", "reduced512", "potato256" })
        {
            var lp = new LinuxForgePreferences
            {
                Mode = "empire",
                Texture = tex,
                AutoPerformanceTier = false,
                PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(tex),
                SquidSkin = "mixed-covenant",
                DeathTrooperVoice = false
            };
            lp.Validate();
            var cmd = lp.SelectionCommand();
            var sel = lp.PackSelection();
            var expectedResolved = tex == "reduced1024" ? "lighter" : tex == "potato256" ? "reduced512" : tex;
            check(sel.Texture == expectedResolved &&
                  sel.Options["covenant"] &&
                  cmd.Contains("--squid-skin") && cmd.Contains("mixed-covenant") &&
                  cmd.Contains("--deathtrooper") && cmd.Contains("no"),
                  $"LinuxForgePreferences accepts texture '{tex}', emits --squid-skin/--deathtrooper, and resolves r48 fallback cleanly");
        }

        // #67 Pre-window launching UX on LinuxForgeHost ("Launching Helldivers 2 — please wait…" + "Cancel Launch" -> "Helldivers 2 is running" + "END SESSION")
        await using (var linuxLaunching = new LinuxForgeHost(["--forge-preview", "launching"]))
        {
            var st = linuxLaunching.GetHomeState();
            check(st.IsSessionRunning &&
                  st.HeaderBadgeText == "Launching…" &&
                  st.Headline == ForgeTacticalCopy.LaunchingHeadline &&
                  st.Detail == ForgeTacticalCopy.LaunchingDetail &&
                  st.PrimaryActionLabel == ForgeTacticalCopy.LaunchingPrimaryButton &&
                  !st.PrimaryActionEnabled &&
                  st.SecondaryActionLabel == ForgeTacticalCopy.CancelLaunchButton &&
                  st.Progress is { IsIndeterminate: true },
                  "LinuxForgeHost shows 'Launching Helldivers 2 — please wait…', pulsing progress, disabled primary CTA, and 'Cancel Launch' while staging (#67)");
        }
        await using (var linuxRunning = new LinuxForgeHost(["--forge-preview", "running"]))
        {
            var st = linuxRunning.GetHomeState();
            check(st.IsSessionRunning &&
                  st.HeaderBadgeText == "Session Active" &&
                  st.Headline == ForgeTacticalCopy.RunningHeadline &&
                  st.PrimaryActionLabel == "END SESSION" &&
                  st.PrimaryActionEnabled &&
                  st.SecondaryActionLabel == null &&
                  st.Progress == null,
                  "LinuxForgeHost transitions to 'Helldivers 2 is running' + primary 'END SESSION' once game is running (#67)");
        }

        // L4: LinuxForgeHost.CanClose omits 'Cancel Launch' when attached to an unowned session (!OwnsSession)
        await using (var unownedHost = new LinuxForgeHost(["--forge"], new LinuxForgePreferences { InstallRoot = root }) { IsGameProcessRunning = () => false })
        {
            var unownedConn = new Connection { Running = true };
            var unownedPlayer = new LinuxForgePlayer(unownedConn, new LinuxForgePreferences { InstallRoot = root });
            await unownedPlayer.RefreshAsync();
            typeof(LinuxForgeHost).GetField("player", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(unownedHost, unownedPlayer);
            bool canCloseUnowned = unownedHost.CanClose(out var unownedReason);
            check(!canCloseUnowned && unownedReason != null && !unownedReason.Contains("Cancel Launch", StringComparison.Ordinal),
                  "L4: LinuxForgeHost.CanClose does not instruct user to click Cancel Launch when !OwnsSession");
        }

        var legacy512LinuxDir = Path.Combine(root, "legacy-linux-config-512");
        Directory.CreateDirectory(legacy512LinuxDir);
        File.WriteAllText(Path.Combine(legacy512LinuxDir, "config.json"), """{"TextureProfile":"reduced512","Options":{"empire":true}}""");
        var migratedLinux512 = LinuxForgePreferences.Load(Path.Combine(legacy512LinuxDir, "forge.json"));
        check(migratedLinux512.Mode == "empire" && migratedLinux512.Texture == "reduced512" &&
              !migratedLinux512.AutoPerformanceTier && migratedLinux512.PerformanceTier == PerformanceTierAdvisor.Low512,
            "Linux legacy config.json TextureProfile='reduced512' migrates cleanly to Texture='reduced512' and 'low-512' (L8)");

        await using (var linuxHostH4 = new LinuxForgeHost(["--forge-preview", "ready"], new LinuxForgePreferences { MinimizeToTray = true }))
        {
            check(linuxHostH4.GetHomeState().MinimizeToTray, "LinuxForgeHost exposes MinimizeToTray in ForgeHomeState on startup (H4)");
        }
        check(SquadFingerprint.Compute(HellForgeGameplayCatalog.Current).StartsWith("r7-", StringComparison.Ordinal) &&
            PerformanceTierAdvisor.Tiers.Count == 5 &&
            DiscordReportFormatter.Format("0.9.3", "r7-a9c4", "clonedivers", "mixed-droids", true, true, "auto", "lighter",
                PerformanceTierAdvisor.Recommend("AMD Radeon GPU", 8.0, 16.0), "/home/deck/HellForge", null).Split('\n').Length is >= 10 and <= 14,
            "Linux shares SquadFingerprint, 5-tier PerformanceTierAdvisor, and 10–14 line DiscordReportFormatter");
        foreach (var texture in new[] { "full", "lighter", "reduced512" })
        {
            var empire = new LinuxForgePreferences { Mode = "empire", Texture = texture, EnemySkin = "all-droids", Covenant = true };
            var on = empire.PackSelection();
            empire.EnemySkin = "mixed-droids";
            var mixed = empire.PackSelection();
            empire.Droids = false;
            var off = empire.PackSelection();
            var saved = Path.Combine(root, "empire-droids-" + texture + ".json"); empire.Save(saved);
            var restored = LinuxForgePreferences.Load(saved);
            check(on.Version == "2026.11.19-r48" && mixed.Version == "2026.11.19-r48" && off.Version == "2026.11.19-r48" &&
                on.Texture == texture && mixed.Texture == texture && off.Texture == texture &&
                on.Options["droids"] && !on.Options["mixed-droids"] && mixed.Options["droids"] && mixed.Options["mixed-droids"] && !off.Options["droids"] &&
                on.Options["covenant"] && off.Options["covenant"] && !restored.Droids &&
                restored.SelectionCommand().Skip(1).Chunk(2).Any(p => p[0] == "--droids" && p[1] == "no") &&
                restored.SelectionCommand().Skip(1).Chunk(2).Any(p => p[0] == "--texture" && p[1] == texture),
                "Linux Empire " + texture + " defaults CIS on, supports Mixed and Covenant, and persists/transmits all variant choices");
            check(on.Files.Count == 111 && mixed.Files.Count == 111 && off.Files.Count == 99 &&
                on.Files.Select(f => f.Name).SequenceEqual(mixed.Files.Select(f => f.Name)) &&
                on.Files.Any(f => f.Sha256 != mixed.Files.Single(g => g.Name == f.Name).Sha256) &&
                off.Files[0].Name == "9ba626afa44a3aa3.patch_1000",
                "Linux Empire " + texture + " exports all three enemy variants with fixed 1000+ archive layers");
            restored.Mode = "clonedivers";
            check(restored.PackSelection().Version == "2026.11.19-r20" && restored.PackSelection().Texture == texture &&
                !restored.PackSelection().Options["droids"] && restored.PackSelection().Options["covenant"] && restored.PackSelection().Files.Count == 102,
                "returning to Clones r20 preserves its saved texture, droid and Covenant selections");
        }
        var protocol = new ForgeProtocol(new StringReader(hello + "{\"id\":7,\"ok\":true,\"result\":{}}\n"), new StringWriter(), _ => { });
        await protocol.ConnectAsync(CancellationToken.None);
        try { await protocol.SendAsync(["status"]); } catch (InvalidDataException) { }
        check(protocol.Faulted, "an unknown bridge outcome is visible to the frontend as a lost connection");

        var preferences = new LinuxForgePreferences { InstallRoot = root };
        var connection = new Connection();
        var player = new LinuxForgePlayer(connection, preferences);
        await player.ApplySelectionAsync(); await player.LaunchAsync();
        check(player.OwnsSession && player.SessionActive, "fixture session is owned before the bridge fails");
        connection.Faulted = true; connection.FailDispose = true;
        check(player.Disconnected && !player.SessionActive && !player.CanLaunch, "a faulted bridge no longer pins the window open with a session it cannot end");
        await player.DisposeAsync();
        check(connection.Disposed && connection.Calls.All(c => c[0] != "stop"), "closing after a bridge fault hands cleanup to the bridge's EOF path without a doomed stop");

        connection = new Connection(); player = new LinuxForgePlayer(connection, preferences);
        await player.ApplySelectionAsync();
        preferences.Mode = "regular"; connection.RefuseSelect = true;
        try { await player.ApplySelectionAsync(); throw new Exception("FAIL: refused selection was accepted"); } catch (ForgeCommandException) { }
        await player.LaunchAsync();
        var play = connection.Calls.FindLastIndex(c => c[0] == "play"); var select = connection.Calls.FindLastIndex(c => c[0] == "select-mode");
        check(connection.Refused.Count == 1 && connection.Refused[0][2] == "regular" && play > select && select >= 0 && connection.Calls[select][2] == "regular","launch re-sends the shown selection so a refused select-mode never plays the stale mode");
        preferences.Mode = "clonedivers";

        connection = new Connection(); player = new LinuxForgePlayer(connection, preferences);
        await player.ImportAsync();
        check(player.State?.Account is { Imported: true, Message: "Opened 3 owned Warbonds." } && player.Error == null,
            "Linux import refresh displays the bridge's owned-Warbond status without losing imported-account readiness");
        connection.Account = new(true, true, "Opened 1 owned Warbonds.");
        await player.RefreshAsync();
        check(player.State is { Account.Message: "Opened 1 owned Warbonds." }, "Linux idle refresh carries the owned-Warbond status into the displayed account message");
        await player.LaunchAsync();
        var imports = connection.Calls.Count(c => c[0] == "account-import");
        try { await player.ImportAsync(); throw new Exception("FAIL: active session accepted account import"); } catch (InvalidOperationException) { }
        check(connection.Calls.Count(c => c[0] == "account-import") == imports, "Linux active sessions defer imports and their owned-Warbond opening before reaching the bridge");

        var bytes = "fixture pack bytes"u8.ToArray(); var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var source = Path.Combine(root, "player-source.bin"); File.WriteAllBytes(source, bytes);
        HellForgePack.Selection Selection(string texture) => new("clonedivers", texture, "1.0", HellForgePack.SupportedBuild, new string('a', 64),
            new() { ["droids"] = true }, [new PackFile { Name = "0123456789abcdef.patch_0", Size = bytes.Length, Sha256 = sha }]);
        var exports = Path.Combine(root, "player-exports"); var partial = Path.Combine(exports, "linux-partial"); Directory.CreateDirectory(partial);
        var full = Path.Combine(exports, "linux-full");
        await HellForgePack.ExportAsync(new HellForgePack.Plan(Selection("full"), full, new() { [sha] = source }), false);
        var reused = await LinuxForgePlayer.FindExportAsync([partial, full], Selection("full"));
        check(reused?.Output == full && reused.Missing.Count == 0, "a verified earlier export is reused instead of writing another full pack copy");
        check(await LinuxForgePlayer.FindExportAsync([partial, full], Selection("lighter")) == null, "a different selection never reuses a mismatched export");
        var start = DateTime.UtcNow.AddHours(-5);
        for (var i = 0; i < 4; i++) { var old = Path.Combine(exports, "linux-old" + i); Directory.CreateDirectory(old); Directory.SetLastWriteTimeUtc(old, start.AddHours(i)); }
        Directory.SetLastWriteTimeUtc(partial, start.AddHours(-1)); Directory.SetLastWriteTimeUtc(full, start.AddHours(-2));
        var windows = Path.Combine(exports, "clonedivers-windows"); Directory.CreateDirectory(windows); Directory.SetLastWriteTimeUtc(windows, start.AddHours(-3));
        LinuxForgePlayer.PruneExports(exports, full);
        check(Directory.Exists(full) && Directory.Exists(windows) && Directory.Exists(Path.Combine(exports, "linux-old3")) && Directory.Exists(Path.Combine(exports, "linux-old2")) &&
            Directory.EnumerateDirectories(exports, "linux-*").Count() == 3, "an applied import prunes older Linux exports to the imported copy and two recent sources");

        var freshRoot = Path.Combine(root, "fresh-forge", "pfx", "drive_c", "Forge");
        Directory.CreateDirectory(freshRoot);
        var freshPrefs = new LinuxForgePreferences { InstallRoot = freshRoot, Mode = "regular" };
        var freshPlayer = new LinuxForgePlayer(new Connection(), freshPrefs);
        freshPrefs.Mode = "clonedivers";
        var freshPlan = await freshPlayer.PlanAppearanceAsync();
        var freshExportsRoot = Path.Combine(Path.GetDirectoryName(freshRoot)!, "Clonedivers-HellForge-cache", "exports");
        check(freshPlan.Selection.Mode == freshPrefs.PackSelection().Mode && freshPlan.Missing.Count > 0 &&
            freshPlan.Output.StartsWith(Path.Combine(freshExportsRoot, "linux-"), StringComparison.Ordinal),
            "appearance planning succeeds on a fresh Forge install without a mods folder");

        var modsDir = Path.Combine(freshRoot, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllBytes(Path.Combine(modsDir, "sample.patch_0"), new byte[freshPlan.Missing.Min(f => f.Size)]);
        var withModsPlan = await freshPlayer.PlanAppearanceAsync();
        check(withModsPlan.Selection.Mode == freshPrefs.PackSelection().Mode && withModsPlan.Output != freshPlan.Output &&
            withModsPlan.Output.StartsWith(Path.Combine(freshExportsRoot, "linux-"), StringComparison.Ordinal),
            "appearance planning succeeds when a normal Forge mods folder exists");

        var fileModsRoot = Path.Combine(root, "file-mods-forge", "pfx", "drive_c", "Forge");
        Directory.CreateDirectory(fileModsRoot);
        File.WriteAllText(Path.Combine(fileModsRoot, "mods"), "not a directory");
        var fileModsPrefs = new LinuxForgePreferences { InstallRoot = fileModsRoot, Mode = "clonedivers" };
        var fileModsPlan = await new LinuxForgePlayer(new Connection(), fileModsPrefs).PlanAppearanceAsync();
        check(fileModsPlan.Selection.Mode == fileModsPrefs.PackSelection().Mode &&
            fileModsPlan.Output.StartsWith(Path.Combine(Path.GetDirectoryName(fileModsRoot)!, "Clonedivers-HellForge-cache", "exports", "linux-"), StringComparison.Ordinal),
            "appearance planning ignores a regular file at mods instead of failing");

        var v2Prefs = new LinuxForgePreferences { Mode = "clonedivers", Texture = "reduced512", EnemySkin = "mixed-droids", DeltaSquad = true, Covenant = false };
        var v2Sel = v2Prefs.PackSelection();
        check(v2Prefs.Droids && v2Prefs.EnemySkin == "mixed-droids" && v2Sel.Version == "2026.11.19-r20" && v2Sel.Texture == "reduced512" &&
            v2Sel.Options["commandos"] && v2Sel.Options["droids"] && v2Sel.Options["mixed-droids"] && v2Sel.Files.Count == 114 && v2Sel.Files.Sum(f => f.Size) == 4449229468L &&
            v2Sel.Files.Single(f => f.Name == "9ba626afa44a3aa3.patch_32").Sha256 == "68e82beabd6b8795984143b25bd2a85e46de9959d2fbac1ca277d62129b3f53e",
            "Linux v2.0 EnemySkin and reduced512 resolve natively against Clones r20 Delta Squad");
        v2Prefs.DeltaSquad = false;
        var v2NoDelta = v2Prefs.PackSelection();
        check(v2NoDelta.Mode == "clonedivers" && !v2NoDelta.Options["commandos"] && v2NoDelta.Files.Count == 114 &&
            v2NoDelta.Files.Single(f => f.Name == "9ba626afa44a3aa3.patch_32").Sha256 == "4254b1f322004651c3710f5d7b8c35c710d271d34a1f28c399ea097b07cdc746" &&
            v2NoDelta.Files.Sum(f => f.Size) - v2Sel.Files.Sum(f => f.Size) == 48579596L,
            "Linux DeltaSquad=false selects clones-r20-delta-off on patch_32 despite combinedRoster=true");
        v2Prefs.DeltaSquad = true;
        v2Prefs.EnemySkin = "native-automatons";
        check(!v2Prefs.Droids && !v2Prefs.PackSelection().Options["droids"] && v2Prefs.PackSelection().Files.Count == 102 && v2Prefs.SelectionCommand().Contains("native-automatons"),
            "Linux EnemySkin native-automatons updates Droids, selects 102 files, and propagates to select-mode arguments");

        try
        {
            var outsideMods = Path.Combine(root, "outside-mods");
            Directory.CreateDirectory(outsideMods);
            var linkModsRoot = Path.Combine(root, "link-mods-forge", "pfx", "drive_c", "Forge");
            Directory.CreateDirectory(linkModsRoot);
            Directory.CreateSymbolicLink(Path.Combine(linkModsRoot, "mods"), outsideMods);
            var rejectedLink = false;
            try { await new LinuxForgePlayer(new Connection(), new LinuxForgePreferences { InstallRoot = linkModsRoot, Mode = "clonedivers" }).PlanAppearanceAsync(); }
            catch (InvalidDataException) { rejectedLink = true; }
            check(rejectedLink, "appearance planning rejects a symlinked Forge mods folder");

            var danglingModsRoot = Path.Combine(root, "dangling-mods-forge", "pfx", "drive_c", "Forge");
            Directory.CreateDirectory(danglingModsRoot);
            Directory.CreateSymbolicLink(Path.Combine(danglingModsRoot, "mods"), Path.Combine(root, "missing-outside-mods"));
            var rejectedDangling = false;
            try { await new LinuxForgePlayer(new Connection(), new LinuxForgePreferences { InstallRoot = danglingModsRoot, Mode = "clonedivers" }).PlanAppearanceAsync(); }
            catch (InvalidDataException) { rejectedDangling = true; }
            check(rejectedDangling, "appearance planning rejects a dangling symlink at Forge mods");
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: mods symlink fixtures require Windows privileges; Linux CI must run these cases."); }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314) { Console.WriteLine("SKIP: mods symlink fixtures require Windows privileges; Linux CI must run these cases."); }
    }

    sealed class Connection : IForgeConnection
    {
        internal bool RefuseSelect, FailDispose, Running, Disposed;
        internal ForgeAccountView? Account;
        public bool Faulted { get; set; }
        internal readonly List<string[]> Calls = [], Refused = [];
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (Faulted) throw new InvalidOperationException("Forge bridge is not connected; review cleanup before reconnecting.");
            // Refused calls never reach Calls, so Calls holds only what the bridge accepted.
            if (args[0] == "select-mode" && RefuseSelect) { RefuseSelect = false; Refused.Add(args.ToArray()); throw new ForgeCommandException("Finish the running session before changing or launching Forge."); }
            Calls.Add(args.ToArray());
            object result = args[0] switch {
                "player-status" => new ForgePlayerState(new(Path.GetTempPath(), "fixture", "25480438", !Running, true, Running, true, true, "observed", null), !Running, "Fixture ready", Account, null),
                "play" => new { @event = "started" },
                _ => new { fixture = true }
            };
            if (args[0] == "play") Running = true;
            if (args[0] == "account-import") Account = new(true, true, "Opened 3 owned Warbonds.");
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
        public ValueTask DisposeAsync() { Disposed = true; return FailDispose ? ValueTask.FromException(new IOException("Official Linux wrapper ended abnormally")) : ValueTask.CompletedTask; }
    }
}
