using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Clonedivers;

/// <summary>Read-only, deliberately allowlisted support information. No log contents or personal paths.</summary>
public static class Diagnostics
{
    public sealed record Adapter(string Name, ulong DedicatedBytes, bool Software);
    public sealed record Hardware(string Cpu, int LogicalProcessors, ulong? PhysicalBytes,
        string Windows, IReadOnlyList<string> Displays, IReadOnlyList<Adapter> Adapters);

    public static string Build(Settings settings, string? gameDir, Manifest? manifest, string? lastError = null)
    {
        var b = new StringBuilder("Clonedivers support report\n");
        void Add(string key, string? value) => b.AppendLine($"{key}: {value ?? "unavailable"}");
        Add("Generated UTC", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Add("Launcher", typeof(Diagnostics).Assembly.GetName().Version?.ToString(3));
        Add("Installed pack (recorded)", VersionToken(settings.InstalledPackVersion));
        Add("Available pack", VersionToken(manifest?.Pack?.Version));
        Add("Installed game build (recorded)", VersionToken(settings.InstalledGameBuild));
        Add("Current Steam build", VersionToken(SteamAcf.Read(gameDir)?.BuildId));
        Add("Manifest source", string.IsNullOrWhiteSpace(settings.ManifestUrl) ? "official" : "custom (location omitted)");
        Add("Requested texture profile", ProfileToken(settings.TextureProfile));
        Add("Installed texture profile (recorded)", ProfileToken(settings.InstalledTextureProfile));
        Add("Legacy lighter-texture preference", settings.Options.TryGetValue("skinny", out var skinny) ? skinny ? "on" : "off" : "not recorded");
        Add("Last full verification (recorded)", settings.LastVerifiedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "never recorded");
        try
        {
            var state = ModFiles.GetState(gameDir);
            Add("Mod state", state.ToString());
            var mode = LauncherModes.Current(state, settings, manifest?.Pack);
            Add("Mode (recorded options / file presence)", mode is null ? null : LauncherModes.Name(mode.Value));
            Add("Game running", Game.IsRunning() ? "yes" : "no");
            Add("Pending pack operation", gameDir is null ? "unavailable" : Pack.HasPendingUpdate(gameDir, Settings.PlanPath) ? "yes" : "no");
            if (gameDir is not null && Directory.Exists(gameDir))
            {
                var receipt = InstallReceipt.Load(Settings.ReceiptPathFor(gameDir), gameDir);
                Add("Install receipt", receipt is null ? "missing or invalid" : "available for this game installation");
                if (receipt is not null)
                {
                    Add("Receipt layout", receipt.MatchesLayout(gameDir) ? "matches file names, sizes and timestamps (not fresh hashes)" : "does not match current layout");
                    Add("Receipt pack", VersionToken(receipt.PackVersion));
                    Add("Receipt texture profile", ProfileToken(receipt.TextureProfile));
                    Add("Receipt full verification UTC", receipt.VerifiedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "never recorded");
                }
                Add("Active patch files", ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.DataFolder)).Length.ToString(CultureInfo.InvariantCulture));
                Add("Parked patch files", ModFiles.ListPatchFiles(Path.Combine(gameDir, ModFiles.OffFolder)).Length.ToString(CultureInfo.InvariantCulture));
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(gameDir))!);
                Add("Game drive free space", drive.IsReady ? GiB((ulong)drive.AvailableFreeSpace) : null);
                Add("Game drive medium", "unavailable (not inferred from drive letter)");
            }
        }
        catch { Add("Installation inspection", "unavailable (access or I/O failure; details omitted)"); }
        Add("Integrity now", "not checked by this report; use Check and repair for a fresh verification");
        Add("Last error category", ErrorCategory(lastError));
        b.Append(FormatHistory(SupportHistory.Read()));
        b.Append(SessionLog.Format(SessionLog.Read()));
        b.AppendLine();
        b.Append(FormatHardware(ReadHardware()));
        b.AppendLine("Gameplay FPS / frame times / current VRAM usage: unavailable (requires gameplay capture)");
        b.AppendLine("Armor body type / equipped armor / audio language: unavailable (not read from game memory)");
        b.AppendLine("Performance sharing: " + (settings.Telemetry.Ready ? "enabled; separate session reports upload to the configured private collector" : "off"));
        b.AppendLine("This support report omits personal paths, account identifiers and upload credentials.");
        return b.ToString();
    }

    public static string ErrorCategory(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "none reported";
        // Never reproduce arbitrary exception messages: they can contain paths, URLs or account names.
        if (Regex.IsMatch(error, @"\b114\b", RegexOptions.CultureInvariant)) return "GameGuard 114 reported";
        if (Regex.IsMatch(error, @"\b110\b", RegexOptions.CultureInvariant)) return "GameGuard 110 reported";
        if (error.Contains("space", StringComparison.OrdinalIgnoreCase)) return "disk space";
        if (error.Contains("hash", StringComparison.OrdinalIgnoreCase) || error.Contains("checksum", StringComparison.OrdinalIgnoreCase)) return "file integrity";
        if (error.Contains("download", StringComparison.OrdinalIgnoreCase) || error.Contains("network", StringComparison.OrdinalIgnoreCase)) return "download or network";
        if (error.Contains("access", StringComparison.OrdinalIgnoreCase) || error.Contains("permission", StringComparison.OrdinalIgnoreCase)) return "file access";
        return "reported (message omitted for privacy)";
    }

    public static string FormatHistory(IReadOnlyList<SupportHistory.Entry> entries)
    {
        var b = new StringBuilder("Recent pack operations (up to 20):\n");
        var safe = entries.Where(e => e is not null && e.Event is "installed" or "verified" or "error").TakeLast(20).ToArray();
        if (safe.Length == 0) b.AppendLine("  No recorded operations.");
        foreach (var entry in safe)
        {
            var category = entry.ErrorCategory is "none reported" or "GameGuard 114 reported" or "GameGuard 110 reported" or "disk space" or "file integrity" or "download or network" or "file access"
                ? entry.ErrorCategory : "reported (message omitted for privacy)";
            b.AppendLine($"  {entry.Utc.ToUniversalTime():O} | {entry.Event} | pack {VersionToken(entry.Pack) ?? "unavailable"} | {ProfileToken(entry.Profile)} | {category}");
        }
        return b.ToString();
    }

    static string? VersionToken(string? value) => value is not null && Regex.IsMatch(value, @"\A[0-9][0-9A-Za-z._-]{0,63}\z", RegexOptions.CultureInvariant) ? value : null;
    static string ProfileToken(string? value) => value?.ToLowerInvariant() switch
    {
        "full" => "Full", "skinny" => "Lighter", "lighter" => "Lighter", "reduced" => "Reduced",
        "reduced1024" or "reduced-1024" => "Reduced 1024", "reduced512" or "reduced-512" => "Reduced 512", null or "" => "not recorded (legacy options may apply)", _ => "unrecognized profile"
    };
    public static string GiB(ulong bytes) => (bytes / 1073741824d).ToString("0.00", CultureInfo.InvariantCulture) + " GiB";
    static string OneLine(string text) => Regex.Replace(text, @"[\r\n\t\p{C}]", " ").Trim();

    public static string FormatHardware(Hardware hw)
    {
        var b = new StringBuilder();
        b.AppendLine($"Windows: {OneLine(hw.Windows)}");
        b.AppendLine($"CPU: {OneLine(hw.Cpu)} ({hw.LogicalProcessors} logical processors)");
        b.AppendLine($"Physical RAM: {(hw.PhysicalBytes is ulong bytes ? GiB(bytes) : "unavailable")}");
        b.AppendLine($"Display bounds: {(hw.Displays.Count == 0 ? "unavailable" : string.Join(", ", hw.Displays.Select(OneLine)))} (desktop bounds, not game render resolution)");
        if (hw.Adapters.Count == 0) b.AppendLine("GPU adapters / dedicated memory: unavailable");
        foreach (var gpu in hw.Adapters)
            b.AppendLine($"GPU: {OneLine(gpu.Name)}; dedicated memory: {GiB(gpu.DedicatedBytes)}{(gpu.Software ? "; software adapter" : "")} (capacity, not current usage)");
        b.AppendLine("GPU driver: unavailable");
        return b.ToString();
    }

    public static Hardware ReadHardware()
    {
        string cpu = "unavailable";
        try { using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"); cpu = key?.GetValue("ProcessorNameString") as string ?? cpu; } catch { }
        ulong? ram = null;
        try { var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() }; if (GlobalMemoryStatusEx(ref mem)) ram = mem.TotalPhysical; } catch { }
        var displays = new List<string>();
        try { displays.AddRange(Screen.AllScreens.Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? " primary" : "")}")); } catch { }
        var adapters = new List<Adapter>();
        IntPtr factory = IntPtr.Zero;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out factory) >= 0)
            {
                var enumerate = Method<EnumAdapters>(factory, 12);
                for (uint index = 0; index < 32; index++)
                {
                    IntPtr adapter = IntPtr.Zero;
                    try
                    {
                        if (enumerate(factory, index, out adapter) < 0) break;
                        if (Method<GetDesc>(adapter, 10)(adapter, out var desc) >= 0)
                            adapters.Add(new(desc.Description, desc.DedicatedVideoMemory.ToUInt64(), (desc.Flags & 2) != 0));
                    }
                    finally { if (adapter != IntPtr.Zero) Marshal.Release(adapter); }
                }
            }
        }
        catch { }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
        return new(cpu, Environment.ProcessorCount, ram, RuntimeInformation.OSDescription, displays, adapters);
    }

    static T Method<T>(IntPtr obj, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDesc(IntPtr self, out AdapterDesc desc);
    [DllImport("dxgi.dll", ExactSpelling = true)] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct AdapterDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
