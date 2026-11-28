using System.Globalization;
using System.Text.RegularExpressions;

namespace Clonedivers;

public sealed record PerformanceTierDefinition(
    string Id,
    string DisplayName,
    string ShortLabel,
    string Description,
    string MappedPackTexture,
    double RecommendedMinVramGb,
    double RecommendedMinRamGb,
    string PlannedFutureProfile = "");

public sealed record HardwarePerformanceRecommendation(
    string GpuName,
    double VramGb,
    double RamGb,
    string RecommendedTierId,
    string Reason,
    double SwapGb = 0d,
    double AvailableRamGb = 0d);

/// <summary>
/// 5-tier Star Wars pack performance ladder (Ultra -> High -> Balanced -> Low VRAM 512px -> Ultra Potato),
/// hardware auto-detection across Windows DXGI and Linux /proc + /sys, and automatic step-down advisor.
/// </summary>
public static class PerformanceTierAdvisor
{
    public const string Auto = "auto";
    public const string Ultra = "ultra";
    public const string High = "high";
    public const string Balanced = "balanced";
    public const string Low512 = "low-512";
    public const string UltraPotato = "ultra-potato";

    public const double UltraMinDedicatedVramExclusiveGb = 18.0;
    public const double UltraMinSystemRamGb = 40.0;
    public const double UltraMinLinuxTotalCommitGb = 48.0;

    public static IReadOnlyList<PerformanceTierDefinition> Tiers { get; } =
    [
        new(
            Id: Ultra,
            DisplayName: "Ultra",
            ShortLabel: "Ultra",
            Description: "Full resident Star Wars textures (~27 GiB GPU/GTT footprint; requires > 18 GB VRAM and 40+ GB RAM).",
            MappedPackTexture: "full",
            RecommendedMinVramGb: 20.0,
            RecommendedMinRamGb: 40.0,
            PlannedFutureProfile: "full"),
        new(
            Id: High,
            DisplayName: "High",
            ShortLabel: "High",
            Description: "Balanced streamed Star Wars textures (~8–10 GiB VRAM) for 8–18 GB GPUs.",
            MappedPackTexture: "lighter",
            RecommendedMinVramGb: 8.0,
            RecommendedMinRamGb: 16.0,
            PlannedFutureProfile: "lighter"),
        new(
            Id: Balanced,
            DisplayName: "Balanced",
            ShortLabel: "Balanced",
            Description: "1024px streamed Star Wars textures for 6–8 GB GPUs and gaming laptops.",
            MappedPackTexture: "reduced1024",
            RecommendedMinVramGb: 6.0,
            RecommendedMinRamGb: 16.0,
            PlannedFutureProfile: "reduced1024"),
        new(
            Id: Low512,
            DisplayName: "Low VRAM (512px)",
            ShortLabel: "Low VRAM",
            Description: "Compact 512px textures cutting resident GPU memory by ~70% for 4–6 GB GPUs and handhelds.",
            MappedPackTexture: "reduced512",
            RecommendedMinVramGb: 4.0,
            RecommendedMinRamGb: 8.0,
            PlannedFutureProfile: "reduced512"),
        new(
            Id: UltraPotato,
            DisplayName: "Ultra Potato",
            ShortLabel: "Ultra Potato",
            Description: "Lowest-memory 256px textures + decimated meshes for <= 4 GB or integrated GPUs.",
            MappedPackTexture: "potato256",
            RecommendedMinVramGb: 0.0,
            RecommendedMinRamGb: 8.0,
            PlannedFutureProfile: "potato256")
    ];

    public static bool IsValidPreferenceTier(string? tierId) =>
        tierId is Auto or Ultra or High or Balanced or Low512 or UltraPotato;

    public static bool IsConcreteTier(string? tierId) =>
        tierId is Ultra or High or Balanced or Low512 or UltraPotato;

    public static string TierFromPackTexture(string? texture) => texture?.Trim().ToLowerInvariant() switch
    {
        "full" => Ultra,
        "lighter" or "skinny" => High,
        "reduced1024" or "reduced-1024" => Balanced,
        "reduced512" or "reduced-512" => Low512,
        "potato256" or "reduced256" or "reduced-256" => UltraPotato,
        _ => Balanced
    };

    /// <summary>
    /// Reconciles a saved manual PerformanceTier with its active pack Texture on load (e.g. upgrading v2.0.3
    /// {"AutoPerformanceTier":false,"PerformanceTier":"balanced","Texture":"lighter"} -> "high" and
    /// {"AutoPerformanceTier":false,"PerformanceTier":"ultra-potato","Texture":"reduced512"} -> "low-512")
    /// while preserving invalid tier strings so Validate() still rejects corrupt preferences.
    /// </summary>
    public static string ReconcileManualTier(string? savedTier, string? texture)
    {
        var canonical = TierFromPackTexture(texture);
        if (string.IsNullOrEmpty(savedTier)) return canonical;
        return IsValidPreferenceTier(savedTier) ? canonical : savedTier;
    }

    public static string NormalizeConcreteTier(string? tierOrTexture, HardwarePerformanceRecommendation? hardware = null)
    {
        if (string.IsNullOrWhiteSpace(tierOrTexture))
            return (hardware ?? ScanHardware()).RecommendedTierId;
        var normalized = tierOrTexture.Trim().ToLowerInvariant();
        return normalized switch
        {
            Auto => (hardware ?? ScanHardware()).RecommendedTierId,
            Ultra or "full" => Ultra,
            High or "lighter" or "skinny" => High,
            Balanced or "reduced1024" or "reduced-1024" => Balanced,
            Low512 or "reduced512" or "reduced-512" => Low512,
            UltraPotato or "potato256" or "reduced256" or "reduced-256" => UltraPotato,
            _ => Balanced
        };
    }

    public static PerformanceTierDefinition GetTier(string? tierOrTexture, HardwarePerformanceRecommendation? hardware = null)
    {
        var id = NormalizeConcreteTier(tierOrTexture, hardware);
        return Tiers.First(t => t.Id == id);
    }

    public static string ResolvePackTexture(string? tierId, HardwarePerformanceRecommendation? hardware = null) =>
        GetTier(tierId, hardware).MappedPackTexture;

    /// <summary>Returns the next lower tier on the 5-step ladder, or null when already at Ultra Potato.</summary>
    public static string? NextLowerTier(string? current, HardwarePerformanceRecommendation? hardware = null)
    {
        var concrete = NormalizeConcreteTier(current, hardware);
        return concrete switch
        {
            Ultra => High,
            High => Balanced,
            Balanced => Low512,
            Low512 => UltraPotato,
            UltraPotato => null,
            _ => null
        };
    }

    public static Func<HardwarePerformanceRecommendation>? HardwareOverride { get; set; }

    public static HardwarePerformanceRecommendation ScanHardware()
    {
        if (HardwareOverride != null) return HardwareOverride();
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var hw = Diagnostics.ReadHardware();
                var ramGb = hw.PhysicalBytes is ulong bytes ? Math.Round(bytes / 1073741824d, 2) : 0d;
                var availGb = hw.AvailablePhysicalBytes is ulong avail ? Math.Round(avail / 1073741824d, 2) : 0d;
                var swapGb = hw.TotalPageFileBytes is ulong pf && hw.PhysicalBytes is ulong phys && pf > phys
                    ? Math.Round((pf - phys) / 1073741824d, 2)
                    : 0d;
                var adapter = hw.Adapters
                    .Where(a => !a.Software)
                    .OrderByDescending(a => a.DedicatedBytes)
                    .FirstOrDefault()
                    ?? hw.Adapters.OrderByDescending(a => a.DedicatedBytes).FirstOrDefault();

                if (adapter != null)
                {
                    var vramGb = Math.Round(adapter.DedicatedBytes / 1073741824d, 2);
                    var gpuName = CleanGpuName(adapter.Name);
                    return Recommend(gpuName, vramGb, ramGb, adapter.Software || LooksIntegrated(gpuName, vramGb), swapGb, isLinux: false, availableRamGb: availGb);
                }
                return Recommend("Unknown GPU", 0d, ramGb, false, swapGb, isLinux: false, availableRamGb: availGb);
            }
            catch
            {
                return Recommend("Unknown GPU", 0d, 0d, false);
            }
        }
#endif
        if (OperatingSystem.IsLinux())
        {
            return ScanLinuxHardware();
        }

        return Recommend("Unknown GPU", 0d, 0d, false);
    }

    public static HardwarePerformanceRecommendation ScanLinuxHardware(
        string memInfoPath = "/proc/meminfo",
        string drmRoot = "/sys/class/drm",
        string nvidiaGpusRoot = "/proc/driver/nvidia/gpus")
    {
        var memInfoText = SafeReadHead(memInfoPath, 16384);
        double ramGb = ParseLinuxMemInfoRamGb(memInfoText);
        double swapGb = ParseLinuxMemInfoFieldGb(memInfoText, "SwapTotal");
        double availGb = ParseLinuxMemInfoFieldGb(memInfoText, "MemAvailable");
        string gpuName = "Unknown GPU";
        double bestVramGb = 0d;
        bool sawDiscreteVendor = false;

        try
        {
            if (Directory.Exists(nvidiaGpusRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(nvidiaGpusRoot).Take(4))
                {
                    var info = SafeReadHead(Path.Combine(dir, "information"), 4096);
                    var modelMatch = Regex.Match(info, @"^Model:\s*(.+)$", RegexOptions.Multiline);
                    if (modelMatch.Success)
                    {
                        gpuName = CleanGpuName(modelMatch.Groups[1].Value);
                        sawDiscreteVendor = true;
                        break;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        try
        {
            if (Directory.Exists(drmRoot))
            {
                foreach (var card in Directory.EnumerateDirectories(drmRoot, "card*")
                    .Where(d => Regex.IsMatch(Path.GetFileName(d), "^card[0-9]+$"))
                    .Take(8))
                {
                    var vramText = SafeReadHead(Path.Combine(card, "device", "mem_info_vram_total"), 128).Trim();
                    if (ulong.TryParse(vramText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vramBytes) && vramBytes > 0)
                    {
                        var gb = Math.Round(vramBytes / 1073741824d, 2);
                        if (gb >= bestVramGb) bestVramGb = gb;
                    }

                    if (gpuName == "Unknown GPU")
                    {
                        var vendor = SafeReadHead(Path.Combine(card, "device", "vendor"), 64).Trim().ToLowerInvariant();
                        gpuName = vendor switch
                        {
                            "0x10de" => "NVIDIA GPU",
                            "0x1002" => "AMD Radeon GPU",
                            "0x8086" => "Intel Graphics",
                            _ => gpuName
                        };
                        if (vendor is "0x10de" or "0x1002") sawDiscreteVendor = true;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        var isIntegrated = !sawDiscreteVendor && LooksIntegrated(gpuName, bestVramGb);
        return Recommend(gpuName, bestVramGb, ramGb, isIntegrated, swapGb, isLinux: true, availableRamGb: availGb);
    }

    public static double ParseLinuxMemInfoRamGb(string? memInfoText) =>
        ParseLinuxMemInfoFieldGb(memInfoText, "MemTotal");

    public static double ParseLinuxMemInfoFieldGb(string? memInfoText, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(memInfoText) || string.IsNullOrWhiteSpace(fieldName)) return 0d;
        var match = Regex.Match(memInfoText, $@"^{Regex.Escape(fieldName)}:\s*(\d+)\s*kB", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!match.Success || !ulong.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
            return 0d;
        return Math.Round(kb / (1024d * 1024d), 2);
    }

    public static HardwarePerformanceRecommendation Recommend(
        string? gpuName,
        double vramGb,
        double ramGb,
        bool isSoftwareOrIntegrated = false,
        double swapGb = 0d,
        bool isLinux = false,
        double availableRamGb = 0d)
    {
        var cleanGpu = CleanGpuName(gpuName);
        var cleanVram = double.IsFinite(vramGb) && vramGb > 0 ? Math.Round(vramGb, 2) : 0d;
        var cleanRam = double.IsFinite(ramGb) && ramGb > 0 ? Math.Round(ramGb, 2) : 0d;
        var cleanSwap = double.IsFinite(swapGb) && swapGb > 0 ? Math.Round(swapGb, 2) : 0d;
        var cleanAvail = double.IsFinite(availableRamGb) && availableRamGb > 0 ? Math.Round(availableRamGb, 2) : 0d;

        if (isSoftwareOrIntegrated || LooksIntegrated(cleanGpu, cleanVram))
        {
            if (cleanRam > 0 && cleanRam < 12.0 || cleanVram > 0 && cleanVram < 2.5)
            {
                return new(cleanGpu, cleanVram, cleanRam, UltraPotato,
                    $"Detected {cleanGpu} ({FormatMem(cleanVram, cleanRam)}) — selected Ultra Potato for maximum stability on shared/low memory.",
                    cleanSwap, cleanAvail);
            }
            return new(cleanGpu, cleanVram, cleanRam, Low512,
                $"Detected {cleanGpu} ({FormatMem(cleanVram, cleanRam)}) — selected Low VRAM (512px) to keep memory headroom safe.",
                cleanSwap, cleanAvail);
        }

        if (cleanVram <= 0)
        {
            // Dedicated VRAM unavailable (e.g. Linux NVIDIA without sysfs VRAM): use safe streamed preset or 512px on low-RAM systems.
            if (cleanRam > 0 && cleanRam <= 10.0)
            {
                return new(cleanGpu, cleanVram, cleanRam, Low512,
                    $"Detected {cleanRam:0.#} GB system RAM (GPU VRAM unreported) — selected Low VRAM (512px) for safe memory usage.",
                    cleanSwap, cleanAvail);
            }
            return new(cleanGpu, cleanVram, cleanRam, Balanced,
                $"GPU VRAM could not be queried ({cleanGpu}) — selected Balanced streamed textures as a safe default.",
                cleanSwap, cleanAvail);
        }

        bool meetsLinuxCommit = !isLinux || (cleanRam + cleanSwap) >= UltraMinLinuxTotalCommitGb;
        if (cleanVram > UltraMinDedicatedVramExclusiveGb && cleanRam >= UltraMinSystemRamGb && meetsLinuxCommit)
        {
            return new(cleanGpu, cleanVram, cleanRam, Ultra,
                $"Detected {cleanGpu} ({FormatMem(cleanVram, cleanRam)}) — plenty of headroom (> 18 GB VRAM, 40+ GB RAM) for Ultra full-resolution textures.",
                cleanSwap, cleanAvail);
        }

        if (cleanVram >= 7.5 && (cleanRam <= 0 || cleanRam >= 14.0))
        {
            return new(cleanGpu, cleanVram, cleanRam, High,
                $"Detected {cleanGpu} ({FormatMem(cleanVram, cleanRam)}) — selected High streamed textures (~8–10 GB VRAM) to prevent Full (Ultra) VRAM/GTT spillover.",
                cleanSwap, cleanAvail);
        }

        if (cleanVram >= 5.5)
        {
            return new(cleanGpu, cleanVram, cleanRam, Balanced,
                $"Detected {cleanGpu} with {cleanVram:0.#} GB VRAM — selected Balanced streamed textures for smooth frame pacing.",
                cleanSwap, cleanAvail);
        }

        if (cleanVram >= 3.5)
        {
            return new(cleanGpu, cleanVram, cleanRam, Low512,
                $"Detected {cleanGpu} with {cleanVram:0.#} GB VRAM — selected Low VRAM (512px) to avoid D3D12 out-of-memory crashes.",
                cleanSwap, cleanAvail);
        }

        return new(cleanGpu, cleanVram, cleanRam, UltraPotato,
            $"Detected {cleanGpu} with {cleanVram:0.#} GB VRAM — selected Ultra Potato for maximum low-VRAM stability.",
            cleanSwap, cleanAvail);
    }

    /// <summary>
    /// Returns true when the detected hardware falls below Full (Ultra)'s safe memory envelope
    /// (&lt;= 18 GB dedicated VRAM or &lt; 40 GiB physical RAM, or &lt; 48 GiB RAM+swap on Linux),
    /// where Full (Ultra)'s ~27 GiB VRAM+GTT footprint causes 1 FPS stutters, TAA smear, or swap exhaustion.
    /// </summary>
    public static bool ShouldWarnAboutLegacyFullTier(HardwarePerformanceRecommendation hw, bool isLinux = false)
    {
        ArgumentNullException.ThrowIfNull(hw);
        if (hw.VramGb <= 0 && hw.RamGb <= 0) return false;
        if (hw.VramGb > 0 && hw.VramGb <= UltraMinDedicatedVramExclusiveGb) return true;
        if (hw.RamGb > 0 && hw.RamGb < UltraMinSystemRamGb) return true;
        if (isLinux && hw.RamGb > 0 && (hw.RamGb + Math.Max(0d, hw.SwapGb)) < UltraMinLinuxTotalCommitGb) return true;
        return false;
    }

    public static (string SuggestedTierId, string WarningReason) BuildLegacyFullSpilloverAdvisory(HardwarePerformanceRecommendation hw)
    {
        ArgumentNullException.ThrowIfNull(hw);
        var suggested = IsConcreteTier(hw.RecommendedTierId) && hw.RecommendedTierId != Ultra
            ? hw.RecommendedTierId
            : High;
        var tierDef = GetTier(suggested, hw);
        var mem = FormatMem(hw.VramGb, hw.RamGb);
        var reason = $"Full (Ultra) uses ~27 GiB of GPU/GTT memory and can cause 1 FPS stutters, TAA smear, or swap exhaustion on {mem} systems — switch to {tierDef.DisplayName} streamed textures (~8–10 GiB VRAM)?";
        return (suggested, reason);
    }

    static bool LooksIntegrated(string gpuName, double vramGb)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return false;
        if (gpuName.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) ||
            gpuName.Contains("lavapipe", StringComparison.OrdinalIgnoreCase))
            return true;
        if (vramGb >= 4.5) return false; // Intel Arc / discrete GPUs with >= 6 GB VRAM are not integrated potato targets
        return gpuName.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
               gpuName.Equals("Intel Graphics", StringComparison.OrdinalIgnoreCase) ||
               (gpuName.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) && vramGb < 2.5) ||
               (gpuName.Contains("Steam Custom GPU", StringComparison.OrdinalIgnoreCase) && vramGb < 4.5);
    }

    static string FormatMem(double vramGb, double ramGb)
    {
        if (vramGb > 0 && ramGb > 0) return $"{vramGb:0.#} GB VRAM, {ramGb:0.#} GB RAM";
        if (vramGb > 0) return $"{vramGb:0.#} GB VRAM";
        if (ramGb > 0) return $"{ramGb:0.#} GB RAM";
        return "memory size unavailable";
    }

    static string CleanGpuName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown GPU";
        var cleaned = Regex.Replace(name, @"[\r\n\t\p{C}]+", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) || cleaned.Equals("unavailable", StringComparison.OrdinalIgnoreCase)
            ? "Unknown GPU"
            : cleaned;
    }

    static string SafeReadHead(string path, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[maxBytes];
            var read = stream.ReadAtLeast(buffer, maxBytes, throwOnEndOfStream: false);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
