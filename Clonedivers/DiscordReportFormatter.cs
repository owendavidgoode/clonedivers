using System.Text;
using System.Text.RegularExpressions;

namespace Clonedivers;

public sealed record DiscordReportInput(
    string? AppVersion,
    string? Platform,
    string? GameBuild,
    string? SquadMatchCode,
    string Mode,
    string EnemySkin,
    bool Covenant,
    bool DeltaSquad,
    string PerformanceTier,
    string PackTexture,
    HardwarePerformanceRecommendation? Hardware,
    string? InstallRoot,
    CrashTriageResult? Triage,
    bool RedactUserPaths = true,
    string? SquidSkin = null,
    bool DeathTrooperVoice = false,
    string? HostTheaterPreset = null);

/// <summary>
/// Formats a concise 10–14 line Discord markdown diagnostic snippet covering version,
/// squad match code, mode/tier, GPU/RAM, install root, last session triage, and bounded error lines.
/// </summary>
public static class DiscordReportFormatter
{
    public const int MinLineCount = 10;
    public const int MaxLineCount = 14;
    public const int MaxTailErrorLines = 4;

    public static string Format(DiscordReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var version = string.IsNullOrWhiteSpace(input.AppVersion) ? AppInfo.Version : input.AppVersion.Trim();
        var platform = string.IsNullOrWhiteSpace(input.Platform)
            ? (OperatingSystem.IsLinux() ? "Linux" : "Windows")
            : input.Platform.Trim();
        var build = string.IsNullOrWhiteSpace(input.GameBuild) ? HellForgePack.SupportedBuild : input.GameBuild.Trim();
        var matchCode = string.IsNullOrWhiteSpace(input.SquadMatchCode)
            ? SquadFingerprint.Compute(HellForgeGameplayCatalog.Current)
            : input.SquadMatchCode.Trim();

        var hw = input.Hardware ?? PerformanceTierAdvisor.ScanHardware();
        var gpuLabel = string.IsNullOrWhiteSpace(hw.GpuName) ? "Unknown GPU" : SingleLine(hw.GpuName, 64);
        var vramText = hw.VramGb > 0 ? $"{hw.VramGb:0.#} GB VRAM" : "VRAM n/a";
        var ramText = hw.RamGb > 0 ? $"{hw.RamGb:0.#} GB RAM" : "RAM n/a";

        var modeSummary = FormatModeSummary(input.Mode, input.EnemySkin, input.Covenant, input.DeltaSquad, input.SquidSkin, input.DeathTrooperVoice, input.HostTheaterPreset);
        var tierSummary = FormatTierSummary(input.PerformanceTier, input.PackTexture, hw);
        var rootText = FormatInstallRoot(input.InstallRoot, input.RedactUserPaths);
        var sessionSummary = FormatTriageSummary(input.Triage);

        // Exactly 8 header lines + opening code fence + 1..4 tail lines + closing code fence = 11..14 lines!
        var lines = new List<string>(MaxLineCount)
        {
            "**Clonedivers Support Report**",
            $"- **Version:** `v{version}` ({platform} · Build `{build}`)",
            $"- **Squad Match:** `{matchCode}`",
            $"- **Mode & Mix:** {modeSummary}",
            $"- **Performance Tier:** {tierSummary}",
            $"- **Hardware:** {gpuLabel} ({vramText}) · {ramText}",
            $"- **Install Root:** `{rootText}`",
            $"- **Last Session:** {sessionSummary}",
            "```text"
        };

        var errorLines = (input.Triage?.RelevantLogLines ?? [])
            .Select(l => SquadReportClient.ScrubSensitiveText(l, 140))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .TakeLast(MaxTailErrorLines)
            .ToList();

        if (errorLines.Count == 0)
        {
            lines.Add(input.Triage is { Kind: CrashKind.Clean }
                ? "No error lines recorded (clean session)."
                : "No modloader error lines recorded.");
        }
        else
        {
            foreach (var err in errorLines)
                lines.Add(err.Replace("```", "'''", StringComparison.Ordinal));
        }

        lines.Add("```");

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i]);
        }
        return sb.ToString();
    }

    public static string Format(
        string? appVersion,
        string? squadMatchCode,
        string mode,
        string enemySkin,
        bool covenant,
        bool deltaSquad,
        string performanceTier,
        string packTexture,
        HardwarePerformanceRecommendation? hardware,
        string? installRoot,
        CrashTriageResult? triage,
        bool redactUserPaths = true,
        string? squidSkin = null,
        bool deathTrooperVoice = false,
        string? hostTheaterPreset = null) =>
        Format(new DiscordReportInput(
            AppVersion: appVersion,
            Platform: OperatingSystem.IsLinux() ? "Linux" : "Windows",
            GameBuild: HellForgePack.SupportedBuild,
            SquadMatchCode: squadMatchCode,
            Mode: mode,
            EnemySkin: enemySkin,
            Covenant: covenant,
            DeltaSquad: deltaSquad,
            PerformanceTier: performanceTier,
            PackTexture: packTexture,
            Hardware: hardware,
            InstallRoot: installRoot,
            Triage: triage,
            RedactUserPaths: redactUserPaths,
            SquidSkin: squidSkin,
            DeathTrooperVoice: deathTrooperVoice,
            HostTheaterPreset: hostTheaterPreset));

    static string FormatModeSummary(string mode, string enemySkin, bool covenant, bool deltaSquad, string? squidSkin, bool deathTrooperVoice, string? hostTheaterPreset)
    {
        bool isEmpire = string.Equals(mode, "empire", StringComparison.OrdinalIgnoreCase);
        var theaterSuffix = HostMutatorConfig.IsUltrakillTheater(hostTheaterPreset)
            ? (isEmpire ? " · L12 ULTRAKILL (Stormtrooper)" : " · L12 ULTRAKILL")
            : "";
        if (string.Equals(mode, "regular", StringComparison.OrdinalIgnoreCase))
            return "Regular HellForge (Vanilla visuals)" + theaterSuffix;

        var universe = isEmpire
            ? (deathTrooperVoice ? "EmpireDivers (Death Trooper VO)" : "EmpireDivers")
            : (deltaSquad ? "Clonedivers (Delta Squad)" : "Clonedivers");

        var bots = enemySkin switch
        {
            "mixed-droids" => "Mixed Droids+Bots",
            "native-automatons" => "Native Automatons",
            _ => "All CIS Droids"
        };

        var cov = squidSkin switch
        {
            "mixed-covenant" => "Mixed Cov+Squids",
            "all-covenant" => "Covenant ON",
            "native-squids" => "Covenant OFF",
            _ => covenant ? "Covenant ON" : "Covenant OFF"
        };
        return $"{universe} · {bots} · {cov}{theaterSuffix}";
    }

    static string FormatTierSummary(string performanceTier, string packTexture, HardwarePerformanceRecommendation hw)
    {
        var tex = string.IsNullOrWhiteSpace(packTexture) ? "full" : packTexture.Trim();
        if (string.IsNullOrWhiteSpace(performanceTier) || string.Equals(performanceTier, PerformanceTierAdvisor.Auto, StringComparison.OrdinalIgnoreCase))
        {
            var resolved = PerformanceTierAdvisor.GetTier(hw.RecommendedTierId, hw);
            return $"Auto → {resolved.DisplayName} (`{tex}`)";
        }
        var tier = PerformanceTierAdvisor.GetTier(performanceTier, hw);
        return $"{tier.DisplayName} (`{tex}`)";
    }

    static string FormatInstallRoot(string? installRoot, bool redactUserPaths)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return "not configured";

        var clean = SingleLine(installRoot, 120).Replace('`', '\'');
        if (!redactUserPaths)
            return clean;

        clean = Regex.Replace(clean, @"(?i)([A-Z]:[\\/]+Users[\\/]+)[^\\/\s""']+", "$1<user>");
        clean = Regex.Replace(clean, @"(?i)(/(?:home|Users)/)[^/\s""']+", "$1<user>");
        return clean;
    }

    static string FormatTriageSummary(CrashTriageResult? triage)
    {
        if (triage == null)
            return "Clean (no session crashes recorded)";

        var exitPart = triage.ExitCode is { } code ? $"exit {code}" : "no exit code";
        var summary = SingleLine(SquadReportClient.ScrubSensitiveText(triage.Summary, 110), 110);
        return $"{triage.Kind} ({exitPart}) — {summary}";
    }

    static string SingleLine(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var s = Regex.Replace(value, @"[\r\n\t\p{C}]+", " ").Trim();
        return s.Length <= maxLen ? s : s[..maxLen] + "…";
    }
}
