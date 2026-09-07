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
        Game.IsRunningCheck = () => false;   // the engine refuses to move files while helldivers2.exe runs; tests must not depend on the host PC
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
            PackZipTests(root);
            ManifestTests();
            DownloadTests(root).GetAwaiter().GetResult();
            ManifestV2Tests();
            EffectiveFilesTests();
            PlanAndApplyTests(root).GetAwaiter().GetResult();
            SurplusAndParkNamingTests(root);
            InterruptedAndReplayTests(root);
            HashCacheAndCleanupTests(root);
            SelfUpdateAndAcfTests(root);
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

    static string MakeZip(string path, params (string entry, string content)[] entries)
    {
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = zip.CreateEntry(entry);
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }
        return path;
    }

    static void PackZipTests(string root)
    {
        Console.WriteLine("Pack zip: extracts only patch files, flat, overwriting; rejects options packages");
        var game = MakeGame(root, "Pack Game");
        var data = Path.Combine(game, "data");
        var zip = MakeZip(Path.Combine(root, "pack.zip"),
            ("Clone Armory/9ba626afa44a3aa3.patch_0", "armory"),
            ("Clone Armory/9ba626afa44a3aa3.patch_0.gpu_resources", "armory-gpu"),
            ("Clone Armory/9ba626afa44a3aa3.patch_0.stream", "armory-stream"),
            ("Blasters/abcdef0123456789.patch_1", "blasters"),
            ("nested/deeper/1111111111111111.patch_2", "deep"),
            ("manifest.json", "{}"),
            ("readme.txt", "hello"),
            ("Blasters/icon.png", "png"));

        var names = Pack.ListPatchEntries(zip);
        Check(names.Length, 5, "ListPatchEntries finds the five patch files and ignores manifest/readme/icon");

        // Stale read-only copy of one file plus a leftover partial from a crashed run.
        File.WriteAllText(Path.Combine(data, "abcdef0123456789.patch_1"), "STALE");
        File.SetAttributes(Path.Combine(data, "abcdef0123456789.patch_1"), FileAttributes.ReadOnly);
        File.WriteAllText(Path.Combine(data, "zzz.patch_0.clonedivers-partial"), "junk");
        File.WriteAllText(Path.Combine(data, "02582f3da1f8daf5"), "base archive");

        long lastDone = 0, lastTotal = 0;
        var progress = new SyncProgress<(long done, long total)>(p => { lastDone = p.done; lastTotal = p.total; });
        var count = Pack.ExtractPatchFiles(zip, data, progress, CancellationToken.None);
        Check(count, 5, "ExtractPatchFiles extracted five files");
        Check(SameSet(Names(data), new[] { "02582f3da1f8daf5", "9ba626afa44a3aa3.patch_0", "9ba626afa44a3aa3.patch_0.gpu_resources", "9ba626afa44a3aa3.patch_0.stream", "abcdef0123456789.patch_1", "1111111111111111.patch_2" }),
            "data\\ holds the base archive plus exactly the five pack files, flat (folders dropped), no manifest/readme/icon, no partial");
        Check(File.ReadAllText(Path.Combine(data, "abcdef0123456789.patch_1")) == "blasters", "stale read-only file overwritten by the pack");
        Check(File.ReadAllText(Path.Combine(data, "1111111111111111.patch_2")) == "deep", "deeply nested entry landed flat in data\\");
        Check(lastDone == lastTotal && lastTotal > 0, "progress reached total");
        Check(ModFiles.GetState(game) == ModState.On, "state is ON after install");

        var options = MakeZip(Path.Combine(root, "options.zip"),
            ("Phase 1/9ba626afa44a3aa3.patch_0", "p1"),
            ("Phase 2/9ba626afa44a3aa3.patch_0", "p2"));
        InvalidDataException? ex = null;
        try { Pack.ListPatchEntries(options); } catch (InvalidDataException e) { ex = e; }
        Check(ex is not null && ex.Message.Contains("options package"), "a zip with the same patch name in two folders is rejected as an options package");

        var empty = MakeZip(Path.Combine(root, "empty.zip"), ("readme.txt", "nothing here"));
        ex = null;
        try { Pack.ExtractPatchFiles(empty, data, null, CancellationToken.None); } catch (InvalidDataException e) { ex = e; }
        Check(ex is not null, "a zip with no patch files is rejected");

        Console.WriteLine("Pack install: parked files come back, stale mods go to mods_old\\, ends ON");
        var game2 = MakeGame(root, "Install Game");
        var data2 = Path.Combine(game2, "data");
        var off2 = Path.Combine(game2, "mods_off");
        var old2 = Path.Combine(game2, "mods_old");
        Directory.CreateDirectory(off2);
        File.WriteAllText(Path.Combine(data2, "02582f3da1f8daf5"), "base");
        File.WriteAllText(Path.Combine(data2, "9ba626afa44a3aa3.patch_0"), "old armory");        // in pack: gets overwritten
        File.WriteAllText(Path.Combine(data2, "feedfeedfeedfeed.patch_0"), "hand-installed");    // not in pack: parked
        File.WriteAllText(Path.Combine(off2, "abcdef0123456789.patch_1"), "parked blasters");    // parked, in pack: comes back then overwritten
        File.WriteAllText(Path.Combine(off2, "dead0000dead0000.patch_3"), "parked stale");       // parked, not in pack: goes to mods_old
        var partA = MakeZip(Path.Combine(root, "packA.zip"), ("9ba626afa44a3aa3.patch_0", "new armory"), ("9ba626afa44a3aa3.patch_0.stream", "s"));
        var partB = MakeZip(Path.Combine(root, "packB.zip"), ("abcdef0123456789.patch_1", "new blasters"));
        var (installed, parked) = Pack.Install(game2, new[] { partA, partB }, null, CancellationToken.None);
        Check(installed, 3, "three files installed across two parts");
        Check(parked, 2, "two stale files parked");
        Check(SameSet(Names(data2), new[] { "02582f3da1f8daf5", "9ba626afa44a3aa3.patch_0", "9ba626afa44a3aa3.patch_0.stream", "abcdef0123456789.patch_1" }), "data\\ = base archive + exactly the pack");
        Check(SameSet(Names(old2), new[] { "feedfeedfeedfeed.patch_0", "dead0000dead0000.patch_3" }), "mods_old\\ holds the two files the pack did not contain");
        Check(Names(off2).Length, 0, "mods_off\\ is empty afterwards");
        Check(File.ReadAllText(Path.Combine(data2, "9ba626afa44a3aa3.patch_0")) == "new armory" && File.ReadAllText(Path.Combine(data2, "abcdef0123456789.patch_1")) == "new blasters", "pack content replaced both the active and the parked copies");
        Check(ModFiles.GetState(game2) == ModState.On, "state is ON after install");

        var partDup = MakeZip(Path.Combine(root, "packDup.zip"), ("9ba626afa44a3aa3.patch_0", "again"));
        ex = null;
        try { Pack.Install(game2, new[] { partA, partDup }, null, CancellationToken.None); } catch (InvalidDataException e) { ex = e; }
        Check(ex is not null && ex.Message.Contains("more than one"), "the same file in two selected zips is rejected before anything moves");
        Check(File.ReadAllText(Path.Combine(data2, "9ba626afa44a3aa3.patch_0")) == "new armory", "and data\\ was left untouched");
    }

    static void ManifestTests()
    {
        Console.WriteLine("pack.json parsing");
        var m = Pack.ParseManifest("{ \"version\": \"2026.09.05\", \"name\": \"Clonedivers pack\", \"notes\": \"Built for 7.0.2\", \"files\": [ { \"url\": \"https://example.com/a.zip\", \"size\": 10, \"sha256\": \"AB\" }, { \"url\": \"https://example.com/b.zip\", \"size\": 5, \"sha256\": \"CD\" } ] }");
        Check(m is not null && m.Version == "2026.09.05" && m.Files.Count == 2, "camelCase manifest parses");
        Check(m!.TotalSize, 15L, "TotalSize sums the parts");
        Check(m.IsPublished, "manifest with URLs counts as published");
        var placeholder = Pack.ParseManifest("{ \"version\": \"\", \"files\": [] }");
        Check(placeholder is not null && !placeholder.IsPublished, "empty files list means not published yet");
        var blankUrl = Pack.ParseManifest("{ \"version\": \"1\", \"files\": [ { \"url\": \"\", \"size\": 1, \"sha256\": \"\" } ] }");
        Check(blankUrl is not null && !blankUrl.IsPublished, "a file with a blank URL means not published yet");
        Check(Pack.FormatBytes(5L * 1024 * 1024 * 1024 + 100) == "5.0 GB" && Pack.FormatBytes(223L * 1024 * 1024) == "223 MB", "FormatBytes");
    }

    /// <summary>Minimal HTTP/1.1 server for the download tests: serves one byte array, honours Range, one request per connection.</summary>
    sealed class TinyHttp : IDisposable
    {
        readonly System.Net.Sockets.TcpListener listener;
        readonly byte[] body;
        readonly string contentType;
        public int RangeRequests;
        public TinyHttp(byte[] body, string contentType = "application/zip")
        {
            this.body = body; this.contentType = contentType;
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            _ = AcceptLoop();
        }
        public string Url => $"http://127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndpoint).Port}/clonedivers-pack.zip";
        async Task AcceptLoop()
        {
            try { while (true) { var c = await listener.AcceptTcpClientAsync(); _ = Task.Run(() => Serve(c)); } }
            catch { /* listener stopped */ }
        }
        void Serve(System.Net.Sockets.TcpClient c)
        {
            using (c)
            using (var s = c.GetStream())
            {
                var reader = new StreamReader(s, System.Text.Encoding.ASCII, false, 4096, leaveOpen: true);
                string? line; long start = 0; bool range = false;
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                    { range = true; start = long.Parse(line.Substring("Range: bytes=".Length).Split('-')[0]); }
                if (range) Interlocked.Increment(ref RangeRequests);
                string head;
                byte[] payload;
                if (start >= body.Length)
                {
                    head = $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{body.Length}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    payload = Array.Empty<byte>();
                }
                else
                {
                    payload = body.AsSpan((int)start).ToArray();
                    head = (range ? $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {start}-{body.Length - 1}/{body.Length}\r\n" : "HTTP/1.1 200 OK\r\n")
                         + $"Content-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                }
                var hb = System.Text.Encoding.ASCII.GetBytes(head);
                s.Write(hb, 0, hb.Length);
                s.Write(payload, 0, payload.Length);
                s.Flush();
            }
        }
        public void Dispose() => listener.Stop();
    }

    static async Task DownloadTests(string root)
    {
        Console.WriteLine("Pack download: full, resumed, corrupted, wrong size, web-page-instead-of-file");
        var body = new byte[3 * 1024 * 1024 + 7];
        new Random(42).NextBytes(body);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body));
        var dlDir = Path.Combine(root, "dl");

        using (var srv = new TinyHttp(body))
        {
            var file = new PackFile { Url = srv.Url, Size = body.Length, Sha256 = sha.ToLowerInvariant() };
            var dest = Path.Combine(dlDir, "full.zip");
            long last = 0;
            await Pack.DownloadAsync(file, dest, new SyncProgress<(long done, long total)>(p => last = p.done), CancellationToken.None);
            Check(File.ReadAllBytes(dest).AsSpan().SequenceEqual(body), "full download matches byte for byte (hash given in lower case)");
            Check(last, (long)body.Length, "progress reported the final byte count");
            Check(srv.RangeRequests, 0, "no Range header on a fresh download");

            var resumeDest = Path.Combine(dlDir, "resume.zip");
            Directory.CreateDirectory(dlDir);
            File.WriteAllBytes(resumeDest, body.AsSpan(0, 1024 * 1024).ToArray());
            await Pack.DownloadAsync(file, resumeDest, null, CancellationToken.None);
            Check(srv.RangeRequests, 1, "a partial file triggers exactly one Range request");
            Check(File.ReadAllBytes(resumeDest).AsSpan().SequenceEqual(body), "resumed download matches byte for byte");

            var already = Path.Combine(dlDir, "already.zip");
            File.WriteAllBytes(already, body);
            await Pack.DownloadAsync(file, already, null, CancellationToken.None);
            Check(srv.RangeRequests, 1, "a complete file is only verified, not re-downloaded");

            var bad = new PackFile { Url = srv.Url, Size = body.Length, Sha256 = new string('0', 64) };
            var badDest = Path.Combine(dlDir, "bad.zip");
            InvalidDataException? ex = null;
            try { await Pack.DownloadAsync(bad, badDest, null, CancellationToken.None); } catch (InvalidDataException e) { ex = e; }
            Check(ex is not null && ex.Message.Contains("SHA-256"), "hash mismatch throws");
            Check(!File.Exists(badDest), "and the corrupt download is deleted");

            var wrongSize = new PackFile { Url = srv.Url, Size = body.Length + 5, Sha256 = "" };
            var wsDest = Path.Combine(dlDir, "wrongsize.zip");
            ex = null;
            try { await Pack.DownloadAsync(wrongSize, wsDest, null, CancellationToken.None); } catch (InvalidDataException e) { ex = e; }
            Check(ex is not null && ex.Message.Contains("bytes"), "size mismatch throws");
            Check(!File.Exists(wsDest), "and the short download is deleted");
        }

        using (var html = new TinyHttp(System.Text.Encoding.UTF8.GetBytes("<html>Google Drive can't scan this file for viruses</html>"), "text/html"))
        {
            var file = new PackFile { Url = html.Url, Size = 0, Sha256 = "" };
            InvalidDataException? ex = null;
            try { await Pack.DownloadAsync(file, Path.Combine(dlDir, "page.zip"), null, CancellationToken.None); } catch (InvalidDataException e) { ex = e; }
            Check(ex is not null && ex.Message.Contains("web page"), "an HTML response is rejected with the browser-download hint");
        }
    }

    /// <summary>Progress that reports synchronously (System.Progress posts to a sync context the console app lacks).</summary>
    sealed class SyncProgress<T> : IProgress<T>
    {
        readonly Action<T> handler;
        public SyncProgress(Action<T> handler) => this.handler = handler;
        public void Report(T value) => handler(value);
    }

    // ------------------------------------------------------------------ 1.3.0: manifest format 2, options, inventory, plan, apply, replay, self-update

    static string ShaOf(string content) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    static PackFile F(string name, string content, string? option = null, string url = "") =>
        new() { Name = name, Size = System.Text.Encoding.UTF8.GetByteCount(content), Sha256 = content.Length == 0 ? PackFile.EmptySha : ShaOf(content), Url = url, Option = option };
    static void Put(string dir, string name, string content) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name), content); }

    static void ManifestV2Tests()
    {
        Console.WriteLine("manifest.json (format 2): parse rules, options, app block");
        var json = """
        { "format": 2,
          "app": { "version": "1.3.1", "url": "https://github.com/owendavidgoode/clonedivers/releases/download/v1.3.1/Clonedivers.exe", "size": 5, "sha256": "ABCD" },
          "pack": { "version": "r4", "name": "P", "notes": "n", "gameBuild": "24826606", "status": "ok",
                    "options": [ { "id": "cmd", "name": "Republic Commandos", "default": true } ],
                    "files": [ { "name": "9ba626afa44a3aa3.patch_0", "size": 3, "sha256": "SHA_A", "url": "https://x/a" },
                               { "name": "9ba626afa44a3aa3.patch_0.stream", "size": 0, "sha256": "", "url": "" },
                               { "name": "9ba626afa44a3aa3.patch_1", "size": 3, "sha256": "SHA_B", "url": "https://x/b", "option": "cmd" } ],
                    "extraFutureField": 1 } }
        """.Replace("SHA_A", new string('A', 64)).Replace("SHA_B", new string('b', 64));
        var m = Manifest.Parse(json);
        Check(m.Format, 2, "format 2 parsed");
        Check(m.App is not null && m.App.IsNewerThan("1.3.0"), "app 1.3.1 is newer than 1.3.0");
        Check(m.App is not null && !m.App.IsNewerThan("1.3.1") && !m.App.IsNewerThan("1.10.0"), "not newer than itself or than 1.10.0");
        Check(m.App!.IsPinned, "app.url matches the pinned release URL");
        Check(new AppRelease { Version = "1.3.1", Url = "https://evil/x.exe" }.IsPinned == false, "a foreign app.url is not pinned");
        var p = m.Pack!;
        Check(p.Files.Count, 3, "three files");
        Check(p.Files[0].Sha256 == new string('a', 64), "sha lower-cased");
        Check(p.Files[1].Sha256 == PackFile.EmptySha && p.Files[1].Url == "", "size-0 entry gets the empty sha and needs no url");
        Check(p.IsPublished, "IsPublished with a blank url only on the size-0 entry");
        Check(p.IsPerFile, "IsPerFile");
        Check(p.GameBuild == "24826606" && !p.IsBroken, "gameBuild read, status ok");
        Check(p.Options.Count == 1 && p.Files[2].Option == "cmd", "option and file tag");

        var noUrl = Manifest.Parse(json.Replace("\"url\": \"https://x/b\"", "\"url\": \"\""));
        Check(!noUrl.Pack!.IsPublished, "a non-empty file without url is not published");
        InvalidDataException? ex = null;
        try { Manifest.Parse(json.Replace("9ba626afa44a3aa3.patch_1\"", "sub\\\\dir.patch_1\"")); } catch (InvalidDataException e) { ex = e; }
        Check(ex is not null, "a file name with a path separator is rejected");
        ex = null;
        try { Manifest.Parse(json.Replace("\"option\": \"cmd\"", "\"option\": \"nope\"")); } catch (InvalidDataException e) { ex = e; }
        Check(ex is not null, "an unknown option id is rejected");
        ex = null;
        try { Manifest.Parse(json.Replace("9ba626afa44a3aa3.patch_1\"", "9ba626afa44a3aa3.PATCH_0\"")); } catch (InvalidDataException e) { ex = e; }
        Check(ex is not null, "duplicate names differing by case are rejected");

        var legacy = Manifest.Parse("""{ "version": "r3", "files": [ { "url": "https://x/p1.zip", "size": 10, "sha256": "AB" } ] }""");
        Check(legacy.Format == 1 && legacy.Pack is not null && legacy.Pack.Files.Count == 1 && !legacy.Pack.IsPerFile && legacy.Pack.IsPublished, "format-1 pack.json still parses (zip parts, no names)");
        Check(Manifest.Parse("""{ "format": 2 }""").Pack is null, "a manifest without a pack block parses (Pack null)");
        Check(Manifest.Parse(json.Replace("\"format\": 2", "\"format\": 3")).App is not null, "format 3 still parses and keeps the app block");
    }

    static void EffectiveFilesTests()
    {
        Console.WriteLine("Options: the enabled set is renumbered so switching a group off leaves no gap");
        var pack = new PackManifest
        {
            Options = { new PackOption { Id = "cmd", Name = "Republic Commandos" } },
            Files =
            {
                F("A.patch_0", "a"), F("A.patch_0.stream", ""),
                F("A.patch_1", "b"), F("A.patch_2", "opt", "cmd"), F("A.patch_2.gpu_resources", "optg", "cmd"),
                F("A.patch_3", "c"), F("B.patch_0", "x"),
            },
        };
        var on = Pack.EffectiveFiles(pack, _ => true);
        Check(on.Select(f => f.Name).SequenceEqual(pack.Files.Select(f => f.Name)), "all options on: names unchanged");
        var off = Pack.EffectiveFiles(pack, _ => false);
        Check(off.Count, 5, "option off drops its two files");
        Check(off.Select(f => f.Name).SequenceEqual(new[] { "A.patch_0", "A.patch_0.stream", "A.patch_1", "A.patch_2", "B.patch_0" }), "A.patch_3 became A.patch_2; B untouched");
        Check(off.Single(f => f.Name == "A.patch_2").Sha256 == ShaOf("c"), "the renumbered entry keeps its bytes");
        Check(!ReferenceEquals(on[0], pack.Files[0]), "EffectiveFiles returns copies, the manifest is not mutated");

        // Variant pair: the base texture is used unless "skinny" is on; then its lighter twin with the same name takes over.
        var skinny = new PackManifest { Options = { new PackOption { Id = "skinny", Name = "Lighter textures", Default = false } } };
        skinny.Files.Add(F("A.patch_0", "base"));
        skinny.Files.Add(F("A.patch_0.gpu_resources", "fat-texture")); skinny.Files[1].UnlessOption = "skinny";
        skinny.Files.Add(F("A.patch_0.gpu_resources", "thin", "skinny"));
        var fat = Pack.EffectiveFiles(skinny, _ => false); var thin = Pack.EffectiveFiles(skinny, _ => true);
        Check(fat.Count == 2 && fat[1].Sha256 == ShaOf("fat-texture"), "skinny off: the base texture, same name");
        Check(thin.Count == 2 && thin[1].Sha256 == ShaOf("thin") && thin[1].Name == "A.patch_0.gpu_resources", "skinny on: the lighter twin under the same name, no renumbering");
        var pairJson = Manifest.Parse(("{ \"format\": 2, \"pack\": { \"version\": \"v\", \"options\": [ { \"id\": \"skinny\", \"name\": \"L\" } ], \"files\": [ " +
            "{ \"name\": \"A.patch_0.gpu_resources\", \"size\": 3, \"sha256\": \"SHA\", \"url\": \"u\", \"unlessOption\": \"skinny\" }, " +
            "{ \"name\": \"A.patch_0.gpu_resources\", \"size\": 4, \"sha256\": \"SHA\", \"url\": \"u\", \"option\": \"skinny\" } ] } }").Replace("SHA", new string('a', 64)));
        Check(pairJson.Pack!.Files.Count == 2, "a base/variant pair with the same name parses");
        InvalidDataException? dupEx = null;
        try { Manifest.Parse("""{ "format": 2, "pack": { "version": "v", "files": [ { "name": "A.patch_0", "size": 1, "sha256": "SHA", "url": "u" }, { "name": "A.patch_0", "size": 1, "sha256": "SHA", "url": "u" } ] } }""".Replace("SHA", new string('a', 64))); } catch (InvalidDataException e) { dupEx = e; }
        Check(dupEx is not null, "the same name twice without a variant option is rejected");
    }

    static async Task PlanAndApplyTests(string root)
    {
        Console.WriteLine("Per-file update: inventory → plan → download → apply, with renumbering, duplicates, zero-byte files and a stale file");
        var game = MakeGame(root, "PerFile Game");
        var data = Path.Combine(game, "data"); var old = Path.Combine(game, "mods_old"); var dl = Path.Combine(game, "mods_download");
        var cachePath = Path.Combine(root, "perfile-hashes.json"); var planPath = Path.Combine(root, "perfile-plan.json");
        // Installed r1: aaa at 0 (+ empty stream), bbb at 1, ccc at 2, plus a stale file nobody wants.
        Put(data, "A.patch_0", "aaa"); Put(data, "A.patch_0.stream", ""); Put(data, "A.patch_1", "bbb"); Put(data, "A.patch_2", "ccc"); Put(data, "stale.patch_5", "zzzzzz");   // 6 bytes: matches no manifest size, so it is never hashed
        File.SetAttributes(Path.Combine(data, "stale.patch_5"), FileAttributes.ReadOnly);

        using var srv = new TinyHttp(System.Text.Encoding.UTF8.GetBytes("new0"));
        // r2 inserts a new mod at 0, shifting everything up; adds a byte-identical copy of bbb at 4 and an empty companion.
        var wanted = new List<PackFile>
        {
            F("A.patch_0", "new0", url: srv.Url), F("A.patch_1", "aaa"), F("A.patch_1.stream", ""), F("A.patch_2", "bbb"),
            F("A.patch_3", "ccc"), F("A.patch_4", "bbb"), F("A.patch_4.stream", ""),
        };
        var cache = new HashCache();
        long lastTotal = -1;
        var local = await Pack.InventoryAsync(game, wanted, cache, false, new SyncProgress<(long done, long total)>(p => lastTotal = p.total), CancellationToken.None);
        Check(local.Count, 5, "inventory lists the five local files");
        Check(local.Single(l => l.Name == "stale.patch_5").Sha is null, "a file whose size matches nothing is not hashed");
        Check(local.Single(l => l.Name == "A.patch_0.stream").Sha == PackFile.EmptySha, "an empty file gets the empty sha without reading");
        Check(lastTotal, 9L, "progress total = bytes actually hashed (3 files × 3 bytes)");

        var plan = Pack.Plan(game, wanted, local, new HashSet<string>());
        Check(plan.Downloads.Count, 1, "one download (the new file)");
        Check(plan.BytesToDownload, 4L, "4 bytes to download");
        Check(plan.RenameCount, 4, "four renames (aaa, stream, bbb, ccc)");
        Check(plan.CopyCount, 1, "one local copy for the duplicated bbb");
        Check(plan.CreateCount, 1, "one zero-byte create");
        Check(plan.ParkCount, 1, "one park (stale.patch_5)");
        Check(plan.InPlaceCount, 0, "nothing was already in place");
        Check(plan.Actions.All(a => a.Op != PlanOp.Finalize || a.From.EndsWith(ModFiles.StagedSuffix)), "every finalize comes from a staged name");
        Check(plan.Actions.Where(a => a.Op == PlanOp.Stage).All(a => !ModFiles.IsPatchFile(a.To)), "staged names never look like mods");
        var stageOrder = plan.Actions.Where(a => a.Op == PlanOp.Stage && ModFiles.TryParsePatchName(Path.GetFileName(a.From), out _, out _, out _))
            .Select(a => { ModFiles.TryParsePatchName(Path.GetFileName(a.From), out _, out var i, out _); return i; }).ToList();
        Check(stageOrder.SequenceEqual(stageOrder.OrderByDescending(i => i)), "stages run in descending patch order");
        var finOrder = plan.Actions.Where(a => a.Op == PlanOp.Finalize).Select(a => { ModFiles.TryParsePatchName(Path.GetFileName(a.To), out _, out var i, out _); return i; }).ToList();
        Check(finOrder.SequenceEqual(finOrder.OrderBy(i => i)), "finalizes run in ascending patch order");

        await Pack.DownloadPlanAsync(plan, dl, null, CancellationToken.None);
        Check(File.Exists(Path.Combine(dl, ShaOf("new0"))), "downloaded file is stored under its sha");
        var result = Pack.Apply(game, plan, planPath, "r2", cache, cachePath);
        Check(!result.Interrupted, "apply completed" + (result.Interrupted ? ": " + string.Join(";", result.Failed) + result.Reason : ""));
        Check(SameSet(Names(data).Where(ModFiles.IsPatchFile), wanted.Select(f => f.Name)), "data\\ holds exactly the manifest names");
        Check(File.ReadAllText(Path.Combine(data, "A.patch_1")) == "aaa" && File.ReadAllText(Path.Combine(data, "A.patch_4")) == "bbb" && File.ReadAllText(Path.Combine(data, "A.patch_0")) == "new0", "contents landed under the right names");
        Check(new FileInfo(Path.Combine(data, "A.patch_4.stream")).Length == 0, "zero-byte companion created");
        Check(File.Exists(Path.Combine(old, "stale.patch_5")) && (File.GetAttributes(Path.Combine(old, "stale.patch_5")) & FileAttributes.ReadOnly) != 0, "stale file parked (read-only flag left alone)");
        Check(ModFiles.ListStagedFiles(game).Length == 0 && !File.Exists(planPath), "no staged files, plan file gone");
        Check(!Directory.Exists(dl), "empty mods_download removed");
        Check(File.Exists(cachePath), "hash cache saved");

        // Second run: everything in place, cache hits, no-op.
        var cache2 = HashCache.Load(cachePath);
        long hashed = -1;
        var local2 = await Pack.InventoryAsync(game, wanted, cache2, false, new SyncProgress<(long done, long total)>(p => hashed = p.total), CancellationToken.None);
        Check(hashed, 0L, "second inventory hashes nothing (cache)");
        var plan2 = Pack.Plan(game, wanted, local2, new HashSet<string>());
        Check(plan2.IsNoOp && plan2.InPlaceCount == 7, "second plan is a no-op with every file in place");

        // Option off: the two 'bbb' files at 2 and 4 belong to an option; switching it off renumbers ccc down and parks them.
        foreach (var f in wanted.Where(f => f.Name is "A.patch_2" or "A.patch_4" or "A.patch_4.stream")) f.Option = "cmd";
        var pm = new PackManifest { Options = { new PackOption { Id = "cmd", Name = "X" } }, Files = wanted };
        var wantedOff = Pack.EffectiveFiles(pm, _ => false);
        Check(wantedOff.Select(f => f.Name).SequenceEqual(new[] { "A.patch_0", "A.patch_1", "A.patch_1.stream", "A.patch_2" }), "option off: ccc moves from 3 to 2");
        var local3 = await Pack.InventoryAsync(game, wantedOff, cache2, false, null, CancellationToken.None);
        var plan3 = Pack.Plan(game, wantedOff, local3, new HashSet<string>());
        Check(plan3.Downloads.Count == 0 && plan3.RenameCount == 1 && plan3.ParkCount == 3, "option off = one rename, three parks, no download");
        var r3 = Pack.Apply(game, plan3, planPath, "r2", cache2, cachePath);
        Check(!r3.Interrupted && SameSet(Names(data).Where(ModFiles.IsPatchFile), wantedOff.Select(f => f.Name)) && File.ReadAllText(Path.Combine(data, "A.patch_2")) == "ccc", "option off applied: gap-free, ccc at 2");
        // Option back on: the parked copies in mods_old\ are reused, still no download.
        var local4 = await Pack.InventoryAsync(game, wanted, cache2, false, null, CancellationToken.None);
        var plan4 = Pack.Plan(game, wanted, local4, new HashSet<string>());
        Check(plan4.Downloads.Count == 0 && plan4.Actions.Any(a => a.Op == PlanOp.Stage && a.From.StartsWith(old, StringComparison.OrdinalIgnoreCase)), "option on again: files come back from mods_old\\, nothing downloaded");
        var r4 = Pack.Apply(game, plan4, planPath, "r2", cache2, cachePath);
        Check(!r4.Interrupted && SameSet(Names(data).Where(ModFiles.IsPatchFile), wanted.Select(f => f.Name)), "option on applied");
    }

    static void SurplusAndParkNamingTests(string root)
    {
        Console.WriteLine("Duplicates: surplus local copies are parked; mods_old\\ never overwrites");
        var game = MakeGame(root, "Surplus Game");
        var data = Path.Combine(game, "data"); var old = Path.Combine(game, "mods_old");
        Put(data, "B.patch_0", "same"); Put(data, "B.patch_1", "same"); Put(old, "B.patch_1", "older");
        var wanted = new List<PackFile> { F("B.patch_0", "same") };
        var local = Pack.InventoryAsync(game, wanted, new HashCache(), false, null, CancellationToken.None).GetAwaiter().GetResult();
        var plan = Pack.Plan(game, wanted, local, new HashSet<string>());
        Check(plan.InPlaceCount == 1 && plan.ParkCount == 1 && plan.Downloads.Count == 0, "one in place, the twin parked");
        var r = Pack.Apply(game, plan, Path.Combine(root, "surplus-plan.json"), "v", null, null);
        Check(!r.Interrupted && File.ReadAllText(Path.Combine(old, "B.patch_1")) == "older" && File.Exists(Path.Combine(old, "B.patch_1.1")), "existing mods_old\\B.patch_1 kept; the parked twin became B.patch_1.1");
        Check(!ModFiles.IsPatchFile("B.patch_1.1"), "the .1 name is not a mod name");
        Check(ModFiles.TryParseParkedName("B.patch_1.gpu_resources.12", out var pn) && pn == "B.patch_1.gpu_resources", "a .N parked name maps back to its pack name");
        Check(!ModFiles.TryParseParkedName("B.patch_1", out _) && !ModFiles.TryParseParkedName("B.patch_1.stream", out _), "pristine names are not parked names");

        // The twin parked as B.patch_1.1 is still pack bytes: wanting B.patch_1 again must find it there instead of downloading
        // (this is what happens when a variant toggle is switched off, on, and off again: the same names get parked twice).
        var wanted2 = new List<PackFile> { F("B.patch_0", "same"), F("B.patch_1", "same") };
        var local2 = Pack.InventoryAsync(game, wanted2, new HashCache(), false, null, CancellationToken.None).GetAwaiter().GetResult();
        Check(local2.Any(l => l.Where == LocalWhere.Old && l.Name == "B.patch_1" && l.Path.EndsWith("B.patch_1.1") && l.Sha == ShaOf("same")), "inventory lists mods_old\\B.patch_1.1 under its pack name, hashed");
        var plan2 = Pack.Plan(game, wanted2, local2, new HashSet<string>());
        Check(plan2.Downloads.Count == 0 && plan2.Actions.Any(a => a.Op == PlanOp.Stage && a.From.EndsWith("B.patch_1.1")), "the .1 twin comes back by rename, nothing downloaded");
        var r2 = Pack.Apply(game, plan2, Path.Combine(root, "surplus-plan2.json"), "v", null, null);
        Check(!r2.Interrupted && File.ReadAllText(Path.Combine(data, "B.patch_1")) == "same" && !File.Exists(Path.Combine(old, "B.patch_1.1")) && File.ReadAllText(Path.Combine(old, "B.patch_1")) == "older", "B.patch_1 restored from the .1 twin; the unrelated mods_old\\B.patch_1 untouched");
    }

    static void InterruptedAndReplayTests(string root)
    {
        Console.WriteLine("Interrupted update: a locked old file leaves a staged copy and a plan; Finish update replays it offline");
        var game = MakeGame(root, "Interrupted Game");
        var data = Path.Combine(game, "data"); var dl = Path.Combine(game, "mods_download");
        var planPath = Path.Combine(root, "int-plan.json");
        Put(data, "C.patch_0", "old"); Put(data, "C.patch_1", "keep");
        var wanted = new List<PackFile> { F("C.patch_0", "fresh"), F("C.patch_1", "keep") };
        Directory.CreateDirectory(dl); File.WriteAllText(Path.Combine(dl, ShaOf("fresh")), "fresh");   // "already downloaded"
        var local = Pack.InventoryAsync(game, wanted, new HashCache(), false, null, CancellationToken.None).GetAwaiter().GetResult();
        var plan = Pack.Plan(game, wanted, local, new HashSet<string> { ShaOf("fresh") });
        Check(plan.Downloads.Count == 0 && plan.ParkCount == 1 && plan.InPlaceCount == 1, "complete download reused; old file to park; keep in place");
        ApplyResult r;
        using (new FileStream(Path.Combine(data, "C.patch_0"), FileMode.Open, FileAccess.Read, FileShare.None))
            r = Pack.Apply(game, plan, planPath, "v2", null, null);
        Check(r.Interrupted && r.Failed.Count >= 1, "locked old file → interrupted");
        Check(File.ReadAllText(Path.Combine(data, "C.patch_0")) == "old", "the occupied final name was NOT overwritten");
        Check(ModFiles.HasStagedFiles(game) && File.Exists(planPath), "staged copy and plan file remain");
        Check(ModFiles.GetState(game) == ModState.On, "the game still sees a consistent (old) set");
        var r2 = Pack.Replay(game, planPath, null, null);
        Check(r2 is not null && !r2.Interrupted, "replay finished once the lock was gone");
        Check(File.ReadAllText(Path.Combine(data, "C.patch_0")) == "fresh" && !ModFiles.HasStagedFiles(game) && !File.Exists(planPath), "fresh file in place, staged and plan gone");
        Check(File.Exists(Path.Combine(game, "mods_old", "C.patch_0")), "old file parked");
        Check(Pack.Replay(game, planPath, null, null) is null, "replay with no plan file returns null (caller re-plans)");
        Check(Pack.Replay(game, Path.Combine(root, "nope.json"), null, null) is null, "missing plan → null");
    }

    static void HashCacheAndCleanupTests(string root)
    {
        Console.WriteLine("Hash cache keys survive a move; download-folder cleanup keeps only wanted shas");
        var dir = Path.Combine(root, "cache"); Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "X.patch_0"); File.WriteAllText(a, "hello");
        var fi = new FileInfo(a);
        var k1 = HashCache.Key(fi.Name, fi.Length, fi.LastWriteTimeUtc);
        var sub = Path.Combine(dir, "off"); Directory.CreateDirectory(sub);
        File.Move(a, Path.Combine(sub, "X.patch_0"));
        var fi2 = new FileInfo(Path.Combine(sub, "X.patch_0"));
        Check(HashCache.Key(fi2.Name, fi2.Length, fi2.LastWriteTimeUtc) == k1, "same key after moving to another folder");
        File.WriteAllText(fi2.FullName, "hello!"); fi2.Refresh();
        Check(HashCache.Key(fi2.Name, fi2.Length, fi2.LastWriteTimeUtc) != k1, "different size → different key");
        var c = new HashCache(); c.Entries[k1] = "deadbeef";
        var cp = Path.Combine(dir, "h.json"); c.Save(cp);
        Check(HashCache.Load(cp).Entries.TryGetValue(k1.ToUpperInvariant(), out var v) && v == "deadbeef", "cache round-trips and keys are case-insensitive");

        var dl = Path.Combine(root, "dlclean"); Directory.CreateDirectory(dl);
        var keep = new string('a', 64);
        File.WriteAllText(Path.Combine(dl, keep), "x"); File.WriteAllText(Path.Combine(dl, keep + Pack.PartialSuffix), "x");
        File.WriteAllText(Path.Combine(dl, "clonedivers-pack-2026.09.05-r3-part1.zip"), "x"); File.WriteAllText(Path.Combine(dl, new string('b', 64)), "x");
        Check(Pack.CleanDownloadFolder(dl, new[] { keep }), 2, "removed the zip partial and the unwanted sha");
        Check(SameSet(Names(dl), new[] { keep, keep + Pack.PartialSuffix }), "kept the wanted sha and its partial");
    }

    static void SelfUpdateAndAcfTests(string root)
    {
        Console.WriteLine("Self-update paths and swap; Steam acf parsing");
        var (exe, upd, old) = SelfUpdate.Paths(@"C:\Users\José\Desktop\Clonedivers (1).exe");
        Check(upd == @"C:\Users\José\Desktop\Clonedivers (1).update.exe" && old == @"C:\Users\José\Desktop\Clonedivers (1).old.exe", "update/old paths derive from the stem, same folder");

        var dir = Path.Combine(root, "swap é % test"); Directory.CreateDirectory(dir);
        var e = Path.Combine(dir, "Clonedivers (1).exe"); var (_, u, o) = SelfUpdate.Paths(e);
        File.WriteAllText(e, "v1"); File.WriteAllText(u, "v2");
        SelfUpdate.Swap(e, u, o, TimeSpan.FromMilliseconds(200));
        Check(File.ReadAllText(e) == "v2" && File.ReadAllText(o) == "v1" && !File.Exists(u), "swap: new exe in place, old kept as .old.exe");
        File.WriteAllText(e, "v2"); File.Delete(o);   // now a swap whose update file is missing must roll back
        Exception? ex = null;
        try { SelfUpdate.Swap(e, u, o, TimeSpan.FromMilliseconds(200)); } catch (Exception x) { ex = x; }
        Check(ex is not null && File.ReadAllText(e) == "v2" && !File.Exists(o), "failed swap rolled back: exe restored, no .old.exe left");

        var acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"553850\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"buildid\"\t\t\"24826606\"\n\t\"TargetBuildID\"\t\t\"24826606\"\n}\n";
        var info = SteamAcf.Parse(acf);
        Check(info.BuildId == "24826606" && info.TargetBuildId == "24826606" && info.StateFlags == "4", "buildid / TargetBuildID / StateFlags parsed from tab-separated acf");
        Check(!info.UpdatePending, "same target → no pending update");
        Check(SteamAcf.Parse(acf.Replace("\"TargetBuildID\"\t\t\"24826606\"", "\"TargetBuildID\"\t\t\"24900000\"")).UpdatePending, "different target → pending");
        Check(!SteamAcf.Parse(acf.Replace("\"TargetBuildID\"\t\t\"24826606\"", "\"TargetBuildID\"\t\t\"0\"")).UpdatePending, "target 0 → not pending");
        Check(SteamAcf.Parse("nothing here").BuildId is null, "missing keys → null");
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
