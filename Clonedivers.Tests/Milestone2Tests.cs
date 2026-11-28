using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Clonedivers.Ui;
using Button = Avalonia.Controls.Button;
using ProgressBar = Avalonia.Controls.ProgressBar;

namespace Clonedivers.Tests;

internal static class Milestone2Tests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        Console.WriteLine("Milestone 2 (v2.1): 5 texture profiles, 3-way SquidSkin, DeathTrooperVoice, r8 catalog draft, Discord PNGs & stray pack guard");

        var dir = Path.Combine(root, "milestone2");
        Directory.CreateDirectory(dir);

        TestFiveTextureProfilesAndFallbacks(check);
        TestSquidSkinAndDeathTrooperVoice(dir, check);
        TestR8CatalogDraftAndSources(check);
        TestCheckedInDiscordHelmetPngs(check);
        await TestStrayOverhaulPackGuardAsync(dir, check);
    }

    internal static async Task RunHeadlessUiChecksAsync(string root, Action<bool, string> check)
    {
        var dir = Path.Combine(root, "milestone2-ui");
        Directory.CreateDirectory(dir);
        TestForgeShellWindowV21Controls(check);
        TestDiscordHelmetExport(dir, check);
        await TestPreWindowLaunchingUxAsync(dir, check);
    }

    static byte[] ReadResourceBytes(string logicalName)
    {
        using var stream = typeof(Milestone2Tests).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException("Missing embedded test resource: " + logicalName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    static byte[] BuildSyntheticV21ManifestBytes(bool isEmpire)
    {
        static string Hex64(char c) => new(c, 64);

        var profiles = new object[]
        {
            new { Id = "full", Name = "Full (Ultra)" },
            new { Id = "lighter", Name = "Balanced (Streamed)" },
            new { Id = "reduced1024", Name = "1024px (Medium)" },
            new { Id = "reduced512", Name = "512px (Low / Deck)" },
            new { Id = "potato256", Name = "256px + Decimated Meshes (Ultra-Potato)" }
        };

        var options = new List<object>
        {
            new { Id = "empire", Name = "Empire", Default = isEmpire },
            new { Id = "commandos", Name = "Commandos", Default = false },
            new { Id = "droids", Name = "CIS Droids", Default = true },
            new { Id = "mixed-droids", Name = "Mixed Bots + CIS", Default = true },
            new { Id = "covenant", Name = "Covenant Squids", Default = true },
            new { Id = "mixed-covenant", Name = "Mixed Covenant", Default = true }
        };
        if (isEmpire)
            options.Add(new { Id = "deathtrooper", Name = "Encrypted Death Trooper Voice", Default = true });

        int basePatch = isEmpire ? 1000 : 200;
        var modeList = isEmpire ? new[] { "empire" } : new[] { "clonedivers", "commandos" };
        var files = new List<object>();

        char shaSeed = '1';
        foreach (var prof in new[] { "full", "lighter", "reduced1024", "reduced512", "potato256" })
        {
            files.Add(new
            {
                Name = $"9ba626afa44a3aa3.patch_{basePatch}",
                Url = $"https://github.com/owendavidgoode/clonedivers/releases/download/v21-test/{prof}.patch_{basePatch}",
                Size = 1024L * (1 + (shaSeed - '0')),
                Sha256 = Hex64(shaSeed),
                Modes = modeList,
                TextureProfiles = new[] { prof }
            });
            shaSeed++;
        }

        // Enemy skin rows (all-droids vs mixed-droids)
        files.Add(new
        {
            Name = $"9ba626afa44a3aa3.patch_{basePatch + 1}",
            Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v21-test/all-droids",
            Size = 2048L,
            Sha256 = Hex64('a'),
            Option = "droids",
            UnlessOption = "mixed-droids",
            Modes = modeList
        });
        files.Add(new
        {
            Name = $"9ba626afa44a3aa3.patch_{basePatch + 1}",
            Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v21-test/mixed-droids",
            Size = 2048L,
            Sha256 = Hex64('b'),
            Option = "mixed-droids",
            Modes = modeList
        });

        // Squid skin rows (all-covenant vs mixed-covenant)
        files.Add(new
        {
            Name = $"9ba626afa44a3aa3.patch_{basePatch + 2}",
            Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v21-test/all-covenant",
            Size = 3072L,
            Sha256 = Hex64('c'),
            Option = "covenant",
            UnlessOption = "mixed-covenant",
            Modes = modeList
        });
        files.Add(new
        {
            Name = $"9ba626afa44a3aa3.patch_{basePatch + 2}",
            Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v21-test/mixed-covenant",
            Size = 3072L,
            Sha256 = Hex64('d'),
            Option = "mixed-covenant",
            Modes = modeList
        });

        if (isEmpire)
        {
            // Voice slot 4: encrypted Death Trooper vs standard trooper
            files.Add(new
            {
                Name = $"9ba626afa44a3aa3.patch_{basePatch + 3}",
                Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v21-test/deathtrooper-on",
                Size = 4096L,
                Sha256 = Hex64('e'),
                Option = "deathtrooper",
                Modes = modeList
            });
            files.Add(new
            {
                Name = $"9ba626afa44a3aa3.patch_{basePatch + 3}",
                Url = "https://github.com/owendavidgoode/clonedivers/releases/download/v21-test/deathtrooper-off",
                Size = 4096L,
                Sha256 = Hex64('f'),
                UnlessOption = "deathtrooper",
                Modes = modeList
            });
        }

        var manifest = new
        {
            Format = 3,
            Pack = new
            {
                Version = isEmpire ? "2026.11.22-r49" : "2026.11.22-r21",
                GameBuild = HellForgePack.SupportedBuild,
                TextureProfiles = profiles,
                Options = options,
                Files = files
            }
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, HellForgePack.Json));
    }

    static void TestFiveTextureProfilesAndFallbacks(Action<bool, string> check)
    {
        var allFive = new[] { "full", "lighter", "reduced1024", "reduced512", "potato256" };
        foreach (var tex in allFive)
        {
            var prefs = new HellForgePreferences { Texture = tex, AutoPerformanceTier = false, PerformanceTier = PerformanceTierAdvisor.TierFromPackTexture(tex) };
            prefs.Validate();
        }
        bool rejectedUnknown = false;
        try { new HellForgePreferences { Texture = "ultra-4k" }.Validate(); } catch (InvalidDataException) { rejectedUnknown = true; }
        check(rejectedUnknown, "HellForgePreferences.Validate accepts all 5 pack texture profiles and rejects unknown profiles");

        check(PerformanceTierAdvisor.ResolvePackTexture("ultra") == "full" &&
              PerformanceTierAdvisor.ResolvePackTexture("high") == "lighter" &&
              PerformanceTierAdvisor.ResolvePackTexture("balanced") == "reduced1024" &&
              PerformanceTierAdvisor.ResolvePackTexture("low-512") == "reduced512" &&
              PerformanceTierAdvisor.ResolvePackTexture("ultra-potato") == "potato256" &&
              PerformanceTierAdvisor.TierFromPackTexture("full") == "ultra" &&
              PerformanceTierAdvisor.TierFromPackTexture("lighter") == "high" &&
              PerformanceTierAdvisor.TierFromPackTexture("reduced1024") == "balanced" &&
              PerformanceTierAdvisor.TierFromPackTexture("reduced512") == "low-512" &&
              PerformanceTierAdvisor.TierFromPackTexture("potato256") == "ultra-potato",
              "PerformanceTierAdvisor maps all 5 tiers 1:1 with full, lighter, reduced1024, reduced512, and potato256");

        // r20 / r48 backwards-compatible fallback: reduced1024 -> lighter, potato256 -> reduced512
        var r20Bytes = ReadResourceBytes("Clonedivers.Tests.Clones.R20.Manifest.json");
        var r48Bytes = ReadResourceBytes("Clonedivers.Tests.Empire.R48.Manifest.json");
        foreach (var mode in new[] { "clonedivers", "commandos", "empire" })
        {
            var manifestBytes = mode == "empire" ? r48Bytes : r20Bytes;
            var sel1024 = HellForgePack.ResolveSelection(manifestBytes, null, mode, "reduced1024");
            var selLighter = HellForgePack.ResolveSelection(manifestBytes, null, mode, "lighter");
            var sel256 = HellForgePack.ResolveSelection(manifestBytes, null, mode, "potato256");
            var sel512 = HellForgePack.ResolveSelection(manifestBytes, null, mode, "reduced512");

            check(sel1024.Texture == "lighter" &&
                  sel1024.Files.Select(f => f.Sha256).SequenceEqual(selLighter.Files.Select(f => f.Sha256)) &&
                  sel256.Texture == "reduced512" &&
                  sel256.Files.Select(f => f.Sha256).SequenceEqual(sel512.Files.Select(f => f.Sha256)),
                  $"ResolveSelection({mode}) falls back seamlessly on r20/r48: reduced1024 -> lighter and potato256 -> reduced512");
        }

        // Live embedded r25 & r54 matrix (90 Clones r25 + 45 Empire r54 + 45 Empire legacy deathtrooper=true stripped/forced-false check = 180)
        int matrixChecked = 0;
        foreach (var mode in new[] { "clonedivers", "commandos", "empire" })
        foreach (var tex in allFive)
        foreach (var enemy in new[] { "all-droids", "mixed-droids", "native-automatons" })
        foreach (var squid in new[] { "mixed-covenant", "all-covenant", "native-squids" })
        foreach (var dtCallerFlag in mode == "empire" ? new[] { true, false } : new[] { false })
        {
            var opts = new Dictionary<string, bool>
            {
                ["droids"] = enemy != "native-automatons",
                ["mixed-droids"] = enemy == "mixed-droids",
                ["covenant"] = squid != "native-squids",
                ["mixed-covenant"] = squid == "mixed-covenant"
            };
            if (mode == "empire" && dtCallerFlag) opts["deathtrooper"] = true;
            var sel = HellForgePack.PinnedSelection(mode, tex, opts);
            var expectedVer = mode == "empire" ? "2026.11.28-r55" : "2026.11.28-r26";
            var expectedCount = mode == "empire"
                ? (enemy == "native-automatons" ? 132 : 144)
                : (enemy == "native-automatons" ? 129 : 144);
            bool dtNeverEnabled = !sel.Options.TryGetValue("deathtrooper", out var dtVal) || !dtVal;
            if (sel.Version != expectedVer || sel.Texture != tex || sel.Files.Count != expectedCount || !dtNeverEnabled)
                check(false, $"PinnedSelection {mode}/{tex}/{enemy}/{squid}/dt={dtCallerFlag} mismatch");
            matrixChecked++;
        }
        check(matrixChecked == 180, "PinnedSelection resolves all 180 r26/r55 combinations natively and strips/disables deathtrooper even if caller passes true");

        // r19 / r47 fallback: both reduced1024 and potato256 fall back to lighter when reduced512 is absent
        var r19Bytes = ReadResourceBytes("Clonedivers.Tests.Clones.R19.Manifest.json");
        var r47Bytes = ReadResourceBytes("Clonedivers.Tests.Empire.R47.Manifest.json");
        var r47Lighter = ReadResourceBytes("Clonedivers.Tests.Empire.R47.Lighter.Manifest.json");
        check(HellForgePack.ResolveSelection(r19Bytes, null, "clonedivers", "reduced1024").Texture == "lighter" &&
              HellForgePack.ResolveSelection(r19Bytes, null, "clonedivers", "potato256").Texture == "lighter" &&
              HellForgePack.ResolveSelection(r47Bytes, r47Lighter, "empire", "reduced1024").Texture == "lighter" &&
              HellForgePack.ResolveSelection(r47Bytes, r47Lighter, "empire", "potato256").Texture == "lighter",
              "ResolveSelection falls back to lighter on legacy r19/r47 manifests lacking reduced1024/reduced512/potato256");

        // Synthetic r21 / r49 5-profile manifests resolve all 5 profiles natively with distinct payloads
        foreach (var (isEmpire, mode) in new[] { (false, "clonedivers"), (false, "commandos"), (true, "empire") })
        {
            var bytes = BuildSyntheticV21ManifestBytes(isEmpire);
            var seenShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tex in allFive)
            {
                var sel = HellForgePack.ResolveSelection(bytes, null, mode, tex);
                check(sel.Texture == tex, $"r21/r49 {mode} resolves native texture profile '{tex}' without fallback");
                seenShas.Add(sel.Files[0].Sha256);
            }
            check(seenShas.Count == 5, $"r21/r49 {mode} selects 5 distinct base texture payloads across the 5 tiers");
        }

        // Customize tier descriptions exact 5 labels (L1: Row 2 High -> "High (Streamed)")
        var desc = ForgeShellWindow.CustomizeTierDescriptions;
        check(desc.Count == 5 &&
              desc[PerformanceTierAdvisor.Ultra] == "Full (Ultra)" &&
              desc[PerformanceTierAdvisor.High] == "High (Streamed)" &&
              desc[PerformanceTierAdvisor.Balanced] == "1024px (Medium)" &&
              desc[PerformanceTierAdvisor.Low512] == "512px (Low / Deck)" &&
              desc[PerformanceTierAdvisor.UltraPotato] == "256px + Decimated Meshes (Ultra-Potato)",
              "ForgeShellWindow.CustomizeTierDescriptions exposes the exact 5 v2.1 tier labels without Balanced ambiguity (L1)");
    }

    static void TestSquidSkinAndDeathTrooperVoice(string dir, Action<bool, string> check)
    {
        // Brand-new defaults & Death Trooper Voice complete removal
        var fresh = HellForgePreferences.Load(Path.Combine(dir, "fresh-v21", "hellforge.json"));
        check(fresh.SquidSkin == "mixed-covenant" && fresh.Covenant && fresh.EnemySkin == "mixed-droids" &&
              typeof(HellForgePreferences).GetProperty("DeathTrooperVoice") == null &&
              typeof(ForgeHomeState).GetProperty("DeathTrooperVoice") == null,
            "brand-new HellForgePreferences defaults to mixed-droids + mixed-covenant and DeathTrooperVoice property is completely removed");

        var hw = new HardwarePerformanceRecommendation("NVIDIA GeForce RTX 4090", 24.0, 64.0, "ultra", "test");
        check(ForgeTacticalCopy.FormatLoadoutSummary("clonedivers", fresh.EnemySkin, fresh.DeltaSquad, fresh.Covenant,
                  true, "auto", hw, squidSkin: fresh.SquidSkin) == "Full Star Wars Mix · Auto (Ultra)" &&
              ForgeTacticalCopy.FormatLoadoutSummary("empire", fresh.EnemySkin, false, fresh.Covenant,
                  true, "auto", hw, squidSkin: fresh.SquidSkin) == "Full Star Wars Mix · Auto (Ultra)" &&
              !ForgeTacticalCopy.FormatLoadoutSummary("empire", "all-droids", false, fresh.Covenant,
                  true, "auto", hw, squidSkin: fresh.SquidSkin).Contains("Death Trooper", StringComparison.OrdinalIgnoreCase),
              "FormatLoadoutSummary treats mixed-droids + mixed-covenant as 'Full Star Wars Mix' with zero Death Trooper voice text");

        // Backwards-compatible legacy JSON loading (with legacy DeathTrooperVoice=true ignored & stripped on save)
        var covTruePath = Path.Combine(dir, "legacy-cov-true.json");
        File.WriteAllText(covTruePath, """{"Format":1,"Mode":"empire","Covenant":true,"DeathTrooperVoice":true,"AppliedSelection":"r54/25480438/empire/full/mixed-droids/mixed-covenant/deathtrooper"}""");
        var loadedCovTrue = HellForgePreferences.Load(covTruePath);
        check(loadedCovTrue.SquidSkin == "all-covenant" && loadedCovTrue.Covenant &&
              loadedCovTrue.EnemySkin == "mixed-droids" &&
              !loadedCovTrue.SelectionKey.Contains("deathtrooper", StringComparison.OrdinalIgnoreCase) &&
              loadedCovTrue.AppliedSelection != loadedCovTrue.SelectionKey &&
              !loadedCovTrue.Selection().Options.GetValueOrDefault("deathtrooper"),
            "upgrading legacy hellforge.json with DeathTrooperVoice=true strips deathtrooper from SelectionKey/Selection so old pack re-syncs cleanly");

        var covFalsePath = Path.Combine(dir, "legacy-cov-false.json");
        File.WriteAllText(covFalsePath, """{"Format":1,"Mode":"clonedivers","Covenant":false,"HostTheaterDefaultVersion":4}""");
        var loadedCovFalse = HellForgePreferences.Load(covFalsePath);
        check(loadedCovFalse.SquidSkin == "native-squids" && !loadedCovFalse.Covenant,
            "v4+ hellforge.json with Covenant=false (no SquidSkin) maps cleanly to SquidSkin='native-squids'");

        // M4: Upgrading v2.0.3 manual PerformanceTier ('balanced' + 'lighter', 'ultra-potato' + 'reduced512') reconciles PerformanceTier to active Texture
        var m4BalancedPath = Path.Combine(dir, "v203-manual-balanced.json");
        File.WriteAllText(m4BalancedPath, """{"Format":1,"Mode":"clonedivers","AutoPerformanceTier":false,"PerformanceTier":"balanced","Texture":"lighter"}""");
        var loadedM4Balanced = HellForgePreferences.Load(m4BalancedPath);
        var m4PotatoPath = Path.Combine(dir, "v203-manual-potato.json");
        File.WriteAllText(m4PotatoPath, """{"Format":1,"Mode":"empire","AutoPerformanceTier":false,"PerformanceTier":"ultra-potato","Texture":"reduced512"}""");
        var loadedM4Potato = HellForgePreferences.Load(m4PotatoPath);
        bool rejectedCorruptTierJson = false;
        var m4CorruptPath = Path.Combine(dir, "v203-corrupt-tier.json");
        File.WriteAllText(m4CorruptPath, """{"Format":1,"Mode":"empire","AutoPerformanceTier":false,"PerformanceTier":"bogus-tier","Texture":"full"}""");
        try { HellForgePreferences.Load(m4CorruptPath); } catch (InvalidDataException) { rejectedCorruptTierJson = true; }
        check(!loadedM4Balanced.AutoPerformanceTier && loadedM4Balanced.Texture == "lighter" && loadedM4Balanced.PerformanceTier == "high" &&
              !loadedM4Potato.AutoPerformanceTier && loadedM4Potato.Texture == "reduced512" && loadedM4Potato.PerformanceTier == "low-512" &&
              rejectedCorruptTierJson,
              "M4: HellForgePreferences.Load reconciles v2.0.3 manual PerformanceTier with saved Texture while rejecting corrupt tier strings");

        // Explicit SquidSkin JSON round-trip across all 3 SquidSkin values on v4+
        foreach (var sq in new[] { "mixed-covenant", "all-covenant", "native-squids" })
        {
            var rtPath = Path.Combine(dir, $"rt-{sq}.json");
            var p = new HellForgePreferences { Mode = "empire", SquidSkin = sq };
            p.Save(rtPath);
            var savedText = File.ReadAllText(rtPath);
            var back = HellForgePreferences.Load(rtPath);
            check(back.SquidSkin == sq &&
                  back.Covenant == (sq != "native-squids") &&
                  !savedText.Contains("DeathTrooper", StringComparison.OrdinalIgnoreCase),
                  $"HellForgePreferences round-trips SquidSkin='{sq}' on v4+ without writing DeathTrooperVoice");
        }

        bool rejectedBadSquid = false;
        try { new HellForgePreferences { SquidSkin = "flood" }.Validate(); } catch (InvalidDataException) { rejectedBadSquid = true; }
        check(rejectedBadSquid, "HellForgePreferences.Validate rejects unsupported SquidSkin values");

        // SelectionKey invalidation for SquidSkin across Empire
        var empDefault = new HellForgePreferences { Mode = "empire", Texture = "full", SquidSkin = "mixed-covenant" };
        var empAllCov = new HellForgePreferences { Mode = "empire", Texture = "full", SquidSkin = "all-covenant" };
        var empNative = new HellForgePreferences { Mode = "empire", Texture = "full", SquidSkin = "native-squids" };
        check(new[] { empDefault.SelectionKey, empAllCov.SelectionKey, empNative.SelectionKey }.Distinct(StringComparer.Ordinal).Count() == 3 &&
              !empDefault.SelectionKey.Contains("deathtrooper", StringComparison.OrdinalIgnoreCase),
            "Empire SelectionKey distinguishes mixed-covenant, all-covenant, and native-squids with no /deathtrooper suffix");

        // r20 / r48 fallback: mixed-covenant falls back cleanly to covenant=true, and deathtrooper is always stripped/disabled
        var r20Bytes = ReadResourceBytes("Clonedivers.Tests.Clones.R20.Manifest.json");
        var r48Bytes = ReadResourceBytes("Clonedivers.Tests.Empire.R48.Manifest.json");
        var r20MixedCov = HellForgePack.ResolveSelection(r20Bytes, null, "clonedivers", "full",
            new Dictionary<string, bool> { ["droids"] = true, ["mixed-droids"] = true, ["covenant"] = true, ["mixed-covenant"] = true });
        var r48MixedCovDtOff = HellForgePack.ResolveSelection(r48Bytes, null, "empire", "full",
            new Dictionary<string, bool> { ["droids"] = true, ["mixed-droids"] = true, ["covenant"] = true, ["mixed-covenant"] = true, ["deathtrooper"] = true });
        var r21Live = new HellForgePreferences { Mode = "clonedivers", SquidSkin = "mixed-covenant" }.Selection();
        var r49LiveDtOff = new HellForgePreferences { Mode = "empire", SquidSkin = "mixed-covenant" }.Selection();
        check(r20MixedCov.Options["covenant"] && !r20MixedCov.Options.ContainsKey("mixed-covenant") &&
              r48MixedCovDtOff.Options["covenant"] && !r48MixedCovDtOff.Options.ContainsKey("deathtrooper") &&
              r21Live.Options["mixed-covenant"] && r21Live.Options["covenant"] &&
              r49LiveDtOff.Options["mixed-covenant"] && !r49LiveDtOff.Options.GetValueOrDefault("deathtrooper"),
              "r20/r48 fall back cleanly while live embedded r26/r55 resolve mixed-covenant and never enable deathtrooper");

        // Synthetic r49 3-way SquidSkin + forced deathtrooper=false verification (even when manifest Default=true and caller passes deathtrooper=true!)
        var r49Bytes = BuildSyntheticV21ManifestBytes(isEmpire: true);
        var r49Feed = Manifest.Parse(Encoding.UTF8.GetString(r49Bytes));
        PackProfiles.Validate(r49Feed.Pack!);
        check(!PackExtras.Visible(r49Feed.Pack!).Any(o => o.Id is "mixed-covenant" or "deathtrooper"),
              "r49 PackProfiles.Validate accepts mixed-covenant and PackExtras hides deathtrooper");

        var r49Mixed = HellForgePack.ResolveSelection(r49Bytes, null, "empire", "full",
            new Dictionary<string, bool> { ["droids"] = true, ["mixed-droids"] = true, ["covenant"] = true, ["mixed-covenant"] = true, ["deathtrooper"] = true });
        var r49AllCov = HellForgePack.ResolveSelection(r49Bytes, null, "empire", "full",
            new Dictionary<string, bool> { ["droids"] = true, ["mixed-droids"] = true, ["covenant"] = true, ["mixed-covenant"] = false, ["deathtrooper"] = false });
        var r49Native = HellForgePack.ResolveSelection(r49Bytes, null, "empire", "full",
            new Dictionary<string, bool> { ["droids"] = true, ["mixed-droids"] = true, ["covenant"] = false, ["mixed-covenant"] = false, ["deathtrooper"] = true });

        check(r49Mixed.Files.Any(f => f.Sha256 == new string('d', 64)) &&
              !r49Mixed.Files.Any(f => f.Sha256 == new string('c', 64)) &&
              !r49Mixed.Files.Any(f => f.Sha256 == new string('e', 64)) &&
              r49Mixed.Files.Any(f => f.Sha256 == new string('f', 64)) &&
              r49AllCov.Files.Any(f => f.Sha256 == new string('c', 64)) &&
              !r49AllCov.Files.Any(f => f.Sha256 == new string('d', 64)) &&
              r49AllCov.Files.Any(f => f.Sha256 == new string('f', 64)) &&
              !r49Native.Files.Any(f => f.Sha256 == new string('c', 64) || f.Sha256 == new string('d', 64)) &&
              r49Native.Files.Any(f => f.Sha256 == new string('f', 64)) &&
              r49Mixed.Files.All(f => f.Name.Contains(".patch_100", StringComparison.Ordinal)),
              "ResolveSelection selects mixed-covenant vs all-covenant vs native-squids and strictly forces deathtrooper=false (standard Imperial voice) even when requested true");
    }

    static void TestForgeShellWindowV21Controls(Action<bool, string> check)
    {
        // Headless Avalonia ForgeShellWindow verification of 3-pill SquidSkin, DeathTrooperVoice removal, and 5-button Step 4
        var previewHost = new PreviewForgeHost("customize");
        var win = new ForgeShellWindow(previewHost, "customize");
        check(!win.IsAdvancedExpanded, "Customize drawer keeps Advanced section collapsed by default in v2.1");

        const BindingFlags priv = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ForgeShellWindow).GetField("showingCustomize", priv)!.SetValue(win, true);
        typeof(ForgeShellWindow).GetMethod("RefreshAll", priv)!.Invoke(win, null);

        var squidPills = (Dictionary<string, ToggleButton>)typeof(ForgeShellWindow).GetField("squidPills", priv)!.GetValue(win)!;
        var warPills = (Dictionary<string, ToggleButton>)typeof(ForgeShellWindow).GetField("warPills", priv)!.GetValue(win)!;
        var deltaRow = (Border)typeof(ForgeShellWindow).GetField("deltaRow", priv)!.GetValue(win)!;
        var extrasLabel = (TextBlock)typeof(ForgeShellWindow).GetField("extrasHeaderLabel", priv)!.GetValue(win)!;
        var hasDeathTrooperField = typeof(ForgeShellWindow).GetField("deathTrooperRow", priv) != null ||
                                   typeof(ForgeShellWindow).GetField("deathTrooperToggle", priv) != null;
        var step4Tex = (Dictionary<string, Button>)typeof(ForgeShellWindow).GetField("setupStep4TextureButtons", priv)!.GetValue(win)!;

        check(squidPills.Count == 3 &&
              squidPills.ContainsKey("mixed-covenant") && squidPills["mixed-covenant"].IsChecked == true &&
              squidPills.ContainsKey("all-covenant") && squidPills.ContainsKey("native-squids") &&
              warPills.Count == 4 && warPills["hellforge"].IsChecked == true &&
              warPills.ContainsKey("open") && warPills.ContainsKey("live") && warPills.ContainsKey("custom") &&
              deltaRow.IsVisible && extrasLabel.IsVisible && !hasDeathTrooperField &&
              step4Tex.Count == 5 && step4Tex.ContainsKey("reduced1024") && step4Tex.ContainsKey("potato256"),
              "ForgeShellWindow Customize renders 3-pill Squid Skin, 4-pill Galactic War, Clones Delta Squad, 5 Step-4 texture buttons, and zero Death Trooper controls");

        previewHost.SetGalacticWarAsync("open").GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        check(warPills["open"].IsChecked == true && warPills["hellforge"].IsChecked != true,
            "selecting All Planets Open updates Galactic War pills in Customize -> Advanced");

        previewHost.SelectModeAsync("empire").GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        check(!deltaRow.IsVisible && !extrasLabel.IsVisible,
            "switching to Empire hides Clones-only EXTRAS header and Delta Squad with no Death Trooper toggle");
        win.Close();

        // HellForge 0.5 Step 3 'Play Now, Sync Later' secondary action advances directly to Step 4
        var step3Host = new PreviewForgeHost("setup-step3-sync");
        var step3Win = new ForgeShellWindow(step3Host, "setup");
        step3Host.EnterSetupAsync().GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var skipBtn = (Button)typeof(ForgeShellWindow).GetField("setupSkipSyncButton", priv)!.GetValue(step3Win)!;
        check(skipBtn.IsVisible && skipBtn.IsEnabled && skipBtn.Content?.ToString() == "Play Now, Sync Later",
            "Guided Setup Step 3 renders enabled 'Play Now, Sync Later' secondary button on HellForge 0.5");
        step3Host.SkipSetupAccountSyncAsync().GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        check(step3Host.GetSetupState().Step == 4 && !skipBtn.IsVisible,
            "clicking 'Play Now, Sync Later' advances setup to Step 4 and hides the Step 3 skip button");
        step3Win.Close();
    }

    static void TestR8CatalogDraftAndSources(Action<bool, string> check)
    {
        check(HellForgeGameplayCatalog.Pinned.Id == "build25480438-r17" &&
              HellForgeGameplayCatalog.Current.Id == "build25480438-r17" &&
              HellForgeGameplayCatalog.Pinned.Artifacts.Count == 19,
              "HellForgeGameplayCatalog activates build25480438-r17 (19 artifacts) as the pinned default");

        var r17Json = Encoding.UTF8.GetString(ReadResourceBytes("Clonedivers.Forge.Gameplay.json"));
        var r17 = JsonSerializer.Deserialize<HellForgeGameplayCatalog>(r17Json, HellForgePack.Json)!;
        r17.ValidateDraft();

        check(r17.Id == "build25480438-r17" &&
              r17.GameBuild == HellForgePack.SupportedBuild &&
              r17.Artifacts.Count == 19 &&
              r17.Order.Count == 22 &&
              r17.Order[^1] == HellForgePreset.BingusSharedLoader &&
              !r17.Artifacts.Any(a => a.Url.Contains("hf-mod.com", StringComparison.OrdinalIgnoreCase)),
              "forge-gameplay-build25480438-r17.json passes ValidateDraft with 19 artifacts, 22 ordered IDs, zero hf-mod.com URLs, and Bingus Shared Loader last");

        // Verify all 11 updated/new v2.1..v2.3.0 code-mod manifests match r17 IDs, versions, and co-op/gameplay flags
        var expectedMods = new (string Resource, string Id, bool ExpectedGameplay, bool ExpectedOptional)[]
        {
            ("Clonedivers.Tests.CleanExit.json", HellForgePreset.CleanExit, false, true),
            ("Clonedivers.Tests.LegoCompactLeafWriter.json", HellForgePreset.LegoCompactLeafWriter, false, true),
            ("Clonedivers.Tests.EmpireLegoYoda.json", HellForgePreset.EmpireLegoYoda, false, true),
            ("Clonedivers.Tests.EmpireAtstWriter.json", HellForgePreset.EmpireAtstWriter, false, true),
            ("Clonedivers.Tests.EmpireTieSwitch.json", HellForgePreset.EmpireTieSwitch, false, true),
            ("Clonedivers.Tests.EmpireCameraPullback.json", HellForgePreset.EmpireCameraPullback, false, true),
            ("Clonedivers.Tests.TitanfallWalkerHud.json", HellForgePreset.TitanfallWalkerHud, false, true),
            ("Clonedivers.Tests.StarWarsLocalization.json", HellForgePreset.StarWarsLocalization, false, true),
            ("Clonedivers.Tests.DedicatedMeleeSlot.json", HellForgePreset.DedicatedMeleeSlot, false, true),
            ("Clonedivers.Tests.FasterCorpseCleanup.json", HellForgePreset.FasterCorpseCleanup, true, false),
            ("Clonedivers.Tests.HeavySeafPresence.json", "local.heavy-seaf-presence", true, false),
            ("Clonedivers.Tests.ChaosInvasions.json", HellForgePreset.ChaosInvasions, true, true)
        };

        foreach (var (resName, id, expectedGameplay, expectedOptional) in expectedMods)
        {
            var art = r17.Artifacts.Single(a => a.Id == id);
            using var doc = JsonDocument.Parse(ReadResourceBytes(resName));
            var rootEl = doc.RootElement;
            check(rootEl.GetProperty("id").GetString() == id &&
                  rootEl.GetProperty("version").GetString() == art.Version &&
                  rootEl.GetProperty("gameplay").GetBoolean() == expectedGameplay &&
                  art.Optional == expectedOptional &&
                  (art.Url.Contains("/releases/download/forge-mods-2026-11-22/", StringComparison.Ordinal) ||
                   art.Url.Contains("/releases/download/forge-mods-2026-11-23/", StringComparison.Ordinal) ||
                   art.Url.Contains("/releases/download/forge-mods-2026-11-25/", StringComparison.Ordinal) ||
                   art.Url.Contains("/releases/download/forge-mods-2026-11-26/", StringComparison.Ordinal) ||
                   art.Url.Contains("/releases/download/forge-mods-2026-11-27/", StringComparison.Ordinal) ||
                   art.Url.Contains("/releases/download/forge-mods-2026-11-28/", StringComparison.Ordinal)) &&
                  art.Bytes > 0 &&
                  FileSafety.IsSha256(art.Sha256) &&
                  FileSafety.IsSha256(art.ContentHash),
                  $"r17 artifact '{id}' ({art.Version}) matches forge-mods hellforge-mod.json, targets forge-mods-2026-11-22..28, and pins valid hashes");
        }

        // SquadFingerprint client-only vs gameplay classification for r17 mods
        var camMod = new HellForgeMod(HellForgePreset.EmpireCameraPullback, "Empire & Clone Camera Pullback", true, "f", []);
        var hudMod = new HellForgeMod(HellForgePreset.TitanfallWalkerHud, "AT-TE Titanfall Tactical Cockpit HUD", true, "f", []);
        var meleeMod = new HellForgeMod(HellForgePreset.DedicatedMeleeSlot, "Dedicated Melee Slot", true, "f", []);
        var corpseMod = new HellForgeMod(HellForgePreset.FasterCorpseCleanup, "Faster Corpse Cleanup", true, "f", ["gameplay"]);
        check(!SquadFingerprint.IsGameplayMod(camMod) &&
              !SquadFingerprint.IsGameplayMod(hudMod) &&
              !SquadFingerprint.IsGameplayMod(meleeMod) &&
              SquadFingerprint.IsGameplayMod(corpseMod) &&
              SquadFingerprint.Compute(r17).StartsWith("r17-", StringComparison.Ordinal),
              "SquadFingerprint excludes per-player camera pullback, walker HUD & melee slot from squad hash while including gameplay corpse cleanup");
    }

    static void TestDiscordHelmetExport(string dir, Action<bool, string> check)
    {
        var exportDir = Path.Combine(dir, "discord-icons");
        ForgeTheme.ExportDiscordHelmetIcons(exportDir, 512);

        foreach (var name in new[] { "clone-helmet.png", "empire-helmet.png" })
        {
            var generated = File.ReadAllBytes(Path.Combine(exportDir, name));
            CheckPng512RgbaTransparentCorner(generated, name + " (generated)", check);
        }
    }

    static void TestCheckedInDiscordHelmetPngs(Action<bool, string> check)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot != null)
        {
            foreach (var name in new[] { "clone-helmet.png", "empire-helmet.png" })
            {
                var repoPng = Path.Combine(repoRoot, "docs", "discord-icons", name);
                check(File.Exists(repoPng), $"docs/discord-icons/{name} exists in repository");
                if (File.Exists(repoPng))
                    CheckPng512RgbaTransparentCorner(File.ReadAllBytes(repoPng), $"docs/discord-icons/{name}", check);
            }
            var readmePath = Path.Combine(repoRoot, "docs", "discord-icons", "README.md");
            check(File.Exists(readmePath) &&
                  File.ReadAllText(readmePath).Contains("clone-helmet", StringComparison.Ordinal) &&
                  File.ReadAllText(readmePath).Contains("empire-helmet", StringComparison.Ordinal),
                  "docs/discord-icons/README.md documents clone-helmet and empire-helmet Discord portal upload keys");
        }
    }

    static void CheckPng512RgbaTransparentCorner(byte[] pngBytes, string label, Action<bool, string> check)
    {
        bool validHeader = pngBytes.Length > 1024 &&
                           pngBytes[0] == 0x89 && pngBytes[1] == 0x50 && pngBytes[2] == 0x4E && pngBytes[3] == 0x47 &&
                           Encoding.ASCII.GetString(pngBytes, 12, 4) == "IHDR";
        uint width = validHeader ? BinaryPrimitives.ReadUInt32BigEndian(pngBytes.AsSpan(16, 4)) : 0;
        uint height = validHeader ? BinaryPrimitives.ReadUInt32BigEndian(pngBytes.AsSpan(20, 4)) : 0;
        byte bitDepth = validHeader ? pngBytes[24] : (byte)0;
        byte colorType = validHeader ? pngBytes[25] : (byte)0; // 6 = RGBA

        using var ms = new MemoryStream(pngBytes);
        using var bmp = new System.Drawing.Bitmap(ms);
        var topLeft = bmp.GetPixel(0, 0);
        var center = bmp.GetPixel(256, 256);

        check(validHeader && width == 512 && height == 512 && bitDepth == 8 && colorType == 6 &&
              topLeft.A == 0 && center.A == 255,
              $"{label} is a crisp 512x512 8-bit RGBA PNG with transparent outer corners and opaque center emblem");
    }

    static string? FindRepoRoot()
    {
        var cur = new DirectoryInfo(AppContext.BaseDirectory);
        while (cur != null)
        {
            if (Directory.Exists(Path.Combine(cur.FullName, "docs", "discord-icons")) &&
                Directory.Exists(Path.Combine(cur.FullName, "forge-mods")))
                return cur.FullName;
            cur = cur.Parent;
        }
        return null;
    }

    static async Task TestStrayOverhaulPackGuardAsync(string dir, Action<bool, string> check)
    {
        var strayDemo = new HellForgeMod(
            "pack.ef3458f5-f409-4f24-8933-05fdc10a2101",
            "EmpireDivers (2.1 Live Demo)",
            true,
            Path.Combine(dir, "mods", "stray-demo"),
            ["visual"]);
        var customNamedClone = new HellForgeMod(
            "pack.11111111-2222-3333-4444-555555555555",
            "Clonedivers Custom Test",
            true,
            Path.Combine(dir, "mods", "custom-clone"),
            []);
        var officialClones = new HellForgeMod(
            HellForgePack.PackId("clonedivers"),
            "Clonedivers",
            true,
            Path.Combine(dir, "mods", "official-clones"),
            []);
        var unrelatedGameplayPack = new HellForgeMod(
            HellForgePreset.BingusSharedLoader,
            "Bingus Shared Loader - v20",
            true,
            Path.Combine(dir, "mods", "bingus"),
            []);

        check(HellForgePack.IsStarWarsOverhaulMod(strayDemo) &&
              HellForgePack.IsStarWarsOverhaulMod(customNamedClone) &&
              HellForgePack.IsStarWarsOverhaulMod(officialClones) &&
              !HellForgePack.IsStarWarsOverhaulMod(unrelatedGameplayPack),
              "HellForgePack.IsStarWarsOverhaulMod catches official, stray GUID-family, and Clonedivers/EmpireDivers-named overhaul packs without touching gameplay packs (#66)");

        var clonesSel = HellForgePack.PinnedSelection("clonedivers", "full");
        var overlappingCatalog = new HellForgeMods([officialClones, strayDemo, unrelatedGameplayPack], [], null);

        bool rejectedSelectedOverlap = false;
        try { HellForgeClient.RequireSelected(overlappingCatalog, clonesSel); }
        catch (InvalidDataException) { rejectedSelectedOverlap = true; }

        bool rejectedRegularStray = false;
        try { HellForgeClient.RequireRegular(new HellForgeMods([strayDemo, unrelatedGameplayPack], [], null)); }
        catch (InvalidDataException) { rejectedRegularStray = true; }

        check(rejectedSelectedOverlap && rejectedRegularStray,
              "RequireSelected and RequireRegular reject launching when a stray demo EmpireDivers/Clonedivers overhaul pack remains enabled (#66)");

        // Verify ConfigureAsync (via PrepareRegularAsync) actively disables the stray demo pack via mod-switch --on no!
        var install = Path.Combine(dir, "stray-pack-install", "HellForge");
        Directory.CreateDirectory(Path.Combine(install, "app", "cli"));
        Directory.CreateDirectory(Path.Combine(install, "mods", "stray-demo"));
        File.WriteAllText(Path.Combine(install, "hellforge-install.json"), """{"schemaVersion":1,"buildId":"25480438"}""");
        File.WriteAllText(Path.Combine(install, "HellForge.exe"), "stub");
        File.WriteAllText(Path.Combine(install, "app", "cli", "HellForge.Cli.exe"), "stub");
        File.WriteAllText(Path.Combine(install, "app", "app-build.json"), """{"version":"0.5.0-beta","engine":"0.1.58"}""");

        var fakeCmds = new StrayPackCommands(install, strayDemo with { Folder = Path.Combine(install, "mods", "stray-demo") });
        var client = new HellForgeClient(install, fakeCmds) { RequireCliQuiescent = () => { } };
        await client.PrepareRegularAsync();

        check(!fakeCmds.StrayEnabled &&
              fakeCmds.SwitchOffIds.Contains(strayDemo.Id),
              "ConfigureAsync disables stray demo EmpireDivers pack (pack.ef3458f5-f409-4f24-8933-05fdc10a2101) without deleting its custom folder (#66)");

        // M1: StatusAsync ignores session.pid (runner PID) and only reports GameLaunched from gameLaunched / gamePid
        fakeCmds.Running = true;
        fakeCmds.SessionRunnerPid = 4242;
        fakeCmds.SessionGamePid = null;
        var stRunnerOnly = await client.StatusAsync();
        fakeCmds.SessionGamePid = 9999;
        var stGamePid = await client.StatusAsync();
        fakeCmds.Running = false;
        check(stRunnerOnly.Running && stRunnerOnly.GameLaunched == null &&
              stGamePid.Running && stGamePid.GameLaunched == true,
              "M1: HellForgeClient.StatusAsync ignores session.pid runner PID and sets GameLaunched=true only for gameLaunched/gamePid");

        // M2: --classic HellForgeForm keeps marquee progress visible, guards OnFormClosing while awaitingGameWindow, and clears progress.Visible afterwards
        using (var classicForm = new HellForgeForm(new HellForgePreferences { InstallRoot = install, Mode = "regular" }))
        {
            const BindingFlags fPriv = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(HellForgeForm).GetField("lastStatus", fPriv)!.SetValue(classicForm,
                new HellForgeStatus(install, "0.5.0-beta", HellForgePack.SupportedBuild, true, true, Running: true, true, true, null, null, GameLaunched: null));
            typeof(HellForgeForm).GetField("awaitingGameWindow", fPriv)!.SetValue(classicForm, true);
            var msgLbl = (System.Windows.Forms.Label)typeof(HellForgeForm).GetField("message", fPriv)!.GetValue(classicForm)!;
            var progBar = (System.Windows.Forms.ProgressBar)typeof(HellForgeForm).GetField("progress", fPriv)!.GetValue(classicForm)!;
            var actPanel = (System.Windows.Forms.Control)typeof(HellForgeForm).GetField("activity", fPriv)!.GetValue(classicForm)!;
            var laterBtn = (System.Windows.Forms.Button)typeof(HellForgeForm).GetField("later", fPriv)!.GetValue(classicForm)!;
            msgLbl.Text = ForgeTacticalCopy.LaunchingDetail;
            typeof(HellForgeForm).GetMethod("UpdateView", fPriv)!.Invoke(classicForm, null);

            static bool WinFormsVisible(System.Windows.Forms.Control ctrl) =>
                (bool)typeof(System.Windows.Forms.Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ctrl, [2])!;

            var closingEvt = new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.UserClosing, false);
            typeof(HellForgeForm).GetMethod("OnFormClosing", fPriv)!.Invoke(classicForm, [closingEvt]);

            bool preWindowOk = WinFormsVisible(progBar) && WinFormsVisible(actPanel) && WinFormsVisible(laterBtn) &&
                               laterBtn.Text == ForgeTacticalCopy.CancelLaunchButton && closingEvt.Cancel;

            // Transition to gameLaunched = true -> progress.Visible & later.Visible clear cleanly
            typeof(HellForgeForm).GetField("lastStatus", fPriv)!.SetValue(classicForm,
                new HellForgeStatus(install, "0.5.0-beta", HellForgePack.SupportedBuild, true, true, Running: true, true, true, null, null, GameLaunched: true));
            typeof(HellForgeForm).GetMethod("UpdateView", fPriv)!.Invoke(classicForm, null);
            var closingAfter = new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.UserClosing, false);
            typeof(HellForgeForm).GetMethod("OnFormClosing", fPriv)!.Invoke(classicForm, [closingAfter]);

            check(preWindowOk && !WinFormsVisible(progBar) && !WinFormsVisible(laterBtn) && !closingAfter.Cancel,
                "M2: HellForgeForm keeps marquee progress visible, blocks closing while awaitingGameWindow, and clears progress.Visible once game launches");
        }
    }

    static async Task TestPreWindowLaunchingUxAsync(string dir, Action<bool, string> check)
    {
        const BindingFlags priv = BindingFlags.Instance | BindingFlags.NonPublic;

        // Also verify WindowsForgeHost.IsModeAppliedOnDisk returns false when a stray overhaul pack is co-enabled (#66)
        var strayDemo = new HellForgeMod("pack.ef3458f5-f409-4f24-8933-05fdc10a2101", "EmpireDivers (2.1 Live Demo)", true, Path.Combine(dir, "s"), []);
        var officialClones = new HellForgeMod(HellForgePack.PackId("clonedivers"), "Clonedivers", true, Path.Combine(dir, "c"), []);
        var hostPrefs = new HellForgePreferences { Mode = "clonedivers", DeltaSquad = false };
        hostPrefs.AppliedSelection = hostPrefs.SelectionKey;
        await using (var overlapHost = new WindowsForgeHost(hostPrefs) { IsGameProcessRunning = () => false })
        {
            typeof(WindowsForgeHost).GetField("lastStatus", priv)!.SetValue(overlapHost,
                new HellForgeStatus(@"D:\FixtureForge", "0.5.0-beta", HellForgePack.SupportedBuild, true, true, false, true, true, null, null));
            typeof(WindowsForgeHost).GetField("lastMods", priv)!.SetValue(overlapHost,
                new HellForgeMods([officialClones, strayDemo], [], null));
            check(!overlapHost.GetHomeState().IsModeAppliedOnDisk,
                "WindowsForgeHost marks IsModeAppliedOnDisk=false when a stray demo overhaul pack is co-enabled alongside Clonedivers (#66)");
        }

        bool fakeGameRunning = false;
        var prefs = new HellForgePreferences { Mode = "empire", MinimizeToTray = true };
        await using var winHost = new WindowsForgeHost(prefs) { IsGameProcessRunning = () => fakeGameRunning };

        typeof(WindowsForgeHost).GetField("lastStatus", priv)!.SetValue(winHost,
            new HellForgeStatus(@"D:\FixtureForge", "0.5.0-beta", HellForgePack.SupportedBuild, true, true, Running: true, true, true, null, null, GameLaunched: null));

        // 1. Pre-window launching state (HellForge session accepted & staging mods, helldivers2.exe not yet running)
        var launchingState = winHost.GetHomeState();
        check(launchingState.IsSessionRunning &&
              launchingState.HeaderBadgeText == "Launching…" &&
              launchingState.Headline == ForgeTacticalCopy.LaunchingHeadline &&
              launchingState.Detail == ForgeTacticalCopy.LaunchingDetail &&
              launchingState.PrimaryActionLabel == ForgeTacticalCopy.LaunchingPrimaryButton &&
              !launchingState.PrimaryActionEnabled &&
              launchingState.SecondaryActionLabel == ForgeTacticalCopy.CancelLaunchButton &&
              launchingState.Progress is { IsIndeterminate: true } &&
              !winHost.CanClose(out var closeReason) &&
              closeReason != null && closeReason.Contains("Cancel Launch", StringComparison.Ordinal),
              "WindowsForgeHost shows 'Launching Helldivers 2 — please wait…', indeterminate progress, disabled primary CTA, 'Cancel Launch' secondary button, and close guard while staging (#67)");

        // Verify ForgeShellWindow renders pulsing homeProgress + enabled secondary Cancel Launch button during pre-window launch
        var previewLaunching = new PreviewForgeHost("launching");
        var shellWin = new ForgeShellWindow(previewLaunching);
        var homeProgress = (ProgressBar)typeof(ForgeShellWindow).GetField("homeProgress", priv)!.GetValue(shellWin)!;
        var heroButton = (Button)typeof(ForgeShellWindow).GetField("heroButton", priv)!.GetValue(shellWin)!;
        var secondaryButton = (Button)typeof(ForgeShellWindow).GetField("secondaryButton", priv)!.GetValue(shellWin)!;
        var statusHeadline = (TextBlock)typeof(ForgeShellWindow).GetField("statusHeadline", priv)!.GetValue(shellWin)!;

        check(statusHeadline.Text == "Launching Helldivers 2 — please wait…" &&
              homeProgress.IsVisible && homeProgress.IsIndeterminate &&
              !heroButton.IsEnabled &&
              secondaryButton.IsVisible && secondaryButton.IsEnabled &&
              secondaryButton.Content?.ToString() == "Cancel Launch",
              "ForgeShellWindow renders animated indeterminate progress bar and enabled secondary 'Cancel Launch' button during pre-window launch (#67)");
        shellWin.Close();

        // 2. Transition once helldivers2.exe is observed running
        fakeGameRunning = true;
        var runningState = winHost.GetHomeState();
        check(runningState.IsSessionRunning &&
              runningState.HeaderBadgeText == "Session Active" &&
              runningState.Headline == "Helldivers 2 is running" &&
              runningState.PrimaryActionLabel == "END SESSION" &&
              runningState.PrimaryActionEnabled &&
              runningState.SecondaryActionLabel == null &&
              runningState.Progress == null,
              "WindowsForgeHost transitions cleanly to 'Helldivers 2 is running' + primary 'END SESSION' once helldivers2.exe is observed (#67)");

        var previewRunning = new PreviewForgeHost("running");
        var runningWin = new ForgeShellWindow(previewRunning);
        var runProgress = (ProgressBar)typeof(ForgeShellWindow).GetField("homeProgress", priv)!.GetValue(runningWin)!;
        var runHero = (Button)typeof(ForgeShellWindow).GetField("heroButton", priv)!.GetValue(runningWin)!;
        var runSecondary = (Button)typeof(ForgeShellWindow).GetField("secondaryButton", priv)!.GetValue(runningWin)!;
        check(!runProgress.IsVisible && runHero.IsEnabled && runHero.Content?.ToString() == "END SESSION" && !runSecondary.IsVisible,
            "ForgeShellWindow hides indeterminate bar & Cancel Launch and enables primary END SESSION once game is running (#67)");
        runningWin.Close();

        // M5: Tray session menu item routes pre-window click to TriggerSecondaryActionAsync (Cancel Launch) and active-game click to TriggerPrimaryActionAsync (End Session)
        var trayMockHost = new TrayRoutingMockHost();
        var trayTestWin = new ForgeShellWindow(trayMockHost);
        var fakeTrayItem = new NativeMenuItem("End Session");
        typeof(ForgeShellWindow).GetField("trayEndSessionItem", priv)!.SetValue(trayTestWin, fakeTrayItem);
        typeof(ForgeShellWindow).GetMethod("RefreshAll", priv)!.Invoke(trayTestWin, null);
        bool preWindowTrayHeaderOk = fakeTrayItem.Header == ForgeTacticalCopy.CancelLaunchButton && fakeTrayItem.IsEnabled;
        await trayTestWin.HandleTraySessionActionAsync();

        trayMockHost.GameLaunched = true;
        typeof(ForgeShellWindow).GetMethod("RefreshAll", priv)!.Invoke(trayTestWin, null);
        bool activeTrayHeaderOk = fakeTrayItem.Header == "End Session" && fakeTrayItem.IsEnabled;
        await trayTestWin.HandleTraySessionActionAsync();
        trayTestWin.Close();

        check(preWindowTrayHeaderOk && activeTrayHeaderOk && trayMockHost.SecondaryCalls == 1 && trayMockHost.PrimaryCalls == 1,
            "M5: System tray item displays 'Cancel Launch' and dispatches TriggerSecondaryActionAsync while staging, then 'End Session' -> TriggerPrimaryActionAsync once running");
    }

    sealed class TrayRoutingMockHost : IForgeUiHost
    {
        readonly PreviewForgeHost inner = new("home-clones");
        public bool GameLaunched { get; set; }
        public int PrimaryCalls { get; private set; }
        public int SecondaryCalls { get; private set; }
        public event Action? StateChanged { add => inner.StateChanged += value; remove => inner.StateChanged -= value; }
        public event Action<bool>? SetupVisibilityChanged { add => inner.SetupVisibilityChanged += value; remove => inner.SetupVisibilityChanged -= value; }
        public bool IsPreview => true;
        public bool SupportsEmbeddedSetup => false;
        public bool SupportsSelfUpdate => false;
        public ForgeHomeState GetHomeState() => new PreviewForgeHost(GameLaunched ? "running" : "launching").GetHomeState();
        public ForgeSetupState GetSetupState() => inner.GetSetupState();
        public Task<ForgeDiagnosticsState> GetDiagnosticsAsync(CancellationToken ct = default) => inner.GetDiagnosticsAsync(ct);
        public void AttachUiCallbacks(Func<ForgeConfirmRequest, Task<bool>> c, Func<string, Task<string?>> p, Func<string, string, Task<string?>> s, Action<OperationProgress?>? t = null, Action? r = null) { }
        public Task InitializeAsync() => Task.CompletedTask;
        public bool CanClose(out string? reason) { reason = null; return true; }
        public Task SelectModeAsync(string mode) => Task.CompletedTask;
        public Task UpdateCustomizationAsync(string enemySkin, bool deltaSquad, bool covenant, string texture, string? squidSkin = null) => Task.CompletedTask;
        public Task SetPerformanceTierAsync(string tierId) => Task.CompletedTask;
        public Task UpdateHostMutatorsAsync(string chaosIntensity, string heavySeaf, bool fasterCorpseCleanup, string? theaterPreset = null) => Task.CompletedTask;
        public Task SetGalacticWarAsync(string warMode) => Task.CompletedTask;
        public Task ApplyCustomizationNowAsync() => Task.CompletedTask;
        public Task TriggerPrimaryActionAsync() { PrimaryCalls++; return Task.CompletedTask; }
        public Task TriggerSecondaryActionAsync() { SecondaryCalls++; return Task.CompletedTask; }
        public Task TriggerUpdateBannerActionAsync() => Task.CompletedTask;
        public void DismissUpdateBanner() { }
        public Task AcceptStepDownAsync() => Task.CompletedTask;
        public void DismissStepDownBanner() { }
        public Task InstallSelfShortcutsAsync() => Task.CompletedTask;
        public void DismissSelfInstallBanner() { }
        public Task<WhatsNewResult> FetchWhatsNewAsync(string? versionOrTag, CancellationToken ct = default) => inner.FetchWhatsNewAsync(versionOrTag, ct);
        public void ToggleSoundEffects() { }
        public void OpenSteamLibrary() { }
        public Task EnterSetupAsync() => Task.CompletedTask;
        public void ExitSetup() { }
        public void SelectSetupDrive(string driveRoot) { }
        public Task BrowseSetupFolderAsync(bool existing) => Task.CompletedTask;
        public Task SetSetupTextureAsync(string texture) => Task.CompletedTask;
        public Task TriggerSetupPrimaryActionAsync() => Task.CompletedTask;
        public Task SkipSetupAccountSyncAsync() => Task.CompletedTask;
        public void CancelSetupWork() { }
        public Task OpenOfficialSetupFallbackAsync() => Task.CompletedTask;
        public Task LocateInstallAsync() => Task.CompletedTask;
        public void OpenInstallFolder() { }
        public Task OpenManagerAsync() => Task.CompletedTask;
        public Task SyncAccountFromSettingsAsync() => Task.CompletedTask;
        public Task CheckUpdatesAsync() => Task.CompletedTask;
        public Task CleanCacheAsync() => Task.CompletedTask;
        public Task VerifyAndRepairAsync() => Task.CompletedTask;
        public void SetDiscordRpcEnabled(bool enabled) { }
        public void SetMinimizeToTray(bool enabled) { }
        public Task<string?> SaveSupportReportAsync() => Task.FromResult<string?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class StrayPackCommands(string installRoot, HellForgeMod initialStray) : IHellForgeCommands
    {
        public bool StrayEnabled { get; private set; } = true;
        public bool Running { get; set; }
        public int? SessionRunnerPid { get; set; }
        public int? SessionGamePid { get; set; }
        public List<string> SwitchOffIds { get; } = [];
        bool multiplayer = true;
        bool localPlayFab = true;

        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            string cmd = args[0];
            if (cmd == "status")
            {
                var sessionObj = new Dictionary<string, object?> { ["running"] = Running };
                if (SessionRunnerPid is { } rpid) sessionObj["pid"] = rpid;
                if (SessionGamePid is { } gpid) sessionObj["gamePid"] = gpid;
                return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    install = installRoot,
                    hellforge = "0.5.0-beta",
                    engine = "0.1.58",
                    modSdk = 20,
                    gameBuild = HellForgePack.SupportedBuild,
                    networkBlock = "wfp",
                    ready = new { gameCopy = true, sessionRunner = true },
                    session = sessionObj,
                    settings = new { sandbox = true, multiplayer, localPlayFab }
                })).RootElement.Clone());
            }
            if (cmd == "mods")
            {
                var mods = new object[]
                {
                    new
                    {
                        id = initialStray.Id,
                        name = initialStray.Name,
                        enabled = StrayEnabled,
                        folder = initialStray.Folder,
                        tags = initialStray.Tags,
                        version = "2026.11.21"
                    },
                    new
                    {
                        id = HellForgeClient.DifficultyId,
                        name = "Hell Forge Difficulty",
                        enabled = true,
                        folder = Path.Combine(installRoot, "mods", "diff"),
                        tags = Array.Empty<string>(),
                        version = "1.0.0"
                    }
                };
                return Task.FromResult(JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    mods,
                    problems = Array.Empty<string>()
                })).RootElement.Clone());
            }
            if (cmd == "mod-switch")
            {
                var id = args[2];
                var on = args[4] == "yes";
                if (id == initialStray.Id)
                {
                    StrayEnabled = on;
                    if (!on) SwitchOffIds.Add(id);
                }
                return Task.FromResult(JsonDocument.Parse("""{"ok":true}""").RootElement.Clone());
            }
            if (cmd == "settings")
            {
                multiplayer = true;
                localPlayFab = true;
                return Task.FromResult(JsonDocument.Parse("""{"ok":true}""").RootElement.Clone());
            }
            if (cmd == "mod-order")
            {
                return Task.FromResult(JsonDocument.Parse("""{"ok":true}""").RootElement.Clone());
            }
            throw new InvalidOperationException("Unexpected CLI command in StrayPackCommands: " + string.Join(" ", args));
        }
    }
}
