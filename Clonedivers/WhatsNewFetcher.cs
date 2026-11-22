using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

public sealed record WhatsNewResult(
    string Tag,
    IReadOnlyList<string> Bullets,
    bool IsFallback,
    string ReleaseUrl);

/// <summary>
/// Fetches a GitHub release body by tag (vX.Y.Z / linux-vX.Y.Z) and distills 3–5 concise,
/// player-friendly bullet points (stripping SHA tables, hashes, and markdown noise), with offline fallback.
/// </summary>
public static class WhatsNewFetcher
{
    public const string RepoReleasesApiPrefix = "https://api.github.com/repos/owendavidgoode/clonedivers/releases/tags/";
    public const string RepoReleasesWebPrefix = "https://github.com/owendavidgoode/clonedivers/releases/tag/";

    static readonly Regex ValidTagRegex = new(
        @"^(?:linux-)?v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    static readonly Regex BareVersionRegex = new(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    static readonly Regex TechnicalHeadingRegex = new(
        @"^#{1,6}\s*(?:sha[- ]?256|checksums?|hashes|asset\s+hashes|verification|build\s+inputs|files|installation\s+hashes|artifacts)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex NoiseBulletRegex = new(
        @"(\b[0-9a-fA-F]{40,64}\b|^full\s+changelog\b|\bsha-?256\b|\.(?:tar\.gz|deb|zip|exe)\s*[:—-]\s*\d+|\bbytes\b.*\bsha)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly HttpClient DefaultHttp = CreateHttpClient();
    static readonly ConcurrentDictionary<string, WhatsNewResult> MemoryCache = new(StringComparer.Ordinal);

    static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Clonedivers/{AppInfo.Version}");
        return client;
    }

    public static string NormalizeTag(string? versionOrTag, bool? isLinux = null)
    {
        var linux = isLinux ?? OperatingSystem.IsLinux();
        if (string.IsNullOrWhiteSpace(versionOrTag))
            return (linux ? "linux-v" : "v") + AppInfo.Version;

        var trimmed = versionOrTag.Trim();
        if (ValidTagRegex.IsMatch(trimmed))
            return trimmed;
        if (BareVersionRegex.IsMatch(trimmed))
            return (linux ? "linux-v" : "v") + trimmed;

        return (linux ? "linux-v" : "v") + AppInfo.Version;
    }

    public static async Task<WhatsNewResult> FetchAsync(
        string? versionOrTag = null,
        CancellationToken ct = default,
        HttpClient? client = null,
        string? apiEndpointOverride = null,
        bool? isLinux = null,
        bool bypassCache = false)
    {
        var tag = NormalizeTag(versionOrTag, isLinux);
        if (!ValidTagRegex.IsMatch(tag))
            return BuildFallback(tag);

        if (!bypassCache && client == null && apiEndpointOverride == null && MemoryCache.TryGetValue(tag, out var cached))
            return cached;

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(5));

            var url = string.IsNullOrWhiteSpace(apiEndpointOverride)
                ? RepoReleasesApiPrefix + tag
                : apiEndpointOverride;

            var http = client ?? DefaultHttp;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!request.Headers.UserAgent.Any())
                request.Headers.UserAgent.ParseAdd($"Clonedivers/{AppInfo.Version}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return BuildFallback(tag);

            var raw = await ReadBoundedStringAsync(response.Content, 256 * 1024, budget.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return BuildFallback(tag);

            var body = doc.RootElement.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.String
                ? bodyEl.GetString()
                : null;

            var releaseUrl = RepoReleasesWebPrefix + tag;
            if (doc.RootElement.TryGetProperty("html_url", out var htmlEl) &&
                htmlEl.ValueKind == JsonValueKind.String &&
                htmlEl.GetString() is { } candidateUrl &&
                candidateUrl.StartsWith("https://github.com/owendavidgoode/clonedivers/releases/", StringComparison.Ordinal))
            {
                releaseUrl = candidateUrl;
            }

            var bullets = ExtractBullets(body, minBullets: 3, maxBullets: 5);
            if (bullets.Count == 0)
                return BuildFallback(tag);

            var result = new WhatsNewResult(tag, bullets, IsFallback: false, releaseUrl);
            if (client == null && apiEndpointOverride == null)
                MemoryCache[tag] = result;
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return BuildFallback(tag);
        }
        catch
        {
            return BuildFallback(tag);
        }
    }

    public static IReadOnlyList<string> ExtractBullets(string? releaseBody, int minBullets = 3, int maxBullets = 5)
    {
        if (string.IsNullOrWhiteSpace(releaseBody))
            return [];

        minBullets = Math.Clamp(minBullets, 1, 5);
        maxBullets = Math.Clamp(maxBullets, minBullets, 8);

        // Remove HTML comments
        var text = Regex.Replace(releaseBody, @"<!--[\s\S]*?-->", "");

        var bullets = new List<string>();
        var proseFallbacks = new List<string>();
        bool inCodeFence = false;
        bool inSkippedSection = false;

        foreach (var rawLine in text.Split(['\r', '\n']))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }
            if (inCodeFence || string.IsNullOrWhiteSpace(line))
                continue;

            // Markdown headings
            if (line.StartsWith('#'))
            {
                inSkippedSection = TechnicalHeadingRegex.IsMatch(line);
                continue;
            }
            if (inSkippedSection)
                continue;

            // Skip markdown tables, horizontal rules, and GitHub callout tags
            if (line.StartsWith('|') || line.EndsWith('|') ||
                Regex.IsMatch(line, @"^[-*_]{3,}$") ||
                Regex.IsMatch(line, @"^>\s*\[!(?:NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]", RegexOptions.IgnoreCase))
            {
                continue;
            }

            if (line.StartsWith('>'))
                continue;

            var bulletMatch = Regex.Match(line, @"^(?:[-*•+]|\d+[.)])\s+(.+)$");
            if (bulletMatch.Success)
            {
                var cleaned = CleanMarkdownLine(bulletMatch.Groups[1].Value);
                if (IsUsableBullet(cleaned))
                    AddUnique(bullets, cleaned);
            }
            else
            {
                var cleanedProse = CleanMarkdownLine(line);
                if (IsUsableBullet(cleanedProse))
                    AddUnique(proseFallbacks, cleanedProse);
            }

            if (bullets.Count >= maxBullets)
                break;
        }

        if (bullets.Count < minBullets)
        {
            foreach (var prose in proseFallbacks)
            {
                if (bullets.Count >= maxBullets) break;
                AddUnique(bullets, prose);
            }
        }

        if (bullets.Count > 0 && bullets.Count < minBullets)
        {
            foreach (var fallback in DefaultFallbackLines)
            {
                if (bullets.Count >= minBullets) break;
                AddUnique(bullets, fallback);
            }
        }

        return bullets.Take(maxBullets).ToList();
    }

    public static WhatsNewResult BuildFallback(string? versionOrTag = null)
    {
        var tag = NormalizeTag(versionOrTag);
        return new WhatsNewResult(
            Tag: tag,
            Bullets: DefaultFallbackLines,
            IsFallback: true,
            ReleaseUrl: RepoReleasesWebPrefix + tag);
    }

    static readonly IReadOnlyList<string> DefaultFallbackLines =
    [
        "Smart 5-tier performance advisor with automatic low-VRAM protection.",
        "One-click Verify & Repair plus automatic cache and backup cleanup.",
        "Squad Match Code and plain-English crash diagnostics for co-op sessions."
    ];

    static string CleanMarkdownLine(string input)
    {
        var s = input;
        // Convert [label](url) -> label
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1");
        // Remove bare URLs
        s = Regex.Replace(s, @"https?://\S+", "");
        // Remove bold/italic/code wrappers
        s = s.Replace("**", "", StringComparison.Ordinal)
             .Replace("__", "", StringComparison.Ordinal)
             .Replace("`", "", StringComparison.Ordinal);
        s = Regex.Replace(s, @"(?<!\w)\*([^*]+)\*(?!\w)", "$1");
        // Collapse whitespace
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = s.Trim('-', '–', '—', ':', ';', ' ');

        if (s.Length > 115)
        {
            var cut = s.LastIndexOf(' ', 112);
            s = (cut > 50 ? s[..cut] : s[..112]).TrimEnd(',', ';', ':', '-', ' ') + "…";
        }
        return s;
    }

    static bool IsUsableBullet(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 12) return false;
        if (NoiseBulletRegex.IsMatch(line)) return false;
        return true;
    }

    static void AddUnique(List<string> target, string candidate)
    {
        if (!target.Any(existing => string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)))
            target.Add(candidate);
    }

    static async Task<string> ReadBoundedStringAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            var take = Math.Min(read, maxBytes - (int)memory.Length);
            if (take > 0) memory.Write(buffer, 0, take);
            if (memory.Length >= maxBytes) break;
        }
        return System.Text.Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }
}
