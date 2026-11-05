using Clonedivers;

namespace Clonedivers.Tests;

internal static class ForgeMigrationTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        AppRelease Release(string channel = "forge", string version = "1.10.0") => new() { Channel = channel, Version = version,
            Url = $"https://github.com/owendavidgoode/clonedivers/releases/download/v{version}/Clonedivers.exe", Size = 3, Sha256 = new string('a', 64) };
        var app = Release();
        check(app.CanSelfUpdate(null, "1.7.3") && ForgeLauncherUpdate.IsOffer(app, "1.9.9"), "the same verified Forge release updates old Steam and current Forge launchers");
        check(!ForgeLauncherUpdate.IsOffer(Release("steam", "9.0.0"), "1.10.0") && !ForgeLauncherUpdate.IsOffer(Release("forge", "1.9.9"), "1.0.0"), "Forge updates refuse legacy-channel releases and pre-migration versions");
        app.Url = "https://example.com/Clonedivers.exe";
        check(!ForgeLauncherUpdate.IsOffer(app, "1.0.0"), "a Forge label cannot authorize a foreign executable URL");
        check(!ForgeLauncherUpdate.IsOffer(Release(), "1.10.0"), "equal versions cannot repeatedly replace the running Forge launcher");
        var parsed = Manifest.Parse("""{"format":2,"app":{"version":"1.10.0","url":"https://github.com/owendavidgoode/clonedivers/releases/download/v1.10.0/Clonedivers.exe","size":3,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","channel":"forge"}}""");
        check(parsed.App?.CanSelfUpdate(null, "1.7.3") == true && parsed.App.Channel == "forge", "the migration app block remains compatible with the old manifest/update contract");
        check(await ForgeLauncherUpdate.CheckAsync("1.9.9", CancellationToken.None, _ => Task.FromResult(parsed)) == parsed.App, "the Forge update review returns the independently pinned executable offer");
        var folder = Directory.CreateDirectory(Path.Combine(root, "launcher-migration")).FullName;
        var exe = Path.Combine(folder, "Clonedivers.exe"); File.WriteAllText(exe, "new launcher");
        var backupDirectory = Path.Combine(folder, "backups");
        check(ForgeLauncherUpdate.PreservePrevious(exe, backupDirectory) == null && !Directory.Exists(backupDirectory), "fresh installations do not create a fictitious legacy backup");
        var old = SelfUpdate.Paths(exe).old; File.WriteAllText(old, "previous launcher");
        var backup = ForgeLauncherUpdate.PreservePrevious(exe, backupDirectory)!;
        check(File.ReadAllText(backup) == "previous launcher" && File.ReadAllText(old) == "previous launcher" && File.ReadAllText(exe) == "new launcher", "migration preserves exact previous launcher bytes without changing or deleting either executable");
        check(ForgeLauncherUpdate.PreservePrevious(exe, backupDirectory) == backup && Directory.GetFiles(backupDirectory).Length == 1, "repeated starts reuse the verified previous-launcher backup");
        File.WriteAllText(backup, "changed"); bool refused = false;
        try { ForgeLauncherUpdate.PreservePrevious(exe, backupDirectory); } catch (IOException) { refused = true; }
        check(refused && File.ReadAllText(old) == "previous launcher", "a damaged retained backup cannot cause deletion of the adjacent recovery executable");
        var legacyFile = Path.Combine(folder, "config.json");
        const string legacyText = """{"Options":{"empire":true,"droids":false,"covenant":true},"TextureProfile":"lighter"}""";
        File.WriteAllText(legacyFile, legacyText);
        var forgeFile = Path.Combine(folder, "hellforge.json");
        var migrated = HellForgePreferences.Load(forgeFile);
        check(migrated.Mode == "empire" && !migrated.Droids && migrated.Covenant && migrated.Texture == "lighter", "first Forge setup carries legacy appearance choices without importing game or account state");
        check(File.ReadAllText(legacyFile) == legacyText && !File.Exists(forgeFile), "reading migration choices leaves both the old settings and new setup uncommitted");
        new HellForgePreferences { Mode = "regular" }.Save(forgeFile);
        check(HellForgePreferences.Load(forgeFile).Mode == "regular", "existing Forge preferences take precedence over the old launcher on every later start");
    }
}
