using System.Security.Cryptography;
using System.Text;
using Clonedivers;

namespace Clonedivers.ForgeBridge.Tests;

internal static class ExportTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "clonedivers-export-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            check(HellForgePack.ContainingVolume("/mnt/mods-out/export", ["/", "/mnt/mods", "/mnt/mods-out"], false) == "/mnt/mods-out",
                "capacity checks choose the containing mounted volume rather than a similarly named neighbor");
            check(HellForgePack.ContainingVolume("/mnt/游戏 files/export", ["/", "/mnt/游戏 files"], false) == "/mnt/游戏 files" &&
                HellForgePack.ContainingVolume("d:\\Mods\\export", ["C:\\", "D:\\"], true) == "D:\\",
                "volume routing preserves Unicode/spaces and Windows drive-letter case conventions");
            var bytes = Encoding.UTF8.GetBytes("pack fixture");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var source = Path.Combine(root, "source.bin"); await File.WriteAllBytesAsync(source, bytes);
            var files = new List<PackFile> {
                new() { Name = "a.patch_0", Size = bytes.Length, Sha256 = hash },
                new() { Name = "b.patch_0", Size = bytes.Length, Sha256 = hash }
            };
            var selection = new HellForgePack.Selection("clonedivers", "full", "fixture", HellForgePack.SupportedBuild, new string('a', 64), [], files);
            HellForgePack.Plan Plan(string name) => new(selection, Path.Combine(root, name), new() { [hash] = source });
            async Task<bool> Refuses(Func<Task> action) { try { await action(); return false; } catch (Exception error) when (error is IOException or InvalidDataException) { return true; } }
            var cancelled = Plan("cancelled"); bool cancelledBeforeWrite = false;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await HellForgePack.ExportAsync(cancelled, false, null, cancellation.Token, _ => throw new Exception("capacity probe should not run")); }
                catch (OperationCanceledException) { cancelledBeforeWrite = true; }
            }
            check(cancelledBeforeWrite && !Directory.Exists(cancelled.Output), "an already cancelled preparation creates no staging folder or disk probe");
            var low = Plan("low-space");
            check(await Refuses(() => HellForgePack.ExportAsync(low, false, null, default, _ => 1)) && !Directory.Exists(low.Output),
                "insufficient capacity is rejected before any export files are written");
            var duplicate = Plan("duplicate-space");
            check(await Refuses(() => HellForgePack.ExportAsync(duplicate, false, null, default, _ => HellForgePack.ExportReserve + bytes.Length)) && !Directory.Exists(duplicate.Output),
                "equal-hash targets still require space for two independent copies");
            var exact = Plan("exact-space");
            await HellForgePack.ExportAsync(exact, false, null, default, _ => HellForgePack.ExportReserve + 2 * bytes.Length);
            await HellForgePack.VerifyAsync(exact.Output, selection);
            check(files.All(f => File.ReadAllBytes(Path.Combine(exact.Output, f.Name)).SequenceEqual(bytes)),
                "an exact sufficient budget creates two independent hash-verified targets and a complete receipt");
            var unknown = Plan("unknown-capacity");
            await HellForgePack.ExportAsync(unknown, false, null, default, _ => null);
            check(File.Exists(Path.Combine(unknown.Output, HellForgePack.ReceiptName)), "an unavailable capacity reading does not invent a full disk or bypass content verification");
            var real = Plan("real-volume"); await HellForgePack.ExportAsync(real, false);
            check(File.Exists(Path.Combine(real.Output, HellForgePack.ReceiptName)), "the host's real containing-volume probe supports an ordinary local export");
            await File.WriteAllBytesAsync(source, bytes.Concat(new byte[] { 1 }).ToArray());
            var grown = Plan("grown-source");
            check(await Refuses(() => HellForgePack.ExportAsync(grown, false, null, default, _ => long.MaxValue)) && !File.Exists(Path.Combine(grown.Output, files[0].Name)) && !File.Exists(Path.Combine(grown.Output, HellForgePack.ReceiptName)),
                "a reusable source that grew after planning cannot write an oversized target or completion receipt");
            await File.WriteAllBytesAsync(source, Enumerable.Repeat((byte)'x', bytes.Length).ToArray());
            var tampered = Plan("tampered-source");
            check(await Refuses(() => HellForgePack.ExportAsync(tampered, false, null, default, _ => long.MaxValue)) && !File.Exists(Path.Combine(tampered.Output, HellForgePack.ReceiptName)) && !File.Exists(Path.Combine(tampered.Output, "manifest.json")),
                "same-size source tampering cannot publish an importable cosmetic manifest or completion receipt");
            using var overInput = new MemoryStream(new byte[4]); using var overOutput = new MemoryStream();
            check(await Refuses(() => HellForgePack.CopyExactAsync(overInput, overOutput, 3, default)) && overOutput.Length <= 3,
                "a stream cannot write beyond the catalog's byte limit");
            using var shortInput = new MemoryStream(new byte[2]); using var shortOutput = new MemoryStream();
            check(await Refuses(() => HellForgePack.CopyExactAsync(shortInput, shortOutput, 3, default)), "a truncated stream cannot count as a completed copy");
            using var midCancellation = new CancellationTokenSource(); using var midInput = new CancelAfterReadStream(midCancellation); using var midOutput = new MemoryStream();
            bool stopped = false;
            try { await HellForgePack.CopyExactAsync(midInput, midOutput, 16, midCancellation.Token); } catch (OperationCanceledException) { stopped = true; }
            check(stopped && midOutput.Length == 0, "cancellation between an asynchronous read and write stops the copy immediately");
            await ResumeTests(root, check);
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("clonedivers-export-io-", StringComparison.Ordinal)) throw new IOException("Fixture cleanup escaped its temporary root.");
            Directory.Delete(resolved, true);
        }
    }
    static async Task ResumeTests(string root, Action<bool, string> check)
    {
        var exports = Path.Combine(root, "cache", "exports"); Directory.CreateDirectory(exports);
        byte[] first = Encoding.UTF8.GetBytes("first verified pack file"), second = Encoding.UTF8.GetBytes("second verified pack file");
        string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var files = new List<PackFile> { new() { Name = "a.patch_0", Size = first.Length, Sha256 = Hash(first) }, new() { Name = "b.patch_0", Size = second.Length, Sha256 = Hash(second) } };
        var selection = new HellForgePack.Selection("clonedivers", "full", "fixture", HellForgePack.SupportedBuild, new string('a', 64), [], files);
        string Output() => Path.Combine(exports, "clonedivers-" + Guid.NewGuid().ToString("N"));
        var good = Path.Combine(root, "good.bin"); await File.WriteAllBytesAsync(good, first);
        var bad = Path.Combine(root, "bad.bin"); await File.WriteAllBytesAsync(bad, Enumerable.Repeat((byte)'x', second.Length).ToArray());
        var interrupted = new HellForgePack.Plan(selection, Output(), new(StringComparer.OrdinalIgnoreCase) { [files[0].Sha256] = good, [files[1].Sha256] = bad });
        try { await HellForgePack.ExportAsync(interrupted, false, null, default, _ => null); check(false, "a tampered second file must stop the export"); }
        catch (InvalidDataException)
        {
            check(File.Exists(Path.Combine(interrupted.Output, "a.patch_0")) && !File.Exists(Path.Combine(interrupted.Output, "b.patch_0")) && !File.Exists(Path.Combine(interrupted.Output, HellForgePack.ReceiptName)),
                "an interrupted export keeps only its verified files and no completion receipt");
        }
        var empty = new HellForgePack.Plan(selection, Output(), new(StringComparer.OrdinalIgnoreCase) { [files[0].Sha256] = bad });
        try { await HellForgePack.ExportAsync(empty, true, null, default, _ => null); check(false, "a tampered first file must stop the export"); }
        catch (InvalidDataException) { check(!Directory.Exists(empty.Output), "an export stopped before verifying any file leaves no folder behind"); }
        Directory.SetLastWriteTimeUtc(interrupted.Output, DateTime.UtcNow.AddHours(-1));
        var finished = Path.Combine(exports, "clonedivers-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(finished);
        File.WriteAllText(Path.Combine(finished, HellForgePack.ReceiptName), "{}"); Directory.SetLastWriteTimeUtc(finished, DateTime.UtcNow.AddHours(-1));
        var foreign = Path.Combine(exports, "keep me"); Directory.CreateDirectory(foreign); Directory.SetLastWriteTimeUtc(foreign, DateTime.UtcNow.AddHours(-1));
        var sources = Path.Combine(root, "sources"); Directory.CreateDirectory(sources); await File.WriteAllBytesAsync(Path.Combine(sources, "second.bin"), second);
        var retry = await HellForgePack.PlanAsync(selection, Output(), [sources]);
        check(retry.Local.TryGetValue(files[0].Sha256, out var reused) && reused.StartsWith(interrupted.Output, StringComparison.OrdinalIgnoreCase) && retry.Missing.Count == 0,
            "the next plan reuses an interrupted export's verified files instead of downloading them again");
        await HellForgePack.ExportAsync(retry, false, null, default, _ => null);
        await HellForgePack.VerifyAsync(retry.Output, selection);
        check(!Directory.Exists(interrupted.Output) && Directory.Exists(finished) && Directory.Exists(foreign),
            "a completed export prunes earlier interrupted exports but keeps receipted and unrelated folders");
    }
    sealed class CancelAfterReadStream(CancellationTokenSource cancellation) : MemoryStream(new byte[16])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); var count = Read(buffer.Span); cancellation.Cancel(); return ValueTask.FromResult(count);
        }
    }
}
