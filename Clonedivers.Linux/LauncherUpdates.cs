using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

internal sealed record LauncherRelease(string Version)
{
    public string Url => "https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v" + Version;
}

internal sealed record LauncherUpdateStatus(LauncherRelease? Available, bool CheckSucceeded)
{
    public string Message => Available is not null ? $"Launcher v{Available.Version} available (installed v{AppInfo.Version})."
        : CheckSucceeded ? "Launcher is up to date." : "Launcher update check unavailable. Try again later.";
    public string Url => Available?.Url ?? LauncherUpdates.ReleasesPage;
}

internal sealed class LauncherUpdates
{
    internal const string ReleasesPage = "https://github.com/owendavidgoode/clonedivers/releases?q=linux-v";
    internal const string Api = "https://api.github.com/repos/owendavidgoode/clonedivers/releases?per_page=100";
    internal const string CacheName = "linux-launcher-update.json";
    static readonly HttpClient Web = CreateClient();
    readonly HttpClient client;
    readonly string endpoint, directory, architecture;
    readonly Func<DateTimeOffset> now;
    readonly SemaphoreSlim gate = new(1, 1);
    internal sealed record Cache(DateTimeOffset CheckedUtc, string? LatestVersion, bool Succeeded);

    internal LauncherUpdates(HttpClient? client = null, string? endpoint = null, string? directory = null, string? architecture = null, Func<DateTimeOffset>? now = null)
    {
        this.client = client ?? Web; this.endpoint = endpoint ?? Api; this.directory = directory ?? PlatformPaths.ConfigDirectory;
        this.architecture = architecture ?? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }
    static HttpClient CreateClient()
    {
        var web = new HttpClient { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 1024 * 1024 };
        web.DefaultRequestHeaders.UserAgent.ParseAdd("Clonedivers-Linux/" + AppInfo.Version);
        web.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return web;
    }
    internal static bool ValidVersion(string? value) => value is not null && Regex.IsMatch(value, @"^\d+\.\d+\.\d+$") && Version.TryParse(value, out _);
    internal static LauncherRelease? SelectRelease(string json, string architecture)
    {
        using var data = JsonDocument.Parse(json);
        if (data.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid release list.");
        LauncherRelease? latest = null;
        foreach (var release in data.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String ||
                !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            var tag = tagElement.GetString()!;
            if (!tag.StartsWith("linux-v", StringComparison.Ordinal) || !ValidVersion(tag[7..])) continue;
            if (!assets.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() == $"Clonedivers-linux-{architecture}.tar.gz" && a.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "uploaded")) continue;
            var version = tag[7..];
            if (latest is null || Version.Parse(version) > Version.Parse(latest.Version)) latest = new LauncherRelease(version);
        }
        return latest;
    }
    Cache? ReadCache()
    {
        try
        {
            var path = FileSafety.Child(directory, CacheName);
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(path));
            return cache is not null && (cache.LatestVersion is null || ValidVersion(cache.LatestVersion)) ? cache : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return null; }
    }
    static LauncherUpdateStatus Status(Cache? cache) => new(cache?.LatestVersion is { } version && Version.Parse(version) > Version.Parse(AppInfo.Version) ? new LauncherRelease(version) : null, cache?.Succeeded == true);

    internal async Task<LauncherUpdateStatus> CheckAsync(CancellationToken ct = default, bool force = false)
    {
        await gate.WaitAsync(ct);
        try
        {
            var cached = ReadCache(); var age = cached is null ? TimeSpan.MaxValue : now() - cached.CheckedUtc;
            if (!force && age >= TimeSpan.Zero && age < (cached?.Succeeded == true ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(15))) return Status(cached);
            Cache result;
            try
            {
                var json = await client.GetStringAsync(endpoint, ct);
                if (json.Length > 1024 * 1024) throw new InvalidDataException("Release list is too large.");
                var latest = SelectRelease(json, architecture) ?? throw new InvalidDataException("No Linux release available.");
                result = new Cache(now(), latest.Version, true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException or InvalidOperationException)
            { result = new Cache(now(), cached?.LatestVersion, false); }
            try
            {
                Directory.CreateDirectory(directory);
                LinuxSupport.WritePrivate(FileSafety.Child(directory, CacheName), JsonSerializer.Serialize(result));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { /* A notice must not depend on writable settings. */ }
            return Status(result);
        }
        finally { gate.Release(); }
    }
    internal static void OpenRelease(LauncherUpdateStatus status) => Process.Start(new ProcessStartInfo(status.Url) { UseShellExecute = true });
}
