// Native tests for the Clonedivers file logic. Exit code 0 = everything passed.
// Everything runs inside a throwaway temp folder shaped like a real install:
//   <tmp>\Helldivers 2\data\   <tmp>\Helldivers 2\bin\helldivers2.exe   <tmp>\Helldivers 2\mods_off\

namespace Clonedivers.Tests;

static class TestProgram
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) failures++;
    }

    static void Check<T>(T actual, T expected, string what) where T : IEquatable<T>
    {
        var ok = actual.Equals(expected);
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what + (ok ? "" : $"   (expected {expected}, got {actual})"));
        if (!ok) failures++;
    }

    static string[] Names(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

    static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) =>
        a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "clonedivers-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            PatchNameTests();
            MoveAndRoundTripTests(root);
            LockedAndReadOnlyTests(root);
            StateTests(root);
            VdfTests();
            NormalizeTests(root);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp cleanup best-effort */ }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : $"{failures} TEST(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    static void PatchNameTests()
    {
        Console.WriteLine("Patch-file name matching");
        Check(ModFiles.IsPatchFile("9ba626afa44a3aa3.patch_0"), "matches <hash>.patch_0");
        Check(ModFiles.IsPatchFile("9ba626afa44a3aa3.patch_0.gpu_resources"), "matches .patch_0.gpu_resources companion");
        Check(ModFiles.IsPatchFile("9ba626afa44a3aa3.patch_0.stream"), "matches .patch_0.stream companion");
        Check(ModFiles.IsPatchFile("9ba626afa44a3aa3.patch_12"), "matches multi-digit patch index");
        Check(ModFiles.IsPatchFile("test.patch_0"), "matches the preflight dummy test.patch_0 (name need not be hex)");
        Check(ModFiles.IsPatchFile(@"C:\some\where\9ba626afa44a3aa3.PATCH_0"), "matches full path, case-insensitive");
        Check(!ModFiles.IsPatchFile("game.patch"), "decoy game.patch is NOT a mod");
        Check(!ModFiles.IsPatchFile("9ba626afa44a3aa3"), "base archive (no extension) is NOT a mod");
        Check(!ModFiles.IsPatchFile("9ba626afa44a3aa3.stream"), "base archive .stream is NOT a mod");
        Check(!ModFiles.IsPatchFile("9ba626afa44a3aa3.gpu_resources"), "base archive .gpu_resources is NOT a mod");
        Check(!ModFiles.IsPatchFile("readme.patch_notes.txt"), "'.patch_' without digits is NOT a mod");
        Check(!ModFiles.IsPatchFile("patch_0"), "bare patch_0 (no dot) is NOT a mod");
        Check(!ModFiles.IsPatchFile("9ba626afa44a3aa3.patch_0.bak"), "hand-parked .patch_0.bak is NOT an active mod");
        Check(!ModFiles.IsPatchFile("9ba626afa44a3aa3.patch_0.disabled"), "hand-parked .patch_0.disabled is NOT an active mod");
    }

    static void LockedAndReadOnlyTests(string root)
    {
        Console.WriteLine("A locked file does not strand the others; a read-only stale copy is still overwritten");
        var game = MakeGame(root, "Locked Game");
        var data = Path.Combine(game, "data");
        var off = Path.Combine(game, "mods_off");
        string[] mods = { "1111111111111111.patch_0", "2222222222222222.patch_0", "3333333333333333.patch_0" };
        foreach (var f in mods) File.WriteAllText(Path.Combine(data, f), "fresh:" + f);

        // Read-only stale copy waiting in mods_off\ (e.g. someone marked it read-only by hand).
        Directory.CreateDirectory(off);
        var stale = Path.Combine(off, mods[0]);
        File.WriteAllText(stale, "STALE");
        File.SetAttributes(stale, FileAttributes.ReadOnly);

        // Another process holds the middle file open with no sharing (antivirus mid-scan, browser still writing).
        using (var lock_ = new FileStream(Path.Combine(data, mods[1]), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            IOException? thrown = null;
            try { ModFiles.Toggle(game); } catch (IOException ex) { thrown = ex; }
            Check(thrown is not null, "ON→OFF with one locked file throws IOException");
            Check(thrown?.Message.Contains(mods[1]) == true, "the error names the locked file");
            Check(File.Exists(Path.Combine(off, mods[0])) && File.Exists(Path.Combine(off, mods[2])), "the other two files still moved");
            Check(File.Exists(Path.Combine(data, mods[1])), "the locked file stayed put");
            Check(File.ReadAllText(Path.Combine(off, mods[0])) == "fresh:" + mods[0], "read-only stale copy was overwritten");
            Check((File.GetAttributes(Path.Combine(off, mods[0])) & FileAttributes.ReadOnly) == 0, "read-only flag is gone from the replaced file");
            Check(ModFiles.GetState(game) == ModState.On, "mixed state still reads as ON");
        }

        // Lock released: an OFF flip then an ON flip brings the straggler along.
        ModFiles.Toggle(game);   // ON→OFF gathers the straggler
        Check(SameSet(Names(off), mods) && Names(data).Length == 0, "after the lock is released, one flip gathers everything into mods_off\\");
        ModFiles.Toggle(game);
        Check(SameSet(Names(data), mods) && Names(off).Length == 0, "and the next flip restores all three to data\\");
    }

    static string MakeGame(string root, string name)
    {
        var game = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(game, "data"));
        Directory.CreateDirectory(Path.Combine(game, "bin"));
        File.WriteAllText(Path.Combine(game, "bin", "helldivers2.exe"), "not really");
        return game;
    }

    static void MoveAndRoundTripTests(string root)
    {
        Console.WriteLine("Toggle moves exactly the patch files (and companions), leaves everything else");
        var game = MakeGame(root, "Helldivers 2");
        var data = Path.Combine(game, "data");
        var off = Path.Combine(game, "mods_off");

        string[] baseFiles = { "02582f3da1f8daf5", "02582f3da1f8daf5.stream", "02582f3da1f8daf5.gpu_resources", "game.patch", "readme.patch_notes.txt" };
        string[] modFiles = { "9ba626afa44a3aa3.patch_0", "9ba626afa44a3aa3.patch_0.gpu_resources", "9ba626afa44a3aa3.patch_0.stream", "abcdef0123456789.patch_1", "test.patch_0" };
        foreach (var f in baseFiles) File.WriteAllText(Path.Combine(data, f), "base:" + f);
        foreach (var f in modFiles) File.WriteAllText(Path.Combine(data, f), "fresh:" + f);

        // A stale copy of one mod is already sitting in mods_off\ from an earlier session.
        Directory.CreateDirectory(off);
        File.WriteAllText(Path.Combine(off, "9ba626afa44a3aa3.patch_0"), "STALE");

        Check(ModFiles.GetState(game) == ModState.On, "state is ON while patch files sit in data\\");

        var moved = ModFiles.Toggle(game);
        Check(moved, modFiles.Length, "ON→OFF moved exactly the mod files");
        Check(SameSet(Names(data), baseFiles), "data\\ now holds only base archives + decoys");
        Check(SameSet(Names(off), modFiles), "mods_off\\ holds exactly the mod files");
        Check(File.ReadAllText(Path.Combine(off, "9ba626afa44a3aa3.patch_0")) == "fresh:9ba626afa44a3aa3.patch_0", "stale collision in mods_off\\ was overwritten by the fresh file");
        Check(File.ReadAllText(Path.Combine(data, "game.patch")) == "base:game.patch", "decoy game.patch untouched");
        Check(ModFiles.GetState(game) == ModState.Off, "state is OFF");

        moved = ModFiles.Toggle(game);
        Check(moved, modFiles.Length, "OFF→ON moved exactly the mod files back");
        Check(SameSet(Names(data), baseFiles.Concat(modFiles)), "data\\ has base + mods again (clean round-trip)");
        Check(Names(off).Length, 0, "mods_off\\ is empty after round-trip");
        Check(ModFiles.GetState(game) == ModState.On, "state is ON again");
        foreach (var f in modFiles)
            Check(File.ReadAllText(Path.Combine(data, f)) == "fresh:" + f, $"content intact after round-trip: {f}");

        Console.WriteLine("Mixed state (files in both folders) collapses on the next toggle");
        File.Move(Path.Combine(data, "test.patch_0"), Path.Combine(off, "test.patch_0"));
        Check(ModFiles.GetState(game) == ModState.On, "mixed counts as ON (something is active)");
        ModFiles.Toggle(game);
        Check(SameSet(Names(off), modFiles), "ON→OFF gathered every mod file into mods_off\\");
        Check(SameSet(Names(data), baseFiles), "data\\ is fully vanilla");
        ModFiles.Toggle(game);
        Check(SameSet(Names(data), baseFiles.Concat(modFiles)), "and OFF→ON restores all of them");
    }

    static void StateTests(string root)
    {
        Console.WriteLine("State derivation");
        var game = MakeGame(root, "Empty Game");
        Check(ModFiles.GetState(game) == ModState.NoModFiles, "no patch files anywhere → NoModFiles");
        Check(ModFiles.Toggle(game), 0, "toggle with nothing to move does nothing");
        Check(ModFiles.GetState(Path.Combine(root, "nope")) == ModState.GameNotFound, "missing folder → GameNotFound");
        Check(ModFiles.GetState(null) == ModState.GameNotFound, "null → GameNotFound");
        Check(ModFiles.GetState(root) == ModState.GameNotFound, "folder without data\\ + bin\\helldivers2.exe → GameNotFound");
    }

    static void VdfTests()
    {
        Console.WriteLine("libraryfolders.vdf parsing");
        const string vdf = "\"libraryfolders\"\n{\n" +
            "\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t\t\"contentid\"\t\t\"123\"\n" +
            "\t\t\"apps\"\n\t\t{\n\t\t\t\"553850\"\t\t\"24526126781\"\n\t\t}\n\t}\n" +
            "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"label\"\t\t\"has \\\"quotes\\\" and a path word\"\n\t}\n" +
            "\t\"2\"\n\t{\n\t\t\"path\"\t\t\"E:\\\\Games\\\\Steam \\\"Two\\\"\"\n\t}\n}\n";
        var paths = GameLocator.ParseLibraryPaths(vdf).ToArray();
        Check(paths.Length, 3, "found three \"path\" entries (ignored label/contentid/apps)");
        Check(paths.Length > 0 && paths[0] == @"C:\Program Files (x86)\Steam", "unescaped \\\\ → \\ in first path");
        Check(paths.Length > 1 && paths[1] == @"D:\SteamLibrary", "second library path");
        Check(paths.Length > 2 && paths[2] == "E:\\Games\\Steam \"Two\"", "unescaped \\\" → \" inside a path");
        Check(!GameLocator.ParseLibraryPaths("\"libraryfolders\"\n{\n}\n").Any(), "empty file → no paths");

        var candidates = GameLocator.CandidateGameDirs().ToArray();
        Console.WriteLine($"        (this machine: {candidates.Length} candidate folder(s); auto-detect → {GameLocator.AutoDetect() ?? "none"})");
        Check(candidates.All(c => c.EndsWith(@"\steamapps\common\Helldivers 2", StringComparison.OrdinalIgnoreCase)), "every candidate ends in \\steamapps\\common\\Helldivers 2");
    }

    static void NormalizeTests(string root)
    {
        Console.WriteLine("Folder-picker normalization");
        var game = MakeGame(root, "Picked Game");
        Check(GameLocator.NormalizeGameDir(game) == game, "game folder accepted as-is");
        Check(GameLocator.NormalizeGameDir(game + @"\") == game, "trailing slash trimmed");
        Check(GameLocator.NormalizeGameDir(Path.Combine(game, "data")) == game, "picking data\\ resolves to the game folder");
        Check(GameLocator.NormalizeGameDir(Path.Combine(game, "bin")) == game, "picking bin\\ resolves to the game folder");
        Check(GameLocator.NormalizeGameDir(root) is null, "a random folder is rejected");
        Check(GameLocator.NormalizeGameDir("") is null, "empty is rejected");
        Check(GameLocator.NormalizeGameDir(null) is null, "null is rejected");
    }
}
