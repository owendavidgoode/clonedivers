using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

public enum CrashKind
{
    Clean,
    HarmlessQuit,
    OutOfVram,
    ModMismatch,
    MissingLinuxNetwork,
    UnexpectedCrash
}

public sealed record StepDownSuggestion(bool ShouldSuggest, string? SuggestedTierId, string? Reason)
{
    public static implicit operator bool(StepDownSuggestion suggestion) => suggestion.ShouldSuggest;
}

public sealed record CrashTriageResult(
    CrashKind Kind,
    int? ExitCode,
    string Summary,
    string? RecommendedAction,
    bool SuggestStepDown,
    string? SuggestedTierId,
    bool ShouldUploadCrash,
    IReadOnlyList<string> RelevantLogLines,
    string? SessionId = null,
    DateTimeOffset? CompletedUtc = null);

/// <summary>
/// Reads result.json and a bounded 64 KB tail of modloader.log (symlink-safe) to classify
/// the last game session in plain English and advise automatic performance step-downs.
/// </summary>
public static class CrashTriage
{
    public const int MaxLogTailBytes = 64 * 1024;
    public const double LowFpsThreshold = 35.0;
    public const string HarmlessQuitMessage = "Harmless game-exit bug — your mission saved fine.";

    internal static bool HasEngineCrashLine(string line) =>
        line.Contains("Access violation", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Crash report", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[Crash]", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("c0000005", StringComparison.OrdinalIgnoreCase);

    static readonly Regex QuitCrashLine = new(
        @"helldivers2\.exe\+0x0*626296\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex VramFaultPattern = new(
        @"(DXGI_ERROR_DEVICE_HUNG|DXGI_ERROR_DEVICE_REMOVED|DXGI_ERROR_DEVICE_RESET|device\s+hung|device\s+removed|" +
        @"E_OUTOFMEMORY|out\s+of\s+(?:video\s+)?memory|out\s+of\s+vram|insufficient\s+vram|0x887a0005|0x887a0006|0x8007000e|" +
        @"D3D12[^\r\n]{0,80}(?:hung|oom|out\s+of\s+memory|failed|fault|removed|crash)|" +
        @"(?:\+0x0*|\b)700AFB\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex ModMismatchPattern = new(
        @"(\bALAMO041\b|mod(?:list)?\s+mismatch|incompatible\s+mod\s+list|host\s+mod\s+mismatch)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex MissingPastaPattern = new(
        @"(\bpasta\b[^\r\n]{0,80}(?:missing|not\s+found|unavailable|install|required|no\s+such\s+file)|" +
        @"(?:missing|not\s+found|install|requires?)\s+[^\r\n]{0,40}\b(?:pasta|passt)\b|" +
        @"\bpasst\b[^\r\n]{0,80}(?:missing|not\s+found|unavailable|install|required))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static StepDownSuggestion ShouldSuggestStepDownFromFps(double? avgFps, string? currentTierId)
    {
        if (avgFps is not { } fps || !double.IsFinite(fps) || fps <= 0d || fps >= LowFpsThreshold)
            return new(false, null, null);

        var next = PerformanceTierAdvisor.NextLowerTier(currentTierId);
        if (next == null)
            return new(false, null, null);

        var tierDef = PerformanceTierAdvisor.GetTier(next);
        return new(
            ShouldSuggest: true,
            SuggestedTierId: next,
            Reason: $"Average frame rate was {fps:0.#} FPS (< 35 FPS). Stepping down to {tierDef.DisplayName} lowers GPU load.");
    }

    public static CrashTriageResult AnalyzeLastSession(string? installRoot, HellForgeStatus? status, string? currentTierId = null)
    {
        var statusHints = string.Join("\n", new[] { status?.LastFailure, status?.LastNotice, status?.CompatibilityProblem }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        // Check immediate Linux network tool missing notice even before a session folder exists.
        if (MissingPastaPattern.IsMatch(statusHints))
        {
            return new(
                Kind: CrashKind.MissingLinuxNetwork,
                ExitCode: null,
                Summary: "Linux network isolation tool 'passt' (pasta) is missing.",
                RecommendedAction: "Run: sudo apt install passt",
                SuggestStepDown: false,
                SuggestedTierId: null,
                ShouldUploadCrash: false,
                RelevantLogLines: ExtractRelevantLines(SplitLines(statusHints)));
        }

        var sessionDir = FindLatestSessionDirectory(installRoot);
        if (sessionDir == null)
        {
            if (!string.IsNullOrWhiteSpace(status?.LastFailure))
            {
                if (ModMismatchPattern.IsMatch(status.LastFailure))
                    return BuildModMismatch(null, SplitLines(status.LastFailure), null, null);
                if (VramFaultPattern.IsMatch(status.LastFailure))
                    return BuildOutOfVram(null, currentTierId, SplitLines(status.LastFailure), null, null);

                return new(
                    Kind: CrashKind.UnexpectedCrash,
                    ExitCode: null,
                    Summary: "Last session reported an unexpected error.",
                    RecommendedAction: "Run Verify & Repair or copy the Discord support report if this repeats.",
                    SuggestStepDown: false,
                    SuggestedTierId: null,
                    ShouldUploadCrash: true,
                    RelevantLogLines: ExtractRelevantLines(SplitLines(status.LastFailure)));
            }

            return new(
                Kind: CrashKind.Clean,
                ExitCode: 0,
                Summary: "Last session exited cleanly.",
                RecommendedAction: null,
                SuggestStepDown: false,
                SuggestedTierId: null,
                ShouldUploadCrash: false,
                RelevantLogLines: []);
        }

        var sessionId = Path.GetFileName(sessionDir);
        var (exitCode, completedUtc, resultText) = ReadResultJson(sessionDir);
        var logLines = ReadBoundedModloaderTail(sessionDir);
        var engineLines = ReadBoundedEngineLogTail(sessionDir);
        var allSessionLines = logLines.Count > 0 && engineLines.Count > 0
            ? logLines.Concat(engineLines).ToList()
            : (logLines.Count > 0 ? logLines : engineLines);
        bool hasCrashDump = HasCrashDumpFile(sessionDir);
        var combinedText = string.Join("\n", new[] { statusHints, resultText, string.Join("\n", allSessionLines) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        // 1. Missing Linux pasta / passt network helper
        if (MissingPastaPattern.IsMatch(combinedText))
        {
            return new(
                Kind: CrashKind.MissingLinuxNetwork,
                ExitCode: exitCode,
                Summary: "Linux network isolation tool 'passt' (pasta) is missing.",
                RecommendedAction: "Install passt with: sudo apt install passt",
                SuggestStepDown: false,
                SuggestedTierId: null,
                ShouldUploadCrash: false,
                RelevantLogLines: ExtractRelevantLines(allSessionLines.Count > 0 ? allSessionLines : SplitLines(combinedText)),
                SessionId: sessionId,
                CompletedUtc: completedUtc);
        }

        // 2. Co-op mod mismatch (ALAMO041)
        if (ModMismatchPattern.IsMatch(combinedText))
        {
            return BuildModMismatch(exitCode, allSessionLines.Count > 0 ? allSessionLines : SplitLines(combinedText), sessionId, completedUtc);
        }

        // 3. Out of VRAM / D3D12 device hung / 700AFB (check before exitCode==0 so sandboxed GPU hangs with wrapper exit 0 are caught)
        if (VramFaultPattern.IsMatch(combinedText))
        {
            return BuildOutOfVram(exitCode, currentTierId, allSessionLines.Count > 0 ? allSessionLines : SplitLines(combinedText), sessionId, completedUtc);
        }

        // 4. Clean exit (exitCode == 0 and no crash dump / engine crash lines)
        bool hasEngineCrashLine = engineLines.Any(HasEngineCrashLine);
        if (exitCode == 0 && !hasCrashDump && !hasEngineCrashLine)
        {
            return new(
                Kind: CrashKind.Clean,
                ExitCode: 0,
                Summary: "Last session exited cleanly.",
                RecommendedAction: null,
                SuggestStepDown: false,
                SuggestedTierId: null,
                ShouldUploadCrash: false,
                RelevantLogLines: [],
                SessionId: sessionId,
                CompletedUtc: completedUtc);
        }

        // 5. Harmless documented quit-time crash (helldivers2.exe+0x626296, exit 1)
        if ((exitCode == 1 || exitCode == 0 || exitCode == null) && IsHarmlessQuitCrash(allSessionLines, status?.LastNotice))
        {
            return new(
                Kind: CrashKind.HarmlessQuit,
                ExitCode: exitCode ?? 1,
                Summary: HarmlessQuitMessage,
                RecommendedAction: null,
                SuggestStepDown: false,
                SuggestedTierId: null,
                ShouldUploadCrash: false,
                RelevantLogLines: ExtractRelevantLines(allSessionLines),
                SessionId: sessionId,
                CompletedUtc: completedUtc);
        }

        // 6. Other non-zero or crash-dumped unexpected crash
        var codeLabel = exitCode is { } code && code != 0
            ? ((uint)code >= 0x10000u ? $"exit 0x{(uint)code:X8}" : $"exit {code}")
            : (hasCrashDump ? "crash dump recorded" : "abnormal exit");
        return new(
            Kind: CrashKind.UnexpectedCrash,
            ExitCode: exitCode,
            Summary: $"Helldivers 2 closed unexpectedly ({codeLabel}).",
            RecommendedAction: "Run Verify & Repair if files changed, or share the Discord report for help.",
            SuggestStepDown: false,
            SuggestedTierId: null,
            ShouldUploadCrash: true,
            RelevantLogLines: ExtractRelevantLines(allSessionLines.Count > 0 ? allSessionLines : SplitLines(combinedText)),
            SessionId: sessionId,
            CompletedUtc: completedUtc);
    }

    static CrashTriageResult BuildOutOfVram(
        int? exitCode,
        string? currentTierId,
        IReadOnlyList<string> lines,
        string? sessionId,
        DateTimeOffset? completedUtc)
    {
        var nextTier = PerformanceTierAdvisor.NextLowerTier(currentTierId);
        if (nextTier != null)
        {
            var nextDef = PerformanceTierAdvisor.GetTier(nextTier);
            return new(
                Kind: CrashKind.OutOfVram,
                ExitCode: exitCode,
                Summary: $"GPU ran out of video memory or hung (D3D12) — try stepping down to {nextDef.DisplayName}.",
                RecommendedAction: $"Switch Performance Tier to {nextDef.DisplayName} ({nextDef.MappedPackTexture}) to cut GPU memory pressure.",
                SuggestStepDown: true,
                SuggestedTierId: nextTier,
                ShouldUploadCrash: true,
                RelevantLogLines: ExtractRelevantLines(lines),
                SessionId: sessionId,
                CompletedUtc: completedUtc);
        }

        return new(
            Kind: CrashKind.OutOfVram,
            ExitCode: exitCode,
            Summary: "GPU ran out of video memory or hung (D3D12) even on Ultra Potato.",
            RecommendedAction: "Lower in-game Helldivers 2 texture/render scale settings and close background GPU apps.",
            SuggestStepDown: false,
            SuggestedTierId: null,
            ShouldUploadCrash: true,
            RelevantLogLines: ExtractRelevantLines(lines),
            SessionId: sessionId,
            CompletedUtc: completedUtc);
    }

    static CrashTriageResult BuildModMismatch(
        int? exitCode,
        IReadOnlyList<string> lines,
        string? sessionId,
        DateTimeOffset? completedUtc) =>
        new(
            Kind: CrashKind.ModMismatch,
            ExitCode: exitCode,
            Summary: "Co-op mod mismatch (ALAMO041) — squad gameplay mods are out of sync.",
            RecommendedAction: "Click Verify & Repair and confirm everyone shows the same Squad Match Code.",
            SuggestStepDown: false,
            SuggestedTierId: null,
            ShouldUploadCrash: false,
            RelevantLogLines: ExtractRelevantLines(lines),
            SessionId: sessionId,
            CompletedUtc: completedUtc);

    static bool IsHarmlessQuitCrash(IReadOnlyList<string> logLines, string? statusNotice)
    {
        bool sawQuitOffset = statusNotice != null && QuitCrashLine.IsMatch(statusNotice);
        foreach (var line in logLines)
        {
            var marker = line.IndexOf(" crash: ", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                var crashPart = line[(marker + 8)..];
                if (QuitCrashLine.IsMatch(crashPart))
                {
                    sawQuitOffset = true;
                }
                else if (!crashPart.StartsWith("fail-fast about to end the game", StringComparison.OrdinalIgnoreCase))
                {
                    // Another distinct crash address was logged in the same session.
                    return false;
                }
            }
            else if (QuitCrashLine.IsMatch(line))
            {
                sawQuitOffset = true;
            }
        }
        return sawQuitOffset;
    }

    static string? FindLatestSessionDirectory(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot)) return null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
            var logs = Path.Combine(root, "logs");
            var sessions = Path.Combine(logs, "sessions");
            FileSafety.RejectLink(root);
            FileSafety.RejectLink(logs);
            FileSafety.RejectLink(sessions);
            if (!Directory.Exists(sessions)) return null;

            string? bestFolder = null;
            DateTimeOffset bestStamp = DateTimeOffset.MinValue;

            foreach (var dir in new DirectoryInfo(sessions).EnumerateDirectories())
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var resultFile = Path.Combine(dir.FullName, "result.json");
                DateTimeOffset stamp = dir.LastWriteTimeUtc;
                try
                {
                    if (File.Exists(resultFile) && (File.GetAttributes(resultFile) & FileAttributes.ReparsePoint) == 0 &&
                        new FileInfo(resultFile).Length <= 1024 * 1024)
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(resultFile));
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty("completedUtc", out var comp) &&
                            comp.ValueKind == JsonValueKind.String &&
                            DateTimeOffset.TryParse(comp.GetString(), out var parsedUtc))
                        {
                            stamp = parsedUtc;
                        }
                        else
                        {
                            stamp = File.GetLastWriteTimeUtc(resultFile);
                        }
                    }
                }
                catch { }

                if (bestFolder == null || stamp >= bestStamp)
                {
                    bestStamp = stamp;
                    bestFolder = dir.FullName;
                }
            }

            return bestFolder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    internal static int? ParseSessionExitCode(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        int? fallbackZero = null;
        if (json.TryGetProperty("exitCode", out var codeEl))
        {
            if (TryParseExitElement(codeEl, out var parsed))
            {
                if (parsed != 0) return parsed;
                fallbackZero = 0;
            }
        }
        if (json.TryGetProperty("gameExitCode", out var gameCodeEl) && TryParseExitElement(gameCodeEl, out var gameCode))
        {
            if (gameCode != 0) return gameCode;
            fallbackZero = 0;
        }
        if (json.TryGetProperty("sandboxExit", out var sbEl) && sbEl.ValueKind == JsonValueKind.Object)
        {
            if (sbEl.TryGetProperty("exitCode", out var sbCodeEl) && TryParseExitElement(sbCodeEl, out var sbCode))
            {
                if (sbCode != 0) return sbCode;
                fallbackZero = 0;
            }
            if (sbEl.TryGetProperty("signal", out var sigEl) && sigEl.TryGetInt32(out var sig) && sig > 0)
                return 128 + sig;
        }
        return fallbackZero;
    }

    static bool TryParseExitElement(JsonElement el, out int code)
    {
        code = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out code))
            return true;
        if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } text)
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex))
            {
                code = unchecked((int)hex);
                return true;
            }
            if (int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out code))
                return true;
        }
        return false;
    }

    static (int? ExitCode, DateTimeOffset? CompletedUtc, string RawSummary) ReadResultJson(string sessionDir)
    {
        var path = Path.Combine(sessionDir, "result.json");
        try
        {
            FileSafety.RejectLink(sessionDir);
            FileSafety.RejectLink(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
                return (null, null, "");

            var raw = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null, "");

            int? exitCode = ParseSessionExitCode(doc.RootElement);

            DateTimeOffset? completedUtc = null;
            if (doc.RootElement.TryGetProperty("completedUtc", out var compEl) &&
                compEl.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(compEl.GetString(), out var dt))
                completedUtc = dt;

            var summaryBuilder = new StringBuilder();
            foreach (var key in new[] { "error", "failure", "message", "status", "cleanupError", "gameExitCode", "sandboxDomainState" })
            {
                if (doc.RootElement.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String && val.GetString() is { Length: > 0 } str)
                    summaryBuilder.AppendLine($"{key}: {str}");
            }

            return (exitCode, completedUtc, summaryBuilder.ToString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return (null, null, "");
        }
    }

    public static IReadOnlyList<string> ReadBoundedModloaderTail(string sessionDir)
    {
        string? logPath = null;
        try
        {
            FileSafety.RejectLink(sessionDir);
            var gameDir = Path.Combine(sessionDir, "game");
            if (Directory.Exists(gameDir))
            {
                FileSafety.RejectLink(gameDir);
                var candidate = Path.Combine(gameDir, "modloader.log");
                if (File.Exists(candidate))
                {
                    FileSafety.RejectLink(candidate);
                    logPath = candidate;
                }
            }

            if (logPath == null)
            {
                var directCandidate = Path.Combine(sessionDir, "modloader.log");
                if (File.Exists(directCandidate))
                {
                    FileSafety.RejectLink(directCandidate);
                    logPath = directCandidate;
                }
            }

            if (logPath == null) return [];

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0L, stream.Length - MaxLogTailBytes);
            stream.Position = start;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            if (start > 0) _ = reader.ReadLine(); // Discard partial leading line

            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(SanitizeLogLine(line));
            }
            return lines;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return [];
        }
    }

    static bool HasCrashDumpFile(string sessionDir)
    {
        try
        {
            FileSafety.RejectLink(sessionDir);
            foreach (var sub in new[] { Path.Combine(sessionDir, "game"), Path.Combine(sessionDir, "game", "userdata") })
            {
                if (!Directory.Exists(sub)) continue;
                FileSafety.RejectLink(sub);
                foreach (var dmp in Directory.EnumerateFiles(sub, "crash-*.dmp"))
                {
                    FileSafety.RejectLink(dmp);
                    return true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        return false;
    }

    static IReadOnlyList<string> ReadBoundedEngineLogTail(string sessionDir)
    {
        try
        {
            FileSafety.RejectLink(sessionDir);
            var candidates = new List<string>();
            var engineDir = Path.Combine(sessionDir, "engine-logs");
            if (Directory.Exists(engineDir))
            {
                FileSafety.RejectLink(engineDir);
                candidates.AddRange(Directory.EnumerateFiles(engineDir, "*.txt").OrderBy(p => p, StringComparer.Ordinal));
            }
            var userLogs = Path.Combine(sessionDir, "game", "userdata", "logs");
            if (candidates.Count == 0 && Directory.Exists(userLogs))
            {
                FileSafety.RejectLink(userLogs);
                candidates.AddRange(Directory.EnumerateFiles(userLogs, "*console*.txt").OrderBy(p => p, StringComparer.Ordinal));
            }
            if (candidates.Count == 0) return [];

            var lines = new List<string>();
            foreach (var file in candidates.TakeLast(2))
            {
                FileSafety.RejectLink(file);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var start = Math.Max(0L, stream.Length - MaxLogTailBytes);
                stream.Position = start;
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                if (start > 0) _ = reader.ReadLine();
                while (reader.ReadLine() is { } line)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        lines.Add(SanitizeLogLine(line));
                }
            }
            return lines;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return [];
        }
    }

    static IReadOnlyList<string> ExtractRelevantLines(IReadOnlyList<string> allLines, int maxLines = 8)
    {
        if (allLines.Count == 0) return [];

        var prioritized = allLines
            .Where(l => Regex.IsMatch(l, @"(crash:|error|exception|fault|fail|hung|out of memory|ALAMO041|pasta|passt|700AFB|626296)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .TakeLast(maxLines)
            .ToList();

        if (prioritized.Count >= Math.Min(3, allLines.Count) || prioritized.Count == maxLines)
            return prioritized;

        return allLines.TakeLast(maxLines).ToList();
    }

    static IReadOnlyList<string> SplitLines(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(SanitizeLogLine)
                .Where(l => l.Length > 0)
                .ToList();

    public static string SanitizeLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";
        var clean = Regex.Replace(line, @"[\r\n\t\p{C}]+", " ").Trim();
        clean = Regex.Replace(clean, @"(?i)([A-Z]:[\\/]+Users[\\/]+)[^\\/\s""']+", "$1<user>");
        clean = Regex.Replace(clean, @"(?i)(/(?:home|Users)/)[^/\s""']+", "$1<user>");
        clean = Regex.Replace(clean, @"\b7656119\d{10}\b", "<steam-id>");
        return clean.Length > 240 ? clean[..240] + "…" : clean;
    }
}
