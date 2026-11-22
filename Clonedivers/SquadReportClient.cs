using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Clonedivers;

public sealed record SquadSessionReport(
    [property: JsonPropertyName("player")] string Player,
    [property: JsonPropertyName("launcherVersion")] string LauncherVersion,
    [property: JsonPropertyName("exitCode")] int ExitCode,
    [property: JsonPropertyName("hostLabel")] string? HostLabel = null,
    [property: JsonPropertyName("matchHash")] string? MatchHash = null,
    [property: JsonPropertyName("mode")] string? Mode = null,
    [property: JsonPropertyName("perfTier")] string? PerfTier = null,
    [property: JsonPropertyName("gpu")] string? Gpu = null,
    [property: JsonPropertyName("ramGb")] double? RamGb = null,
    [property: JsonPropertyName("durationSec")] double? DurationSec = null,
    [property: JsonPropertyName("avgFps")] double? AvgFps = null,
    [property: JsonPropertyName("low1Fps")] double? Low1Fps = null,
    [property: JsonPropertyName("statusSummary")] string? StatusSummary = null);

public sealed record SquadCrashReport(
    [property: JsonPropertyName("player")] string Player,
    [property: JsonPropertyName("launcherVersion")] string LauncherVersion,
    [property: JsonPropertyName("exitCode")] int ExitCode,
    [property: JsonPropertyName("crashClass")] string CrashClass,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("matchHash")] string? MatchHash = null,
    [property: JsonPropertyName("mode")] string? Mode = null,
    [property: JsonPropertyName("perfTier")] string? PerfTier = null,
    [property: JsonPropertyName("gpu")] string? Gpu = null,
    [property: JsonPropertyName("ramGb")] double? RamGb = null,
    [property: JsonPropertyName("logTail")] string? LogTail = null);

/// <summary>
/// Fire-and-forget async squad session &amp; crash uploader to https://clonedivers-telemetry.goodecraft.com/{session,crash}.
/// Aligned with the Milestone 1 Worker contract (#36): 32 KiB max JSON, 5-second timeout, never throws,
/// never uploads HarmlessQuit crashes, and strips all Steam tokens, credentials, user paths, and capture references.
/// </summary>
public sealed class SquadReportClient
{
    public const string DefaultBaseUrl = "https://clonedivers-telemetry.goodecraft.com";
    public const int MaxPayloadBytes = 32 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly HttpClient http;
    readonly string baseUrl;
    readonly TimeSpan timeout;

    public SquadReportClient(HttpClient? http = null, string? baseUrl = null, TimeSpan? timeout = null)
    {
        this.http = http ?? SharedHttp;
        this.baseUrl = NormalizeBaseUrl(baseUrl ?? DefaultBaseUrl);
        this.timeout = timeout ?? DefaultTimeout;
    }

    public static bool ShouldUploadCrash(CrashTriageResult? triage) =>
        triage != null &&
        triage.ShouldUploadCrash &&
        triage.Kind is not (CrashKind.Clean or CrashKind.HarmlessQuit or CrashKind.ModMismatch or CrashKind.MissingLinuxNetwork);

    /// <summary>Fire-and-forget post-session upload: sends /session, and sends /crash ONLY on real unexpected crashes (never HarmlessQuit).</summary>
    public void ReportSessionCompletedFireAndForget(
        SquadSessionReport session,
        CrashTriageResult? triage = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SendSessionAsync(session).ConfigureAwait(false);
                if (ShouldUploadCrash(triage))
                {
                    var crash = BuildCrashReportFromSession(session, triage!);
                    await SendCrashAsync(crash, triage).ConfigureAwait(false);
                }
            }
            catch
            {
                // Fire-and-forget: never propagate telemetry errors.
            }
        });
    }

    public async Task<bool> SendSessionAsync(SquadSessionReport report, CancellationToken ct = default)
    {
        if (report == null) return false;
        var sanitized = SanitizeSessionReport(report);
        return await PostJsonSafeAsync("/session", sanitized, ct).ConfigureAwait(false);
    }

    public async Task<bool> SendCrashAsync(SquadCrashReport report, CrashTriageResult? triageGuard = null, CancellationToken ct = default)
    {
        if (report == null) return false;
        if (triageGuard != null && !ShouldUploadCrash(triageGuard))
            return false;
        if (string.Equals(report.CrashClass, nameof(CrashKind.HarmlessQuit), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(report.CrashClass, nameof(CrashKind.Clean), StringComparison.OrdinalIgnoreCase))
            return false;

        var sanitized = SanitizeCrashReport(report);
        return await PostJsonSafeAsync("/crash", sanitized, ct).ConfigureAwait(false);
    }

    public static SquadCrashReport BuildCrashReportFromSession(SquadSessionReport session, CrashTriageResult triage) =>
        new(
            Player: session.Player,
            LauncherVersion: session.LauncherVersion,
            ExitCode: triage.ExitCode ?? (session.ExitCode == 0 ? 1 : session.ExitCode),
            CrashClass: triage.Kind.ToString(),
            Summary: triage.Summary,
            MatchHash: session.MatchHash,
            Mode: session.Mode,
            PerfTier: session.PerfTier,
            Gpu: session.Gpu,
            RamGb: session.RamGb,
            LogTail: triage.RelevantLogLines.Count == 0 ? null : string.Join("\n", triage.RelevantLogLines.TakeLast(8)));

    async Task<bool> PostJsonSafeAsync<T>(string relativePath, T payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(baseUrl)) return false;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(timeout);

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
                return false;

            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + relativePath);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Clonedivers", SanitizeToken(AppInfo.Version, 32)));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static SquadSessionReport SanitizeSessionReport(SquadSessionReport r) =>
        r with
        {
            Player = NormalizePlayer(r.Player),
            LauncherVersion = SanitizeToken(r.LauncherVersion, 32),
            HostLabel = string.IsNullOrWhiteSpace(r.HostLabel) ? null : ScrubSensitiveText(r.HostLabel, 40),
            MatchHash = string.IsNullOrWhiteSpace(r.MatchHash) ? null : SanitizeToken(r.MatchHash, 24),
            Mode = string.IsNullOrWhiteSpace(r.Mode) ? null : SanitizeToken(r.Mode, 24),
            PerfTier = string.IsNullOrWhiteSpace(r.PerfTier) ? null : SanitizeToken(r.PerfTier, 24),
            Gpu = string.IsNullOrWhiteSpace(r.Gpu) ? null : ScrubSensitiveText(r.Gpu, 120),
            RamGb = CleanFinite(r.RamGb),
            DurationSec = CleanFinite(r.DurationSec),
            AvgFps = CleanFinite(r.AvgFps),
            Low1Fps = CleanFinite(r.Low1Fps),
            StatusSummary = string.IsNullOrWhiteSpace(r.StatusSummary) ? null : ScrubSensitiveText(r.StatusSummary, 240)
        };

    public static SquadCrashReport SanitizeCrashReport(SquadCrashReport r) =>
        r with
        {
            Player = NormalizePlayer(r.Player),
            LauncherVersion = SanitizeToken(r.LauncherVersion, 32),
            CrashClass = SanitizeToken(r.CrashClass, 32),
            Summary = ScrubSensitiveText(string.IsNullOrWhiteSpace(r.Summary) ? "Unexpected game exit." : r.Summary, 240),
            MatchHash = string.IsNullOrWhiteSpace(r.MatchHash) ? null : SanitizeToken(r.MatchHash, 24),
            Mode = string.IsNullOrWhiteSpace(r.Mode) ? null : SanitizeToken(r.Mode, 24),
            PerfTier = string.IsNullOrWhiteSpace(r.PerfTier) ? null : SanitizeToken(r.PerfTier, 24),
            Gpu = string.IsNullOrWhiteSpace(r.Gpu) ? null : ScrubSensitiveText(r.Gpu, 120),
            RamGb = CleanFinite(r.RamGb),
            LogTail = SanitizeMultilineLogTail(r.LogTail)
        };

    static string NormalizePlayer(string? player)
    {
        var scrubbed = ScrubSensitiveText(player, 40).Trim();
        return string.IsNullOrWhiteSpace(scrubbed) ? "Anonymous" : scrubbed;
    }

    static string? SanitizeMultilineLogTail(string? logTail)
    {
        if (string.IsNullOrWhiteSpace(logTail)) return null;
        var cleanedLines = logTail
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(8)
            .Select(l => ScrubSensitiveText(l, 220))
            .Where(l => l.Length > 0)
            .ToList();
        if (cleanedLines.Count == 0) return null;
        var joined = string.Join("\n", cleanedLines);
        return joined.Length <= 2000 ? joined : joined[..2000];
    }

    /// <summary>Strips Steam IDs, Bearer/auth tokens, hex secrets, Discord @everyone/@here mentions, URL queries, account capture files, and OS user home paths.</summary>
    public static string ScrubSensitiveText(string? value, int maxLength = 240)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var s = Regex.Replace(value, @"[\r\n\t\p{C}]+", " ").Trim();
        s = Regex.Replace(s, @"(?i)Bearer\s+[A-Za-z0-9._~+/=-]+", "Bearer [redacted]");
        s = Regex.Replace(s, @"(?i)(?:steamLoginSecure|sessionid|token|password|secret|apikey|api_key|authorization)\s*[:=]\s*\S+", "[redacted-credential]");
        s = Regex.Replace(s, @"\b7656119\d{10}\b", "<steam-id>");
        s = Regex.Replace(s, @"\b[0-9a-fA-F]{40,128}\b", "<hex-redacted>");
        s = Regex.Replace(s, @"(?i)([A-Z]:[\\/]+Users[\\/]+)[^\\/\s""']+", "$1<user>");
        s = Regex.Replace(s, @"(?i)(/(?:home|Users)/)[^/\s""']+", "$1<user>");
        s = Regex.Replace(s, @"https?://[^\s?]+\?[^\s]+", "<url-redacted>");
        s = Regex.Replace(s, @"(?i)@(?:everyone|here)\b", "at-mention");
        s = Regex.Replace(s, @"(?i)(?:real-inventory\.json|progression\.json|seal\.json|account-snapshot)", "[account-redacted]");
        return s.Length <= maxLength ? s : s[..maxLength];
    }

    static string SanitizeToken(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var clean = Regex.Replace(value.Trim(), @"[^A-Za-z0-9._+-]", "");
        return string.IsNullOrEmpty(clean) ? "unknown" : clean[..Math.Min(clean.Length, maxLength)];
    }

    static double? CleanFinite(double? number) =>
        number is { } n && double.IsFinite(n) && n >= 0d ? Math.Round(n, 2) : null;

    static string NormalizeBaseUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return "";
        }
        return uri.ToString().TrimEnd('/');
    }
}
