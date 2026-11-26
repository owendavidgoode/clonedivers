using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clonedivers;

/// <summary>
/// Deep streaming I/O and hash-verification engine shared by Windows, Linux, and ForgeBridge.
/// Eliminates Large Object Heap allocations via 1 MiB ArrayPool buffers, merges copy + SHA-256
/// into a single disk pass, accelerates NVMe pack scans via bounded-parallel SequentialScan I/O
/// plus a racy-clean-guarded metadata hash cache, and unifies cross-platform volume capacity checks.
/// </summary>
public static class PackArtifactEngine
{
    public const int BufferSize = 1 << 20; // 1 MiB pooled buffer (zero LOH churn)
    public const int DefaultParallelism = 4;
    public const string HashCacheFileName = "artifact-hashes.json";
    const int MaxCachedEntries = 8192;
    static readonly long RacyGuardTicks = TimeSpan.FromSeconds(2).Ticks;
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };

    public sealed record CachedArtifactStamp(
        long Size,
        long LastWriteUtcTicks,
        long CreationUtcTicks,
        string Sha256,
        long VerifiedUtcTicks);

    sealed class ArtifactHashStoreFile
    {
        public int Format { get; set; } = 1;
        public Dictionary<string, CachedArtifactStamp> Entries { get; set; } = new(PlatformPaths.Comparer);
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default, IProgress<long>? bytes = null)
    {
        FileSafety.RejectLink(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length == 0)
        {
            bytes?.Report(0);
            return PackFile.EmptySha;
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long done = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
                done += read;
                bytes?.Report(done);
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> to a brand-new <paramref name="target"/> while computing its SHA-256
    /// inline in a single sequential pass, eliminating redundant post-copy and post-backup multi-GB re-reads.
    /// </summary>
    public static async Task<string> CopyAndHashAsync(
        string source,
        string target,
        long expectedSize,
        CancellationToken ct = default,
        Action<long>? onBytes = null)
    {
        FileSafety.RejectLink(source);
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != expectedSize)
            throw new InvalidDataException("Reusable pack content changed size; prepare again.");

        await using var output = new FileStream(
            target,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (expectedSize == 0)
        {
            onBytes?.Invoke(0);
            return PackFile.EmptySha;
        }

        const int ProgressSliceBytes = 65536;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (count > expectedSize - total)
                    throw new InvalidDataException("Pack content exceeds catalog size.");
                sha.AppendData(buffer, 0, count);
                if (onBytes == null || count <= ProgressSliceBytes)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                    total += count;
                    onBytes?.Invoke(total);
                }
                else
                {
                    for (int offset = 0; offset < count; offset += ProgressSliceBytes)
                    {
                        int chunk = Math.Min(ProgressSliceBytes, count - offset);
                        await output.WriteAsync(buffer.AsMemory(offset, chunk), ct).ConfigureAwait(false);
                        total += chunk;
                        onBytes.Invoke(total);
                    }
                }
            }
            if (total != expectedSize)
                throw new InvalidDataException("Pack content is shorter than its catalog size.");

            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task CopyExactStreamAsync(
        Stream input,
        Stream output,
        long expectedSize,
        CancellationToken ct = default,
        Action<long>? onBytes = null)
    {
        const int ProgressSliceBytes = 65536;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                if (count > expectedSize - total)
                    throw new InvalidDataException("Pack content exceeds catalog size.");
                if (onBytes == null || count <= ProgressSliceBytes)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                    total += count;
                    onBytes?.Invoke(total);
                }
                else
                {
                    for (int offset = 0; offset < count; offset += ProgressSliceBytes)
                    {
                        int chunk = Math.Min(ProgressSliceBytes, count - offset);
                        await output.WriteAsync(buffer.AsMemory(offset, chunk), ct).ConfigureAwait(false);
                        total += chunk;
                        onBytes.Invoke(total);
                    }
                }
            }
            if (total != expectedSize)
                throw new InvalidDataException("Pack content is shorter than its catalog size.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Verifies all files in <paramref name="expectedFiles"/> inside <paramref name="folder"/> using
    /// bounded-parallel SequentialScan 1 MiB reads and monotonic aggregate progress reporting.
    /// </summary>
    public static async Task VerifyPackFilesAsync(
        string folder,
        IReadOnlyList<PackFile> expectedFiles,
        IProgress<(long Done, long Total)>? bytes = null,
        CancellationToken ct = default,
        int maxParallelism = DefaultParallelism)
    {
        long total = expectedFiles.Sum(f => f.Size);
        bytes?.Report((0, total));
        if (expectedFiles.Count == 0) return;

        // Pre-validate links and exact lengths upfront for deterministic immediate failure on missing/resized files.
        var workItems = new List<(PackFile File, string FullPath)>(expectedFiles.Count);
        foreach (var file in expectedFiles)
        {
            ct.ThrowIfCancellationRequested();
            var path = FileSafety.Child(folder, file.Name);
            FileSafety.RejectLink(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size)
                throw new InvalidDataException($"Pack file changed: {file.Name}. Prepare the pack again.");
            workItems.Add((file, path));
        }

        long completedBytes = 0;
        var progressGate = new object();
        long highestReported = 0;
        void ReportDelta(long delta)
        {
            if (bytes == null || delta <= 0) return;
            var current = Interlocked.Add(ref completedBytes, delta);
            lock (progressGate)
            {
                var clamped = Math.Clamp(current, 0, total);
                if (clamped >= highestReported)
                {
                    highestReported = clamped;
                    bytes.Report((clamped, total));
                }
            }
        }

        await Parallel.ForEachAsync(
            workItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(maxParallelism, 1, 8),
                CancellationToken = ct
            },
            async (item, token) =>
            {
                if (item.File.Size == 0)
                {
                    if (!string.Equals(PackFile.EmptySha, item.File.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Pack file changed: {item.File.Name}. Prepare the pack again.");
                    return;
                }

                long prevFileBytes = 0;
                var progress = bytes == null
                    ? null
                    : new SyncProgress<long>(done =>
                    {
                        var clamped = Math.Clamp(done, 0, item.File.Size);
                        var delta = clamped - prevFileBytes;
                        if (delta > 0)
                        {
                            prevFileBytes = clamped;
                            ReportDelta(delta);
                        }
                    });

                var actual = await HashFileAsync(item.FullPath, token, progress).ConfigureAwait(false);
                if (!string.Equals(actual, item.File.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Pack file changed: {item.File.Name}. Prepare the pack again.");

                var remainder = item.File.Size - prevFileBytes;
                if (remainder > 0) ReportDelta(remainder);
            }).ConfigureAwait(false);

        bytes?.Report((total, total));
    }

    public static ConcurrentDictionary<string, CachedArtifactStamp> LoadHashCache(string? cacheFilePath)
    {
        var map = new ConcurrentDictionary<string, CachedArtifactStamp>(PlatformPaths.Comparer);
        if (string.IsNullOrWhiteSpace(cacheFilePath)) return map;
        try
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(cacheFilePath))!); dir != null; dir = dir.Parent)
                FileSafety.RejectLink(dir.FullName);
            FileSafety.RejectLink(cacheFilePath);
            if (!File.Exists(cacheFilePath) || new FileInfo(cacheFilePath).Length > 4 * 1024 * 1024)
                return map;

            var parsed = JsonSerializer.Deserialize<ArtifactHashStoreFile>(File.ReadAllText(cacheFilePath, Encoding.UTF8), CacheJson);
            if (parsed?.Format != 1 || parsed.Entries == null) return map;
            foreach (var (k, v) in parsed.Entries)
            {
                if (!string.IsNullOrWhiteSpace(k) && v != null && v.Size >= 0 && FileSafety.IsSha256(v.Sha256))
                    map[k] = v;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
        }
        return map;
    }

    public static void SaveHashCache(string? cacheFilePath, ConcurrentDictionary<string, CachedArtifactStamp> entries)
    {
        if (string.IsNullOrWhiteSpace(cacheFilePath) || entries.IsEmpty) return;
        try
        {
            var full = Path.GetFullPath(cacheFilePath);
            var parentDir = Path.GetDirectoryName(full)!;
            for (var dir = new DirectoryInfo(parentDir); dir != null; dir = dir.Parent)
                FileSafety.RejectLink(dir.FullName);
            Directory.CreateDirectory(parentDir);
            FileSafety.RejectLink(full);

            var trimmed = entries
                .Where(kv => FileSafety.IsSha256(kv.Value.Sha256))
                .OrderByDescending(kv => kv.Value.VerifiedUtcTicks)
                .Take(MaxCachedEntries)
                .ToDictionary(kv => kv.Key, kv => kv.Value, PlatformPaths.Comparer);

            var temp = FileSafety.Child(parentDir, $"{HashCacheFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                var payload = Utf8NoBom.GetBytes(JsonSerializer.Serialize(new ArtifactHashStoreFile { Format = 1, Entries = trimmed }, CacheJson));
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(payload, 0, payload.Length);
                    fs.Flush(flushToDisk: true);
                }
                FileSafety.RejectLink(full);
                File.Move(temp, full, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
        }
    }

    public static async Task<string> HashWithMetadataCacheAsync(
        string path,
        ConcurrentDictionary<string, CachedArtifactStamp>? cache,
        CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        FileSafety.RejectLink(full);
        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("Candidate pack file disappeared.", full);

        long size = info.Length;
        long mtime = info.LastWriteTimeUtc.Ticks;
        long ctime = info.CreationTimeUtc.Ticks;
        long nowTicks = DateTime.UtcNow.Ticks;
        bool safeFromRacyWrite = mtime <= nowTicks - RacyGuardTicks && ctime <= nowTicks - RacyGuardTicks;

        if (cache != null && safeFromRacyWrite && cache.TryGetValue(full, out var existing) &&
            existing.Size == size &&
            existing.LastWriteUtcTicks == mtime &&
            existing.CreationUtcTicks == ctime &&
            existing.VerifiedUtcTicks >= mtime + RacyGuardTicks &&
            FileSafety.IsSha256(existing.Sha256))
        {
            return existing.Sha256;
        }

        var computed = await HashFileAsync(full, ct).ConfigureAwait(false);
        if (cache != null && safeFromRacyWrite)
        {
            // Re-stat after hashing so a concurrent writer during read can never poison the cache.
            var after = new FileInfo(full);
            if (after.Exists && after.Length == size && after.LastWriteTimeUtc.Ticks == mtime && after.CreationTimeUtc.Ticks == ctime)
                cache[full] = new CachedArtifactStamp(size, mtime, ctime, computed, DateTime.UtcNow.Ticks);
        }
        return computed;
    }

    public static void RecordVerifiedFileInCache(
        string path,
        long expectedSize,
        string verifiedSha256,
        ConcurrentDictionary<string, CachedArtifactStamp>? cache)
    {
        if (cache == null || !FileSafety.IsSha256(verifiedSha256)) return;
        try
        {
            var full = Path.GetFullPath(path);
            var info = new FileInfo(full);
            if (!info.Exists || info.Length != expectedSize) return;
            long nowTicks = DateTime.UtcNow.Ticks;
            if (info.LastWriteTimeUtc.Ticks <= nowTicks - RacyGuardTicks && info.CreationTimeUtc.Ticks <= nowTicks - RacyGuardTicks)
            {
                cache[full] = new CachedArtifactStamp(expectedSize, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks, verifiedSha256.ToLowerInvariant(), nowTicks);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    /// <summary>
    /// Unified upfront volume-capacity check shared by Windows (<see cref="HellForgeClient"/>) and Linux (<see cref="LinuxForgePlayer"/>)
    /// so staging + backup + replacement reserves are verified together before writing a multi-GB export.
    /// </summary>
    public static void EnsureStagingAndInstallCapacity(
        string installRoot,
        string cacheRoot,
        HellForgePack.Plan plan,
        long existingInstalledPackBytes,
        Func<string, long?> availableBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(availableBytes);
        ct.ThrowIfCancellationRequested();

        long existingInOutput = 0;
        if (Directory.Exists(plan.Output))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(plan.Output, "*", SearchOption.AllDirectories))
                {
                    FileSafety.RejectLink(f);
                    existingInOutput = checked(existingInOutput + new FileInfo(f).Length);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }

        long exportNewBytes = Directory.Exists(plan.Output)
            ? Math.Max(0L, plan.Bytes - existingInOutput)
            : plan.Bytes;

        long exportNeeded = exportNewBytes > 0
            ? checked(exportNewBytes + HellForgePack.ExportReserve + plan.Missing.Select(f => f.Size).DefaultIfEmpty().Max())
            : 0L;

        long replacementBytes = checked(plan.Bytes + 65536L);
        long backupNeeded = checked(existingInstalledPackBytes + replacementBytes + HellForgePack.ExportReserve);
        long modsNeeded = checked(replacementBytes + HellForgePack.ExportReserve);

        var exportPath = Path.GetFullPath(plan.Output);
        var backupPath = Path.Combine(cacheRoot, "backups");
        var modsPath = Path.Combine(installRoot, "mods");

        var exportVol = HellForgePack.VolumeKey(exportPath);
        var backupVol = HellForgePack.VolumeKey(backupPath);
        var modsVol = HellForgePack.VolumeKey(modsPath);

        var volumeNeeds = new Dictionary<string, (string ProbePath, long Required)>(StringComparer.Ordinal);
        void RequireOnVolume(string vol, string probe, long addBytes, bool additive)
        {
            if (addBytes <= 0) return;
            if (volumeNeeds.TryGetValue(vol, out var cur))
                volumeNeeds[vol] = (cur.ProbePath, additive ? checked(cur.Required + addBytes) : Math.Max(cur.Required, addBytes));
            else
                volumeNeeds[vol] = (probe, addBytes);
        }

        RequireOnVolume(backupVol, backupPath, backupNeeded, additive: false);
        RequireOnVolume(modsVol, modsPath, modsNeeded, additive: false);
        if (exportNeeded > 0)
            RequireOnVolume(exportVol, exportPath, exportNeeded, additive: true);

        bool cleanedOnce = false;
        foreach (var (_, (probe, required)) in volumeNeeds)
        {
            if (availableBytes(probe) is not long free || free >= required) continue;
            if (!cleanedOnce)
            {
                cleanedOnce = true;
                try { ForgeDiskManager.CleanSafeFilesSync(installRoot, ct, plan.Local.Values); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
                if (availableBytes(probe) is not long freeAfter || freeAfter >= required) continue;
                free = freeAfter;
            }
            throw new IOException(
                $"Not enough space to prepare and install this Star Wars pack: {Pack.FormatBytes(required)} needed " +
                $"({Pack.FormatBytes(exportNeeded)} staging + {Pack.FormatBytes(backupNeeded)} backup/install reserve), " +
                $"{Pack.FormatBytes(Math.Max(0, free))} available. Free space on this drive or choose a lighter Performance Tier, then try again.");
        }
    }
}
