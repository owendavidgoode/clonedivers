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
            check(buttons.Count == 2 && buttons.All(b => !b.Text.StartsWith("✓")), "preview with unavailable saved profile renders available choices without claiming one selected");
            typeof(MainForm).GetMethod("OnTextureProfile", flags)!.Invoke(preview, new object[] { new PackTextureProfile { Id = "lighter", Name = "Lighter textures" } });
            check(previewSettings.TextureProfile == "lighter" && buttons.Count(b => b.Text.StartsWith("✓")) == 1, "preview can recover from unavailable preference by selecting an available texture profile");
        }
        var afterSettings = File.Exists(Settings.FilePath) ? File.ReadAllBytes(Settings.FilePath) : null;
        check(beforeSettings is null ? afterSettings is null : afterSettings is not null && beforeSettings.SequenceEqual(afterSettings), "preview interaction leaves real saved settings byte-identical");
    }
}
