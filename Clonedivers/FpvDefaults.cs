using System.Security.Cryptography;
using System.Text;

namespace Clonedivers;

/// <summary>Missing defaults for the exact, unchanged FPV archive shipped with r20. Existing player choices win.</summary>
public static class FpvDefaults
{
    internal const string CameraSha256 = "53d5cf890d8fc654c9e6e7970d855a73142695a35f08c339ced3b9abd35749e5";
    internal const long CameraSize = 90214;
    internal const int MaximumValuesBytes = 1024 * 1024;
    const string Enabled = "firstperson.enabled", Bridge = "firstperson.bridge2";
    static readonly UTF8Encoding Utf8 = new(false, true);
    internal sealed record ArchiveIdentity(string Name, long Size, string Sha256);
    static readonly ArchiveIdentity[] Dependencies =
    {
        new("ModOptions", 139136, "ce6229cffe8c78706a37293ac64f553becb7f3816aa0cc6fbb108d74956b68a5"),
        new("Bingus loader", 41616, "295488f31ff583118bf9f3dbe05c096ddbcdd493bde33513b3219e683d3f08ea"),
    };

    /// <summary>Matches the author's loader.log_directory: getenv('LOCALAPPDATA')/CowboyBingus/Helldivers2/Logs.</summary>
    public static string ValuesPath()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local) || !Path.IsPathFullyQualified(local))
            throw new InvalidOperationException("LOCALAPPDATA is missing or invalid. Start Clonedivers from your normal Windows account.");
        return Path.Combine(local, "CowboyBingus", "Helldivers2", "Logs", "ModOptionsMenu.values");
    }

    public static bool EnsureForInstalledPack(string? gameDir)
    {
        if (ModFiles.GetState(gameDir) != ModState.On) return false;
        // Resolve the values path only after recognizing installed FPV: vanilla/non-FPV never touch this setting.
        return EnsureForInstalledPack(gameDir!, ValuesPath, CameraSize, CameraSha256,
            InstallReceipt.Load(Settings.ReceiptPathFor(gameDir!), gameDir!)?.Files.Select(f => f.File).ToList(), dependencies: Dependencies);
    }

    // Explicit paths and archive identity keep regression tests away from the player's real game/options.
    internal static bool EnsureForInstalledPack(string gameDir, Func<string> valuesPath, long cameraSize, string cameraSha,
        IReadOnlyList<PackFile>? installedFiles = null, Action? beforeCommit = null, Action? beforeReplace = null,
        IReadOnlyList<ArchiveIdentity>? dependencies = null)
    {
        if (ModFiles.GetState(gameDir) != ModState.On) return false;
        if (Game.IsRunning()) throw new InvalidOperationException("Close Helldivers 2 before preparing the FPV defaults.");
        var data = Path.Combine(gameDir, ModFiles.DataFolder);
        RejectLinks(data);
        var expected = installedFiles?.Where(f => f.Size == cameraSize && f.Sha256.Equals(cameraSha, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var path in ModFiles.ListPatchFiles(data))
        {
            if (!ModFiles.TryParsePatchName(Path.GetFileName(path), out _, out _, out var companion) || companion.Length != 0) continue;
            var pinned = expected?.Any(f => f.Name.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) == true;
            if (new FileInfo(path).Length != cameraSize)
            {
                if (pinned) throw new InvalidDataException("The installed FPV archive changed. Verify the pack before launching.");
                continue;
            }
            FileSafety.RejectLink(path);
            // Deny writes/deletion while the recognized camera is being prepared. A feed URL cannot opt into this code.
            using var archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(SHA256.HashData(archive));
            if (!hash.Equals(cameraSha, StringComparison.OrdinalIgnoreCase))
            {
                if (pinned) throw new InvalidDataException("The installed FPV archive changed. Verify the pack before launching.");
                continue;
            }
            var pinnedDependencies = new List<FileStream>();
            try
            {
                foreach (var dependency in dependencies ?? Array.Empty<ArchiveIdentity>())
                    pinnedDependencies.Add(OpenDependency(data, dependency));
                return Seed(valuesPath(), beforeCommit, beforeReplace);
            }
            finally { foreach (var dependency in pinnedDependencies) dependency.Dispose(); }
        }
        if (expected is { Count: > 0 }) throw new InvalidDataException("The installed FPV archive is missing. Verify the pack before launching.");
        return false;
    }

    static FileStream OpenDependency(string data, ArchiveIdentity identity)
    {
        foreach (var path in ModFiles.ListPatchFiles(data))
        {
            if (!ModFiles.TryParsePatchName(Path.GetFileName(path), out _, out _, out var companion) || companion.Length != 0 ||
                new FileInfo(path).Length != identity.Size) continue;
            RejectLinks(path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (Convert.ToHexString(SHA256.HashData(stream)).Equals(identity.Sha256, StringComparison.OrdinalIgnoreCase)) return stream;
            }
            catch { stream.Dispose(); throw; }
            stream.Dispose();
        }
        throw new InvalidDataException($"FPV's {identity.Name} addon is missing or damaged. Verify the pack before launching.");
    }

    sealed record Snapshot(byte[]? Bytes, long WriteTicks)
    {
        public bool Same(Snapshot other) => WriteTicks == other.WriteTicks &&
            (Bytes is null ? other.Bytes is null : other.Bytes is not null && Bytes.AsSpan().SequenceEqual(other.Bytes));
    }

    static void RejectLinks(string path)
    {
        for (var part = Path.GetFullPath(path); !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
            FileSafety.RejectLink(part);
    }

    static Snapshot Read(string path)
    {
        RejectLinks(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumValuesBytes) throw new InvalidDataException("The options file exceeds 1 MiB.");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new IOException("The options file changed while being read.");
            return new(bytes, File.GetLastWriteTimeUtc(path).Ticks);
        }
        catch (FileNotFoundException) { return new(null, 0); }
        catch (DirectoryNotFoundException) { return new(null, 0); }
    }

    static Dictionary<string, string> Parse(byte[]? bytes)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bytes is null) return values;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            throw new InvalidDataException("Save the options file as UTF-8 without a byte-order mark; the mod reads the mark as part of its first key.");
        var text = Utf8.GetString(bytes);
        if (text.Contains('\0')) throw new InvalidDataException("The options file contains invalid text.");
        foreach (var line in text.Split('\n'))
        {
            var item = line.EndsWith('\r') ? line[..^1] : line;
            if (item.Length == 0) continue;
            var tab = item.IndexOf('\t');
            if (tab <= 0 || item.IndexOf('\t', tab + 1) >= 0 || item.Contains('\r'))
                throw new InvalidDataException("Each options line must contain one key, one TAB and its value.");
            var key = item[..tab];
            if (!values.TryAdd(key, item[(tab + 1)..])) throw new InvalidDataException($"The options file repeats '{key}'. Resolve the duplicate before launching.");
        }
        if (values.TryGetValue(Enabled, out var enabled) && enabled is not ("true" or "false"))
            throw new InvalidDataException("firstperson.enabled must be exactly true or false.");
        if (values.TryGetValue(Bridge, out var bridge) &&
            (!double.TryParse(bridge, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var choice) || choice is not (1 or 2)))
            throw new InvalidDataException("firstperson.bridge2 must be choice 1 (Off) or 2 (On).");
        return values;
    }

    static bool Seed(string path, Action? beforeCommit, Action? beforeReplace)
    {
        path = Path.GetFullPath(path);
        var backup = path + ".clonedivers-seed-backup";
        var mutexName = "Local\\Clonedivers-FpvDefaults-" + Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(path.ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        bool held;
        try { held = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("Another launcher is preparing FPV options. Close it and try again.");
        string? temporary = null;
        try
        {
            RejectLinks(path); RejectLinks(backup);
            if (File.Exists(backup)) throw new IOException($"A previous options update needs review. Compare {path} with {backup}, preserve your choices, then remove the seed backup and try again.");
            var original = Read(path);
            var values = Parse(original.Bytes);
            Snapshot? recovered = null;
            var basis = original.Bytes;
            if (values.Count == 0)
            {
                recovered = Read(path + ".bak");
                var saved = Parse(recovered.Bytes);
                if (saved.Count > 0) { values = saved; basis = recovered.Bytes; }
            }
            var additions = new List<string>();
            if (!values.ContainsKey(Enabled)) additions.Add(Enabled + "\tfalse");
            if (!values.ContainsKey(Bridge)) additions.Add(Bridge + "\t1");
            if (additions.Count == 0 && ReferenceEquals(basis, original.Bytes)) return false;
            var suffix = additions.Count == 0 ? "" : (basis is { Length: > 0 } && basis[^1] != (byte)'\n' ? "\n" : "") + string.Join('\n', additions) + "\n";
            var next = (basis ?? Array.Empty<byte>()).Concat(Utf8.GetBytes(suffix)).ToArray();
            if (next.Length > MaximumValuesBytes) throw new InvalidDataException("Adding FPV defaults would exceed the 1 MiB options limit.");
            var directory = Path.GetDirectoryName(path)!;
            RejectLinks(directory); Directory.CreateDirectory(directory); RejectLinks(directory);
            temporary = path + ".clonedivers-" + Guid.NewGuid().ToString("N") + ".tmp";
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(next); output.Flush(flushToDisk: true); }
            beforeCommit?.Invoke();
            RejectLinks(path); RejectLinks(temporary); RejectLinks(backup);
            if (!original.Same(Read(path)) || recovered is not null && !recovered.Same(Read(path + ".bak")))
                throw new IOException("The options file changed during preparation. Your newer file was left untouched; try again with the game and other editors closed.");
            beforeReplace?.Invoke();
            if (original.Bytes is null) File.Move(temporary, path); // Create only: a concurrent new file wins.
            else
            {
                // Replace atomically, preserving even a late competing writer's complete old bytes in a guarded backup.
                File.Replace(temporary, path, backup);
                if (!original.Same(Read(backup))) throw new IOException($"The options file changed during replacement. Launch stopped; reconcile your saved choices in {backup} with {path} before removing the seed backup.");
            }
            temporary = null;
            if (!Read(path).Bytes!.AsSpan().SequenceEqual(next)) throw new IOException($"The options file changed after replacement. Launch stopped; review {path} and {backup} before trying again.");
            if (original.Bytes is not null) { RejectLinks(backup); File.Delete(backup); }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            throw new IOException($"FPV options could not be prepared safely: {path}\n{ex.Message}\nClose Helldivers 2 and other editors, correct this options file (keep any saved choices), then try again.", ex);
        }
        finally
        {
            if (temporary is not null)
            {
                try { FileSafety.RejectLink(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
            }
            mutex.ReleaseMutex();
        }
    }
}
