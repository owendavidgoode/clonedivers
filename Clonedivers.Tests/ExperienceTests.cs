namespace Clonedivers.Tests;

static class ExperienceTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "metadata-cache");
        Directory.CreateDirectory(directory);
        var publicPath = ManifestStore.CachePath(null, directory);
        var customPath = ManifestStore.CachePath("http://localhost:9876/test", directory);
        check(publicPath != customPath, "test metadata has a separate cache from the public feed");
        check(customPath == ManifestStore.CachePath("http://localhost:9876/test", directory), "metadata cache identity is stable");
        check(Path.GetDirectoryName(customPath) == directory, "metadata source cannot escape the cache directory");
        var manifest = new Manifest { Pack = new PackManifest { Version = "test", Options = new() {
            new() { Id = "commandos", Default = true }, new() { Id = "skinny", Default = false }
        }, Files = new() { new() { Name = "a.patch_0", Size = 0, Sha256 = PackFile.EmptySha } } } };
        ManifestStore.Save(publicPath, manifest);
        var saved = ManifestStore.Load(publicPath);
        check(saved?.Pack?.Version == "test", "usable metadata survives offline cold start");
        check(ManifestStore.Load(customPath) is null, "an uncached test feed cannot borrow public metadata");
        File.WriteAllText(publicPath, "{bad json");
        check(ManifestStore.Load(publicPath) is null, "corrupt metadata is ignored rather than claimed valid");
        File.WriteAllText(publicPath, "{\"format\":99}");
        check(ManifestStore.Load(publicPath) is null, "unsupported cached format is ignored");
        var invalidRejected = false;
        try { ManifestStore.Save(publicPath, new Manifest()); } catch (InvalidDataException) { invalidRejected = true; }
        check(invalidRejected, "an unusable online response cannot replace usable cached metadata");
        var settings = new Settings { Options = new() { ["skinny"] = true } };
        check(settings.ProfileFor(manifest.Pack!) == "lighter", "existing skinny preference migrates to Lighter");
        settings.TextureProfile = "full";
        check(settings.ProfileFor(manifest.Pack!) == "full", "explicit profile preference overrides the legacy toggle");
        settings.TextureProfile = "lighter";
        var files = Pack.EffectiveFiles(manifest.Pack!, _ => false, settings.ProfileFor(manifest.Pack!));
        check(files.Count == 1 && settings.InstalledPackVersion is null, "texture choice is resolvable before any successful installation");

        // A corrupt cached filename must not become a usable offline target.
        var escaped = System.Text.Json.JsonSerializer.Serialize(manifest).Replace("a.patch_0", "../a.patch_0");
        File.WriteAllText(publicPath, escaped);
        check(ManifestStore.Load(publicPath) is null, "tampered cached target paths are rejected");
        using (var oversized = File.Create(publicPath)) oversized.SetLength(16L * 1024 * 1024 + 1);
        check(ManifestStore.Load(publicPath) is null, "oversized metadata cache is rejected before parsing");
        ManifestStore.Save(publicPath, manifest);
        var validCache = File.ReadAllBytes(publicPath);
        bool unusableRejected = false;
        try { ManifestStore.ValidateForUse(new Manifest()); } catch (InvalidDataException) { unusableRejected = true; }
        check(unusableRejected && validCache.SequenceEqual(File.ReadAllBytes(publicPath)) && ManifestStore.Load(publicPath)?.Pack?.Version == "test",
            "missing online pack is rejected before replacing the usable cached manifest");

        // Construct the real preview surface without showing a window, starting the game,
        // fetching metadata, or writing the real settings profile.
        var beforeSettings = File.Exists(Settings.FilePath) ? File.ReadAllBytes(Settings.FilePath) : null;
        var previewSettings = new Settings { TextureProfile = "unavailable-profile" };
        using (var preview = new MainForm(previewSettings, null, preview: true))
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var buttons = (List<RoundButton>)typeof(MainForm).GetField("optionButtons", flags)!.GetValue(preview)!;
            check(buttons.Count == 1 && buttons[0].Text == PackProfiles.PotatoName + ": Off", "one potato toggle replaces the texture choices");
            typeof(MainForm).GetMethod("OnPotatoToggle", flags)!.Invoke(preview, null);
            check(previewSettings.TextureProfile == "lighter" && buttons[0].Text.EndsWith(": On"), "potato toggle recovers an unavailable preference with the supported lighter profile");
            typeof(MainForm).GetMethod("OnPotatoToggle", flags)!.Invoke(preview, null);
            check(previewSettings.TextureProfile == "full" && buttons[0].Text.EndsWith(": Off"), "potato toggle returns to full textures");
        }
        var extrasManifest = Manifest.Parse(System.Text.Json.JsonSerializer.Serialize(manifest));
        extrasManifest.Pack!.Options.Add(new PackOption { Id = "droids", Name = "Droid skins", Default = true });
        var extrasSettings = new Settings();
        using (var preview = new MainForm(extrasSettings, null, preview: true, previewManifest: extrasManifest))
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var extras = (List<RoundButton>)typeof(MainForm).GetField("extraButtons", flags)!.GetValue(preview)!;
            check(extras.Count == 1 && extras[0].Text == "Droid skins: On", "pack extras display manifest defaults without duplicating universe or texture controls");
            typeof(MainForm).GetMethod("OnOptionToggle", flags)!.Invoke(preview, new object[] { extrasManifest.Pack.Options.Last() });
            check(!extrasSettings.Options["droids"] && extras[0].Text == "Droid skins: Off", "extra choice works before installation and updates its visible state");
            var clones = LauncherModes.OptionsFor(extrasSettings, extrasManifest.Pack, LauncherMode.Clonedivers);
            var commandos = LauncherModes.OptionsFor(extrasSettings, extrasManifest.Pack, LauncherMode.CommandoDivers);
            check(!clones["droids"] && !commandos["droids"] && !clones["commandos"] && commandos["commandos"], "switching universes preserves the independent droid preference");
            extrasManifest.Pack.CombinedRoster = true;
            extrasSettings.Options["commandos"] = true;
            typeof(MainForm).GetMethod("RefreshPreview", flags)!.Invoke(preview, null);
            check(extras.Count == 1 && extras.All(b => (string)b.Tag! == "droids"), "combined roster keeps Delta always on without a control");
            check(LauncherModes.Current(ModState.On, extrasSettings, extrasManifest.Pack) == LauncherMode.Clonedivers,
                "an existing Delta preference selects the combined modded mode");
            check(LauncherModes.OptionsFor(extrasSettings, extrasManifest.Pack, LauncherMode.Clonedivers)["commandos"],
                "returning from vanilla preserves the existing Delta voice preference");
            extrasSettings.Options["commandos"] = false;
            extrasManifest.Pack.Options.Add(new PackOption { Id = "aimpoints", Default = false });
            extrasSettings.Options["aimpoints"] = false;
            var forced = LauncherModes.OptionsFor(extrasSettings, extrasManifest.Pack, LauncherMode.Clonedivers);
            check(forced["commandos"] && forced["aimpoints"] && !forced["droids"],
                "old disabled Delta and walker flags are overridden while droid choice remains independent");
            extrasManifest.Pack.Files.Add(new PackFile { Name = "delta.patch_0", Size = 0, Sha256 = PackFile.EmptySha, Option = "commandos" });
            extrasManifest.Pack.Files.Add(new PackFile { Name = "walker.patch_0", Size = 0, Sha256 = PackFile.EmptySha, Option = "aimpoints" });
            check(Pack.EffectiveFiles(extrasManifest.Pack, _ => false, "full").Count == 3,
                "file selection enforces required features even with a stale false option callback");
            extrasManifest.Pack.Options.Clear();
            typeof(MainForm).GetMethod("RefreshPreview", flags)!.Invoke(preview, null);
            check(extras.Count == 0, "changing to a feed without extras removes stale controls");
        }
        var afterSettings = File.Exists(Settings.FilePath) ? File.ReadAllBytes(Settings.FilePath) : null;
        check(beforeSettings is null ? afterSettings is null : afterSettings is not null && beforeSettings.SequenceEqual(afterSettings), "preview interaction leaves real saved settings byte-identical");
    }
}
