using System.Text;
using System.Text.RegularExpressions;

namespace Clonedivers;

// procfs/sysfs lengths are not content lengths. Read these trusted, read-only OS
// metadata files from the beginning; normal OS symlinks are expected here.
internal static class LinuxSystemInfo
{
    internal static string Head(Stream source, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var bytes = new byte[limit];
        var read = source.ReadAtLeast(bytes, limit, throwOnEndOfStream: false);
        return Encoding.UTF8.GetString(bytes, 0, read).Trim();
    }

    internal static string Head(string path, int limit)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Head(file, limit);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "unknown"; }
    }

    internal static string Cpu(string path = "/proc/cpuinfo") => Head(path, 32768).Split('\n')
        .FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal) || l.StartsWith("Hardware", StringComparison.Ordinal))?
        .Split(':', 2).Last().Trim() is { Length: > 0 } model ? model : "unknown";

    internal static string PciId(string path)
    {
        var value = Head(path, 128);
        return Regex.IsMatch(value, "^0x[0-9a-fA-F]{4}$") ? value : "unknown";
    }

    internal static object[] Gpus(string root = "/sys/class/drm")
    {
        try
        {
            return Directory.EnumerateDirectories(root, "card*").Where(d => Regex.IsMatch(Path.GetFileName(d), "^card[0-9]+$"))
                .Take(8).Select(d => (object)new
                {
                    Device = Path.GetFileName(d), Vendor = PciId(Path.Combine(d, "device/vendor")),
                    PciDevice = PciId(Path.Combine(d, "device/device")), Driver = Driver(d)
                }).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    static string Driver(string device)
    {
        try { return new DirectoryInfo(Path.Combine(device, "device/driver")).ResolveLinkTarget(true)?.Name ?? "unknown"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "unknown"; }
    }
}
