using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Clonedivers.Tests;

static class FpvDefaultsTests
{
    const string Camera = "unchanged camera test archive";
    static readonly long Size = Encoding.UTF8.GetByteCount(Camera);
    static readonly string Sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Camera)));
    static readonly UTF8Encoding Utf8 = new(false);
    static void Put(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, Utf8); }
    static bool Seed(string game, string path, Action? beforeCommit = null, Action? beforeReplace = null, IReadOnlyList<PackFile>? expected = null) =>
        FpvDefaults.EnsureForInstalledPack(game, () => path, Size, Sha, expected, beforeCommit, beforeReplace);
    static bool Fails(Action action) { try { action(); return false; } catch (IOException) { return true; } catch (InvalidDataException) { return true; } catch (InvalidOperationException) { return true; } }

    public static void Run(string root, Action<bool, string> check)
    {
        Console.WriteLine("FPV defaults: exact installed bytes, saved choices, backup recovery and guarded atomic writes");
        var folder = Path.Combine(root, "FPV defaults");
        var priorLocal = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", folder);
            check(FpvDefaults.ValuesPath() == Path.Combine(folder, "CowboyBingus", "Helldivers2", "Logs", "ModOptionsMenu.values"), "values path follows the exact author LOCALAPPDATA resolution");
            Environment.SetEnvironmentVariable("LOCALAPPDATA", null);
            check(Fails(() => FpvDefaults.ValuesPath()), "missing LOCALAPPDATA produces an actionable error instead of seeding another location");
        }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", priorLocal); }
        var game = Path.Combine(folder, "game");
        var camera = Path.Combine(game, "data", "A.patch_0");
        Put(Path.Combine(game, "bin", "helldivers2.exe"), "");
        check(!FpvDefaults.EnsureForInstalledPack(null), "undetected game leaves a vanilla Steam launch available");
        check(!FpvDefaults.EnsureForInstalledPack(Path.Combine(folder, "missing game")), "missing game does not resolve receipt or FPV options");
        check(!FpvDefaults.EnsureForInstalledPack(game), "game without mods does not resolve receipt or FPV options");
        Put(camera, Camera);
        string At(string name) => Path.Combine(folder, name, "ModOptionsMenu.values");

        var path = At("Fresh");
        check(Seed(game, path) && File.ReadAllText(path) == "firstperson.enabled\tfalse\nfirstperson.bridge2\t1\n", "fresh FPV starts disabled and seeds bridge choice 1");
        var stamp = File.GetLastWriteTimeUtc(path);
        check(!Seed(game, path) && stamp == File.GetLastWriteTimeUtc(path), "repeated preparation leaves existing defaults untouched");

        foreach (var enabled in new[] { "true", "false" })
        {
            path = At("Saved " + enabled);
            var text = "other.option\t17\r\nfirstperson.enabled\t" + enabled + "\r\nfirstperson.bridge2\t2\r\nunknown.future\tkeep me\r\n";
            Put(path, text);
            check(!Seed(game, path) && File.ReadAllText(path) == text, "saved " + enabled + ", bridge and unknown options stay byte-exact");
        }
        path = At("Append");
        var original = "unknown.option\tunchanged\r\nfirstperson.enabled\ttrue";
        Put(path, original);
        check(Seed(game, path) && File.ReadAllText(path) == original + "\nfirstperson.bridge2\t1\n", "only missing bridge is appended; existing enabled=true is preserved");

        foreach (var primary in new string?[] { null, "" })
        {
            path = At(primary is null ? "Backup missing primary" : "Backup empty primary");
            if (primary is not null) Put(path, primary);
            var saved = "firstperson.enabled\ttrue\nfirstperson.bridge2\t2\nunknown.backup\t42\n";
            Put(path + ".bak", saved);
            check(Seed(game, path) && File.ReadAllText(path) == saved && File.ReadAllText(path + ".bak") == saved,
                "authoritative backup choices are recovered without resetting enabled=true");
        }
        path = At("Primary wins");
        Put(path, "unknown.primary\tkeep\n"); Put(path + ".bak", "firstperson.enabled\ttrue\n");
        check(Seed(game, path) && File.ReadAllText(path).Contains("firstperson.enabled\tfalse"), "valid primary ignores stale backup exactly as the options menu does");

        var bad = new[] { "firstperson.enabled\tfalse\nfirstperson.enabled\ttrue\n", "firstperson.enabled\tTRUE\n", "firstperson.enabled\t\n",
            "firstperson.bridge2\t0\n", "firstperson.bridge2\t1.5\n", "firstperson.enabled=false\n", "bad\tline\textra\n", "unknown\t1\nunknown\t2\n", "unknown\tzero\0\n" };
        for (var index = 0; index < bad.Length; index++)
        {
            path = At("Invalid " + index); Put(path, bad[index]);
            check(Fails(() => Seed(game, path)) && File.ReadAllText(path) == bad[index], "invalid/duplicate options block without overwriting case " + index);
        }
        path = At("BOM"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("firstperson.enabled\tfalse\n")).ToArray());
        var bom = File.ReadAllBytes(path);
        check(Fails(() => Seed(game, path)) && File.ReadAllBytes(path).SequenceEqual(bom), "UTF-8 BOM is rejected because the author treats it as part of the key");
        path = At("Invalid UTF8"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, new byte[] { 0xc3, 0x28 });
        check(Fails(() => Seed(game, path)), "invalid UTF-8 blocks rather than replacing unknown bytes");
        path = At("Bound"); Put(path, new string('x', FpvDefaults.MaximumValuesBytes + 1));
        check(Fails(() => Seed(game, path)), "oversized options are bounded before decoding or writing");

        path = At("Changed before commit"); Put(path, "unknown\told\n");
        check(Fails(() => Seed(game, path, () => Put(path, "firstperson.enabled\ttrue\nunknown\tnew\n"))) &&
            File.ReadAllText(path) == "firstperson.enabled\ttrue\nunknown\tnew\n", "a concurrent edit before commit remains untouched");
        path = At("New file race");
        check(Fails(() => Seed(game, path, () => Put(path, "firstperson.enabled\ttrue\n"))) && File.ReadAllText(path) == "firstperson.enabled\ttrue\n",
            "a concurrently created options file wins over fresh seeding");
        path = At("Changed backup"); Put(path + ".bak", "firstperson.enabled\ttrue\n");
        check(Fails(() => Seed(game, path, () => Put(path + ".bak", "firstperson.enabled\tfalse\n"))) && !File.Exists(path), "backup recovery rejects a racing backup change");
        path = At("Late replacement race"); Put(path, "unknown\told\n");
        check(Fails(() => Seed(game, path, beforeReplace: () => Put(path, "firstperson.enabled\ttrue\nunknown\tlate\n"))) &&
            File.ReadAllText(path + ".clonedivers-seed-backup") == "firstperson.enabled\ttrue\nunknown\tlate\n" && Fails(() => Seed(game, path)),
            "an edit at the atomic replacement boundary is retained in backup and blocks subsequent launch until reconciled");

        path = At("Locked"); Put(path, "unknown\tkeep\n");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(Fails(() => Seed(game, path)), "locked options stop launch with an error");
        check(File.ReadAllText(path) == "unknown\tkeep\n", "locked options keep original bytes");

        var outside = Path.Combine(folder, "linked destination"); Directory.CreateDirectory(outside);
        var link = Path.Combine(folder, "junction");
        // NTFS junction creation works without Developer Mode/admin; all paths belong to this throwaway test root.
        var script = "New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using (var process = Process.Start(start)!) { process.WaitForExit(); check(process.ExitCode == 0, "junction safety fixture is created"); }
        try { check(Fails(() => Seed(game, Path.Combine(link, "ModOptionsMenu.values"))) && !File.Exists(Path.Combine(outside, "ModOptionsMenu.values")), "linked parent directories never redirect FPV option writes"); }
        finally { Directory.Delete(link); }

        path = At("Eligibility");
        Put(camera, "another unrelated mod archive");
        check(!Seed(game, path) && !Directory.Exists(Path.GetDirectoryName(path)), "non-FPV mods never create the options directory");
        var expected = new List<PackFile> { new() { Name = "A.patch_0", Size = Size, Sha256 = Sha, Url = "https://example.invalid/camera" } };
        check(Fails(() => Seed(game, path, expected: expected)) && !File.Exists(path), "a receipt/URL claiming FPV cannot seed when installed bytes differ");
        Put(camera, Camera);
        var parked = Path.Combine(game, ModFiles.OffFolder); Directory.CreateDirectory(parked); File.Move(camera, Path.Combine(parked, "A.patch_0"));
        check(!Seed(game, path) && !File.Exists(path), "vanilla with parked FPV never seeds options");
        File.Move(Path.Combine(parked, "A.patch_0"), camera);
        Game.IsRunningCheck = () => true;
        try { check(Fails(() => Seed(game, path)) && !File.Exists(path), "running game blocks option preparation"); }
        finally { Game.IsRunningCheck = () => false; }
        File.Move(camera, Path.Combine(game, "data", "A.patch_99"));
        check(Seed(game, path), "exact camera bytes remain eligible after production patch renumbering");

        var dependency = "options dependency fixture";
        var dependencySha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dependency)));
        var dependencies = new[] { new FpvDefaults.ArchiveIdentity("ModOptions", Encoding.UTF8.GetByteCount(dependency), dependencySha) };
        path = At("Dependency guard");
        bool Guarded() => FpvDefaults.EnsureForInstalledPack(game, () => path, Size, Sha, dependencies: dependencies);
        check(Fails(() => Guarded()) && !File.Exists(path), "recognized FPV with missing ModOptions blocks before creating values");
        var dependencyPath = Path.Combine(game, "data", "A.patch_100");
        Put(dependencyPath, new string('x', dependency.Length));
        check(Fails(() => Guarded()) && !File.Exists(path), "corrupt same-size ModOptions cannot pass the fresh dependency hash guard");
        Put(dependencyPath, dependency);
        dependencies = new[] { dependencies[0], new FpvDefaults.ArchiveIdentity("Bingus loader", 6, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("loader")))) };
        check(Fails(() => Guarded()) && !File.Exists(path), "missing loader blocks even with verified camera and ModOptions");
        var loaderPath = Path.Combine(game, "data", "A.patch_101");
        Put(loaderPath, "broken");
        check(Fails(() => Guarded()) && !File.Exists(path), "corrupt loader is rejected by content rather than name or size");
        Put(loaderPath, "loader");
        check(Guarded() && File.ReadAllText(path).Contains("firstperson.enabled\tfalse"), "exact active camera/options/loader bytes allow default-off preparation");
        check(!FpvDefaults.EnsureForInstalledPack(Path.Combine(folder, "no mods")), "dependency preparation never changes a separate vanilla install");
    }
}
