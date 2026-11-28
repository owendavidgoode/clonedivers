using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Clonedivers;

public sealed record SystemMemorySnapshot(
    ulong TotalRamBytes,
    ulong AvailableRamBytes,
    ulong SwapTotalBytes,
    ulong SwapUsedBytes,
    ulong CommittedBytes,
    ulong CommitLimitBytes,
    ulong? LinuxUnattributedBytes = null);

public sealed record ProcessMemorySample(
    int Pid,
    string Name,
    long WorkingSetBytes,
    long PeakWorkingSetBytes,
    long PrivateBytes,
    long PagefileOrSwapBytes,
    long? LinuxRssAnonBytes = null,
    long? LinuxRssFileBytes = null,
    long? LinuxRssShmemBytes = null,
    ulong? DrmResidentVramBytes = null,
    ulong? DrmResidentGttBytes = null,
    ulong? AmdEvictedVramBytes = null,
    int Threads = 0);

public sealed record SessionMemoryObservation(
    DateTimeOffset CapturedUtc,
    SystemMemorySnapshot System,
    IReadOnlyList<ProcessMemorySample> Processes);

/// <summary>
/// Lightweight, read-only system, process, and GPU/DRM memory sampler for session observation and support diagnostics (#43, #45, #46).
/// Never opens game memory for reading and never mutates OS or process state.
/// </summary>
public static class ProcessMemorySampler
{
    static readonly string[] SessionProcessPrefixes =
    [
        "helldivers2",
        "HellForge.Session",
        "HellForge.LocalBackend",
        "HellForge.Local"
    ];

    public static SystemMemorySnapshot CaptureSystem(string linuxMemInfoPath = "/proc/meminfo")
    {
        if (OperatingSystem.IsLinux())
            return ParseLinuxMemInfo(ReadHeadSafe(linuxMemInfoPath, 16384));

        if (OperatingSystem.IsWindows())
            return CaptureWindowsSystemMemory();

        return new(0, 0, 0, 0, 0, 0);
    }

    public static SystemMemorySnapshot ParseLinuxMemInfo(string? memInfoText)
    {
        if (string.IsNullOrWhiteSpace(memInfoText))
            return new(0, 0, 0, 0, 0, 0);

        ulong FieldBytes(string key)
        {
            var match = Regex.Match(memInfoText, $@"^{Regex.Escape(key)}:\s*(\d+)\s*kB", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return match.Success && ulong.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb)
                ? kb * 1024UL
                : 0UL;
        }

        bool hasMemFree = Regex.IsMatch(memInfoText, @"^MemFree:\s*\d+\s*kB", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        ulong memTotal = FieldBytes("MemTotal");
        ulong memFree = FieldBytes("MemFree");
        ulong memAvail = FieldBytes("MemAvailable");
        if (memAvail == 0) memAvail = memFree;
        ulong swapTotal = FieldBytes("SwapTotal");
        ulong swapFree = FieldBytes("SwapFree");
        ulong swapUsed = swapTotal > swapFree ? swapTotal - swapFree : 0UL;
        ulong committed = FieldBytes("Committed_AS");
        ulong commitLimit = FieldBytes("CommitLimit");
        ulong anonPages = FieldBytes("AnonPages");
        ulong cached = FieldBytes("Cached");
        ulong buffers = FieldBytes("Buffers");
        ulong sreclaimable = FieldBytes("SReclaimable");

        // Estimate physical RAM not accounted for by AnonPages + page cache (Cached already includes Shmem) + Buffers + SReclaimable.
        // Uses MemTotal - MemFree rather than MemTotal - MemAvailable so reclaimable page cache is never subtracted twice.
        ulong? unattributed = null;
        if (memTotal > 0 && hasMemFree && memTotal >= memFree)
        {
            ulong physUsed = memTotal - memFree;
            ulong accounted = anonPages + cached + buffers + sreclaimable;
            unattributed = physUsed > accounted ? physUsed - accounted : 0UL;
        }

        return new(memTotal, memAvail, swapTotal, swapUsed, committed, commitLimit, unattributed);
    }

    public static ProcessMemorySample? ParseLinuxProcProcess(int pid, string procRoot = "/proc")
    {
        if (pid <= 0 || string.IsNullOrWhiteSpace(procRoot)) return null;
        var procDir = Path.Combine(procRoot, pid.ToString(CultureInfo.InvariantCulture));
        var statusText = ReadHeadSafe(Path.Combine(procDir, "status"), 16384);
        if (string.IsNullOrWhiteSpace(statusText)) return null;

        string name = "unknown";
        var nameMatch = Regex.Match(statusText, @"^Name:\s*(.+)$", RegexOptions.Multiline);
        if (nameMatch.Success) name = nameMatch.Groups[1].Value.Trim();

        var cmdline = ReadCmdlineSafe(Path.Combine(procDir, "cmdline"), 4096);
        if (cmdline.Count > 0)
        {
            foreach (var token in cmdline)
            {
                var leaf = token[(token.LastIndexOfAny(['/', '\\']) + 1)..];
                if (leaf.Equals("helldivers2.exe", StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals("HellForge.Session.exe", StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals("HellForge.LocalBackend", StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals("HellForge.LocalBackend.exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = leaf;
                    break;
                }
            }
        }

        long FieldBytes(string key)
        {
            var match = Regex.Match(statusText, $@"^{Regex.Escape(key)}:\s*(\d+)\s*kB", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb)
                ? kb * 1024L
                : 0L;
        }

        long vmRss = FieldBytes("VmRSS");
        long vmHwm = FieldBytes("VmHWM");
        long rssAnon = FieldBytes("RssAnon");
        long rssFile = FieldBytes("RssFile");
        long rssShmem = FieldBytes("RssShmem");
        long vmSwap = FieldBytes("VmSwap");
        long vmData = FieldBytes("VmData");
        int threads = 0;
        var threadsMatch = Regex.Match(statusText, @"^Threads:\s*(\d+)", RegexOptions.Multiline);
        if (threadsMatch.Success) _ = int.TryParse(threadsMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out threads);

        var (drmVram, drmGtt, evictedVram) = ReadLinuxDrmFdInfo(Path.Combine(procDir, "fdinfo"));

        return new ProcessMemorySample(
            Pid: pid,
            Name: name,
            WorkingSetBytes: vmRss,
            PeakWorkingSetBytes: vmHwm > 0 ? vmHwm : vmRss,
            PrivateBytes: rssAnon > 0 ? rssAnon + vmSwap : vmData,
            PagefileOrSwapBytes: vmSwap,
            LinuxRssAnonBytes: rssAnon,
            LinuxRssFileBytes: rssFile,
            LinuxRssShmemBytes: rssShmem,
            DrmResidentVramBytes: drmVram,
            DrmResidentGttBytes: drmGtt,
            AmdEvictedVramBytes: evictedVram,
            Threads: threads);
    }

    public static (ulong? ResidentVramBytes, ulong? ResidentGttBytes, ulong? AmdEvictedVramBytes) ReadLinuxDrmFdInfo(
        string fdInfoDirectory,
        Func<FileSystemInfo, string?>? resolveFdTarget = null)
    {
        if (!Directory.Exists(fdInfoDirectory))
            return (null, null, null);

        var clients = new Dictionary<string, (ulong Vram, ulong Gtt, ulong Evicted)>(StringComparer.Ordinal);
        try
        {
            IEnumerable<string> candidateFdInfos;
            var parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fdInfoDirectory));
            var fdDir = !string.IsNullOrEmpty(parentDir) ? Path.Combine(parentDir, "fd") : "";
            if (!string.IsNullOrEmpty(fdDir) && Directory.Exists(fdDir))
            {
                var targetResolver = resolveFdTarget ?? (static entry => entry.LinkTarget);
                var driFiles = new List<string>();
                foreach (var fdEntry in new DirectoryInfo(fdDir).EnumerateFileSystemInfos())
                {
                    string? target = null;
                    try { target = targetResolver(fdEntry); } catch { }
                    if (!string.IsNullOrEmpty(target) && target.Contains("/dev/dri/", StringComparison.Ordinal))
                        driFiles.Add(Path.Combine(fdInfoDirectory, fdEntry.Name));
                }
                candidateFdInfos = driFiles;
            }
            else
            {
                candidateFdInfos = Directory.EnumerateFiles(fdInfoDirectory);
            }

            foreach (var fdFile in candidateFdInfos)
            {
                var text = ReadHeadSafe(fdFile, 8192);
                if (string.IsNullOrEmpty(text) || !text.Contains("drm-client-id:", StringComparison.Ordinal))
                    continue;

                var clientMatch = Regex.Match(text, @"^drm-client-id:\s*(\S+)", RegexOptions.Multiline);
                if (!clientMatch.Success) continue;
                var pdevMatch = Regex.Match(text, @"^drm-pdev:\s*(\S+)", RegexOptions.Multiline);
                var clientKey = (pdevMatch.Success ? pdevMatch.Groups[1].Value : "gpu") + "#" + clientMatch.Groups[1].Value;

                ulong vram = ParseDrmSizeBytes(text, "drm-resident-vram") ?? ParseDrmSizeBytes(text, "drm-memory-vram") ?? 0UL;
                ulong gtt = ParseDrmSizeBytes(text, "drm-resident-gtt") ?? ParseDrmSizeBytes(text, "drm-memory-gtt") ?? 0UL;
                ulong evicted = ParseDrmSizeBytes(text, "amd-evicted-vram") ?? 0UL;

                if (clients.TryGetValue(clientKey, out var existing))
                {
                    clients[clientKey] = (
                        Math.Max(existing.Vram, vram),
                        Math.Max(existing.Gtt, gtt),
                        Math.Max(existing.Evicted, evicted));
                }
                else
                {
                    clients[clientKey] = (vram, gtt, evicted);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        if (clients.Count == 0)
            return (null, null, null);

        ulong totalVram = 0, totalGtt = 0, totalEvicted = 0;
        foreach (var entry in clients.Values)
        {
            totalVram += entry.Vram;
            totalGtt += entry.Gtt;
            totalEvicted += entry.Evicted;
        }
        return (totalVram, totalGtt, totalEvicted > 0 ? totalEvicted : null);
    }

    static ulong? ParseDrmSizeBytes(string text, string key)
    {
        var match = Regex.Match(text, $@"^{Regex.Escape(key)}:\s*(\d+)(?:\s*(KiB|kB|MiB|MB|GiB|GB|B))?", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!match.Success || !ulong.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            return null;
        var unit = match.Groups[2].Success ? match.Groups[2].Value : "B";
        return unit.ToUpperInvariant() switch
        {
            "KIB" or "KB" => raw * 1024UL,
            "MIB" or "MB" => raw * 1024UL * 1024UL,
            "GIB" or "GB" => raw * 1024UL * 1024UL * 1024UL,
            _ => raw
        };
    }

    public static ProcessMemorySample? SampleProcess(Process process, string linuxProcRoot = "/proc")
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var procSample = ParseLinuxProcProcess(process.Id, linuxProcRoot);
                if (procSample != null) return procSample;
            }

            process.Refresh();
            if (process.HasExited) return null;

            long ws = process.WorkingSet64;
            long peakWs = process.PeakWorkingSet64;
            long priv = process.PrivateMemorySize64;
            long paged = process.PagedMemorySize64;
            int threads = 0;
            try { threads = process.Threads.Count; } catch { }

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var counters = new ProcessMemoryCountersEx { cb = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>() };
                    if (K32GetProcessMemoryInfo(process.Handle, out counters, counters.cb))
                    {
                        ws = (long)counters.WorkingSetSize.ToUInt64();
                        peakWs = (long)counters.PeakWorkingSetSize.ToUInt64();
                        priv = (long)counters.PrivateUsage.ToUInt64();
                        paged = (long)counters.PagefileUsage.ToUInt64();
                    }
                }
                catch { }
            }

            return new ProcessMemorySample(
                Pid: process.Id,
                Name: process.ProcessName,
                WorkingSetBytes: ws,
                PeakWorkingSetBytes: peakWs,
                PrivateBytes: priv,
                PagefileOrSwapBytes: paged,
                Threads: threads);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or IOException)
        {
            return null;
        }
    }

    public static SessionMemoryObservation CaptureObservation(string linuxProcRoot = "/proc", bool includeSessionProcesses = true)
    {
        var memInfoPath = Path.Combine(linuxProcRoot, "meminfo");
        var sys = OperatingSystem.IsLinux() || File.Exists(memInfoPath)
            ? ParseLinuxMemInfo(ReadHeadSafe(memInfoPath, 16384))
            : CaptureSystem();

        var samples = new List<ProcessMemorySample>();
        try
        {
            using var self = Process.GetCurrentProcess();
            if (SampleProcess(self, linuxProcRoot) is { } selfSample)
                samples.Add(selfSample);
        }
        catch { }

        if (includeSessionProcesses)
        {
            if (OperatingSystem.IsLinux() || Directory.Exists(linuxProcRoot))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(linuxProcRoot))
                    {
                        if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                            continue;
                        if (samples.Any(s => s.Pid == pid)) continue;
                        var candidate = ParseLinuxProcProcess(pid, linuxProcRoot);
                        if (candidate != null && IsRelevantSessionProcessName(candidate.Name))
                            samples.Add(candidate);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            else
            {
                foreach (var name in new[] { "helldivers2", "HellForge.Session", "HellForge.LocalBackend" })
                {
                    Process[] found = [];
                    try { found = Process.GetProcessesByName(name); }
                    catch { }
                    foreach (var proc in found)
                    {
                        using (proc)
                        {
                            if (samples.Any(s => s.Pid == proc.Id)) continue;
                            if (SampleProcess(proc, linuxProcRoot) is { } s)
                                samples.Add(s);
                        }
                    }
                }
            }
        }

        return new SessionMemoryObservation(DateTimeOffset.UtcNow, sys, samples);
    }

    static bool IsRelevantSessionProcessName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        SessionProcessPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public static string FormatGiB(ulong bytes) =>
        (bytes / 1073741824d).ToString("0.00", CultureInfo.InvariantCulture) + " GiB";

    public static string FormatGiB(long bytes) =>
        (Math.Max(0L, bytes) / 1073741824d).ToString("0.00", CultureInfo.InvariantCulture) + " GiB";

    static SystemMemorySnapshot CaptureWindowsSystemMemory()
    {
        ulong totalRam = 0, availRam = 0, swapTotal = 0, swapUsed = 0, committed = 0, commitLimit = 0;
        try
        {
            var mem = new WinMemoryStatusEx { Length = (uint)Marshal.SizeOf<WinMemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                totalRam = mem.TotalPhysical;
                availRam = mem.AvailablePhysical;
                committed = mem.TotalPageFile > mem.AvailablePageFile ? mem.TotalPageFile - mem.AvailablePageFile : 0UL;
                commitLimit = mem.TotalPageFile;
            }
            ulong page = 4096UL;
            var perf = new WinPerformanceInfo { Size = (uint)Marshal.SizeOf<WinPerformanceInfo>() };
            if (K32GetPerformanceInfo(ref perf, perf.Size))
            {
                page = perf.PageSize.ToUInt64();
                committed = perf.CommitTotal.ToUInt64() * page;
                commitLimit = perf.CommitLimit.ToUInt64() * page;
            }
            // Query actual Windows paging file occupancy via K32EnumPageFilesW rather than mixing virtual commit charge with physical RAM.
            ulong pfTotalPages = 0, pfUsedPages = 0;
            bool CollectPageFile(IntPtr ctx, ref EnumPageFileInformation info, IntPtr filename)
            {
                pfTotalPages += info.TotalSize.ToUInt64();
                pfUsedPages += info.TotalInUse.ToUInt64();
                return true;
            }
            if (K32EnumPageFilesW(CollectPageFile, IntPtr.Zero))
            {
                swapTotal = pfTotalPages * page;
                swapUsed = pfUsedPages * page;
            }
        }
        catch { }
        return new(totalRam, availRam, swapTotal, swapUsed, committed, commitLimit);
    }

    static string ReadHeadSafe(string path, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return "";
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[maxBytes];
            int read = fs.Read(buf, 0, buf.Length);
            return read <= 0 ? "" : Encoding.UTF8.GetString(buf, 0, read);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }

    static IReadOnlyList<string> ReadCmdlineSafe(string path, int maxBytes)
    {
        var raw = ReadHeadSafe(path, maxBytes);
        return string.IsNullOrEmpty(raw)
            ? []
            : raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinMemoryStatusEx
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinPerformanceInfo
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint Handles, Processes, Threads;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessMemoryCountersEx
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EnumPageFileInformation
    {
        public uint cb;
        public uint Reserved;
        public UIntPtr TotalSize;
        public UIntPtr TotalInUse;
        public UIntPtr PeakUsage;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate bool EnumPageFileCallback(IntPtr context, ref EnumPageFileInformation pageFileInfo, IntPtr filename);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref WinMemoryStatusEx status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool K32GetPerformanceInfo(ref WinPerformanceInfo info, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool K32EnumPageFilesW(EnumPageFileCallback callback, IntPtr context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool K32GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCountersEx ppsmemCounters, uint cb);
}

/// <summary>
/// Bounded per-session memory high-water tracker sampled periodically during active gameplay sessions.
/// </summary>
public sealed class SessionMemoryTracker
{
    readonly object gate = new();
    DateTimeOffset lastSampleUtc = DateTimeOffset.MinValue;

    public int SampleCount { get; private set; }
    public SessionMemoryObservation? StartObservation { get; private set; }
    public SessionMemoryObservation? LatestObservation { get; private set; }
    public long PeakGameWorkingSetBytes { get; private set; }
    public long PeakGamePrivateBytes { get; private set; }
    public long PeakGameSwapOrPagefileBytes { get; private set; }
    public ulong PeakGameDrmVramBytes { get; private set; }
    public ulong PeakGameDrmGttBytes { get; private set; }
    public ulong PeakGameEvictedVramBytes { get; private set; }
    public ulong MinSystemAvailableRamBytes { get; private set; }
    public ulong PeakSystemSwapUsedBytes { get; private set; }
    public ulong PeakSystemCommittedBytes { get; private set; }
    public ulong PeakLinuxUnattributedBytes { get; private set; }

    public void Reset()
    {
        lock (gate)
        {
            lastSampleUtc = DateTimeOffset.MinValue;
            SampleCount = 0;
            StartObservation = null;
            LatestObservation = null;
            PeakGameWorkingSetBytes = 0;
            PeakGamePrivateBytes = 0;
            PeakGameSwapOrPagefileBytes = 0;
            PeakGameDrmVramBytes = 0;
            PeakGameDrmGttBytes = 0;
            PeakGameEvictedVramBytes = 0;
            MinSystemAvailableRamBytes = 0;
            PeakSystemSwapUsedBytes = 0;
            PeakSystemCommittedBytes = 0;
            PeakLinuxUnattributedBytes = 0;
        }
    }

    public bool TrySample(bool force = false, string linuxProcRoot = "/proc")
    {
        var now = DateTimeOffset.UtcNow;
        lock (gate)
        {
            if (!force && SampleCount > 0 && now - lastSampleUtc < TimeSpan.FromSeconds(15))
                return false;
            lastSampleUtc = now;
        }

        var obs = ProcessMemorySampler.CaptureObservation(linuxProcRoot, includeSessionProcesses: true);
        Ingest(obs);
        return true;
    }

    public void Ingest(SessionMemoryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (gate)
        {
            if (SampleCount == 0)
            {
                StartObservation = observation;
                MinSystemAvailableRamBytes = observation.System.AvailableRamBytes;
            }
            else if (observation.System.AvailableRamBytes > 0 &&
                     (MinSystemAvailableRamBytes == 0 || observation.System.AvailableRamBytes < MinSystemAvailableRamBytes))
            {
                MinSystemAvailableRamBytes = observation.System.AvailableRamBytes;
            }

            LatestObservation = observation;
            SampleCount++;

            if (observation.System.SwapUsedBytes > PeakSystemSwapUsedBytes)
                PeakSystemSwapUsedBytes = observation.System.SwapUsedBytes;
            if (observation.System.CommittedBytes > PeakSystemCommittedBytes)
                PeakSystemCommittedBytes = observation.System.CommittedBytes;
            if (observation.System.LinuxUnattributedBytes is ulong unattr && unattr > PeakLinuxUnattributedBytes)
                PeakLinuxUnattributedBytes = unattr;

            foreach (var proc in observation.Processes)
            {
                if (!proc.Name.StartsWith("helldivers2", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (proc.WorkingSetBytes > PeakGameWorkingSetBytes)
                    PeakGameWorkingSetBytes = proc.WorkingSetBytes;
                if (proc.PeakWorkingSetBytes > PeakGameWorkingSetBytes)
                    PeakGameWorkingSetBytes = proc.PeakWorkingSetBytes;
                if (proc.PrivateBytes > PeakGamePrivateBytes)
                    PeakGamePrivateBytes = proc.PrivateBytes;
                if (proc.PagefileOrSwapBytes > PeakGameSwapOrPagefileBytes)
                    PeakGameSwapOrPagefileBytes = proc.PagefileOrSwapBytes;
                if (proc.DrmResidentVramBytes is ulong vram && vram > PeakGameDrmVramBytes)
                    PeakGameDrmVramBytes = vram;
                if (proc.DrmResidentGttBytes is ulong gtt && gtt > PeakGameDrmGttBytes)
                    PeakGameDrmGttBytes = gtt;
                if (proc.AmdEvictedVramBytes is ulong ev && ev > PeakGameEvictedVramBytes)
                    PeakGameEvictedVramBytes = ev;
            }
        }
    }

    public string FormatSummaryLine()
    {
        lock (gate)
        {
            if (SampleCount == 0)
            {
                var snap = ProcessMemorySampler.CaptureObservation(includeSessionProcesses: false);
                var launcher = snap.Processes.FirstOrDefault();
                var launcherWs = launcher != null ? ProcessMemorySampler.FormatGiB(launcher.WorkingSetBytes) : "n/a";
                var unattrPart = snap.System.LinuxUnattributedBytes is ulong u && u >= (4UL << 30)
                    ? $" · UnattributedEst={ProcessMemorySampler.FormatGiB(u)}"
                    : "";
                return $"Idle (Sys Avail={ProcessMemorySampler.FormatGiB(snap.System.AvailableRamBytes)}/{ProcessMemorySampler.FormatGiB(snap.System.TotalRamBytes)} · SwapUsed={ProcessMemorySampler.FormatGiB(snap.System.SwapUsedBytes)} · Launcher WS={launcherWs}{unattrPart})";
            }

            var sb = new StringBuilder();
            sb.Append($"Samples={SampleCount} · Peak HD2 WS={ProcessMemorySampler.FormatGiB(PeakGameWorkingSetBytes)} Priv={ProcessMemorySampler.FormatGiB(PeakGamePrivateBytes)} Swap/Page={ProcessMemorySampler.FormatGiB(PeakGameSwapOrPagefileBytes)}");
            if (PeakGameDrmVramBytes > 0 || PeakGameDrmGttBytes > 0)
            {
                sb.Append($" DRM-VRAM={ProcessMemorySampler.FormatGiB(PeakGameDrmVramBytes)} DRM-GTT={ProcessMemorySampler.FormatGiB(PeakGameDrmGttBytes)}");
                if (PeakGameEvictedVramBytes > 0)
                    sb.Append($" Evicted={ProcessMemorySampler.FormatGiB(PeakGameEvictedVramBytes)}");
            }
            sb.Append($" · Sys MinAvail={ProcessMemorySampler.FormatGiB(MinSystemAvailableRamBytes)} PeakSwap={ProcessMemorySampler.FormatGiB(PeakSystemSwapUsedBytes)}");
            if (PeakSystemCommittedBytes > 0)
                sb.Append($" PeakCommit={ProcessMemorySampler.FormatGiB(PeakSystemCommittedBytes)}");
            if (PeakLinuxUnattributedBytes >= (4UL << 30))
                sb.Append($" PeakUnattributedEst={ProcessMemorySampler.FormatGiB(PeakLinuxUnattributedBytes)}");
            return sb.ToString();
        }
    }

    public object ToDiagnosticObject()
    {
        lock (gate)
        {
            return new
            {
                SampleCount,
                PeakGameWorkingSetBytes,
                PeakGamePrivateBytes,
                PeakGameSwapOrPagefileBytes,
                PeakGameDrmVramBytes,
                PeakGameDrmGttBytes,
                PeakGameEvictedVramBytes,
                MinSystemAvailableRamBytes,
                PeakSystemSwapUsedBytes,
                PeakSystemCommittedBytes,
                PeakLinuxUnattributedBytes,
                Start = StartObservation,
                Latest = LatestObservation
            };
        }
    }
}
