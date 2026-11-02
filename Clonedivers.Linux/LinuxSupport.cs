using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Clonedivers;

public static class LinuxSupport
{
    public sealed record Observation(DateTimeOffset Utc, string Event, string Detail);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Redact(string value, string? home = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) value = value.Replace(home, "$HOME", StringComparison.Ordinal);
        value = Regex.Replace(value, @"(?im)^.*(?:authorization|bearer\s|password|access[_-]?token|refresh[_-]?token|api[_-]?key|invitation).*$", "[sensitive line omitted]");
        value = Regex.Replace(value, @"(https?://[^\s?""<>]+)\?[^\s""<>]*", "$1?[query omitted]", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\b\d{17}\b", "[Steam ID]");
        value = Regex.Replace(value, @"/home/[^/\s""']+", "/home/[user]");
        value = Regex.Replace(value, @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[email]", RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"[\p{C}-[\r\n\t]]", "");
    }

    internal static string Tail(string path, int limit = 256 * 1024)
    {
        try
        {
            FileSafety.RejectLink(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(file.Length, limit);
            var truncated = file.Length > length;
            if (truncated) file.Seek(-length, SeekOrigin.End);
            var bytes = new byte[length]; var read = file.ReadAtLeast(bytes, length, throwOnEndOfStream: false);
            return (truncated ? "[Only the end of this file is included.]\n" : "") + Encoding.UTF8.GetString(bytes, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return "Unavailable: " + ex.GetType().Name; }
    }

    public static void Record(string kind, string detail, string? configDirectory = null)
    {
        try
        {
            var directory = configDirectory ?? PlatformPaths.ConfigDirectory;
            var path = FileSafety.Child(directory, "linux-history.json");
            var safe = Redact(detail);
            var history = ReadHistory(directory).Append(new Observation(DateTimeOffset.UtcNow, kind, safe[..Math.Min(safe.Length, 2000)])).TakeLast(40).ToArray();
            Directory.CreateDirectory(directory);
            WritePrivate(path, JsonSerializer.Serialize(history, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { /* Reporting must not break launching. */ }
    }

    static IReadOnlyList<Observation> ReadHistory(string directory)
    {
        var path = Path.Combine(directory, "linux-history.json");
        try
        {
            FileSafety.RejectLink(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return Array.Empty<Observation>();
            return (JsonSerializer.Deserialize<List<Observation>>(File.ReadAllText(path)) ?? new()).Where(x => x is not null).TakeLast(40).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return Array.Empty<Observation>(); }
    }

    public static object Selection(string? gameDir, Settings settings)
    {
        var receipt = gameDir is null ? null : InstallReceipt.Load(Settings.ReceiptPathFor(gameDir), gameDir);
        return new
        {
            CapturedUtc = DateTimeOffset.UtcNow,
            Launcher = AppInfo.Version,
            GameFound = GameLocator.IsGameDir(gameDir),
            ModState = ModFiles.GetState(gameDir).ToString(),
            GameBuild = SteamAcf.Read(gameDir)?.BuildId,
            Pack = receipt?.PackVersion ?? settings.InstalledPackVersion,
            PackGameBuild = receipt?.GameBuild ?? settings.InstalledGameBuild,
            TextureProfile = receipt?.TextureProfile ?? settings.InstalledTextureProfile,
            Options = (receipt?.Options ?? settings.Options).Where(p => p.Key is "droids" or "covenant" or "commandos" or "aimpoints" or "empire" or "skinny").ToDictionary(p => p.Key, p => p.Value),
            ReceiptMatchesLayout = receipt is not null && gameDir is not null && receipt.MatchesLayout(gameDir),
            Note = "Selection is observed from launcher metadata and file layout, not a fresh content hash check."
        };
    }

    static object SafeSelection(string? gameDir, Settings settings)
    {
        try { return Selection(gameDir, settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { return new { Unavailable = ex.Message }; }
    }

    static JsonNode? RedactJson(JsonNode? node, string? home) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, RedactJson(p.Value, home)))),
        JsonArray array => new JsonArray(array.Select(x => RedactJson(x, home)).ToArray()),
        JsonValue val when val.TryGetValue<string>(out var s) => JsonValue.Create(Redact(s, home)),
        _ => node?.DeepClone()
    };

    static object[] Gpus()
    {
        try
        {
            return Directory.EnumerateDirectories("/sys/class/drm", "card*").Where(d => Regex.IsMatch(Path.GetFileName(d), "^card[0-9]+$"))
                .Take(8).Select(d => (object)new
                {
                    Device = Path.GetFileName(d), Vendor = Tail(Path.Combine(d, "device/vendor"), 128).Trim(),
                    PciDevice = Tail(Path.Combine(d, "device/device"), 128).Trim(),
                    Driver = new DirectoryInfo(Path.Combine(d, "device/driver")).ResolveLinkTarget(true)?.Name ?? "unknown"
                }).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<object>(); }
    }

    public static string SaveReport(string? gameDir, Settings settings, string? output = null, string? configDirectory = null, string? home = null)
    {
        var config = configDirectory ?? PlatformPaths.ConfigDirectory;
        output ??= Path.Combine(config, "reports", $"clonedivers-linux-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        output = Path.GetFullPath(output);
        FileSafety.RejectLink(Path.GetDirectoryName(output)!); FileSafety.RejectLink(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
        {
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            void Add(string name, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                if (name.EndsWith(".json", StringComparison.Ordinal))
                {
                    try { writer.Write(RedactJson(JsonNode.Parse(content), home)?.ToJsonString(Json)); }
                    catch (JsonException) { writer.Write("{\"Unavailable\":\"Source metadata was invalid or exceeded the excerpt limit.\"}"); }
                }
                else writer.Write(Redact(content, home));
            }
            Add("report.json", JsonSerializer.Serialize(new
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                Kernel = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                Distribution = Tail("/etc/os-release", 8192),
                Cpu = Tail("/proc/cpuinfo", 8192).Split('\n').FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal)) ?? "unknown",
                Gpus = Gpus(), Selection = SafeSelection(gameDir, settings),
                SteamRoots = LinuxPlatform.SteamRoots(home).Where(Directory.Exists).Select(p => Redact(p, home)).ToArray(),
                Privacy = "Local export only. No raw settings, environment dump, game memory or automatic upload. Credential-like lines are filtered; text redaction is best-effort. Inspect before sharing."
            }, Json));
            Add("launcher-history.json", JsonSerializer.Serialize(ReadHistory(config), Json));
            try
            {
            var session = gameDir is null ? null : LinuxRecording.LatestSession(gameDir);
            if (session is not null)
            {
                foreach (var name in new[] { "selection-at-launch.json", "selection.json", "status.txt", "proton-version.txt" })
                    if (File.Exists(Path.Combine(session, name))) Add("session/" + name, Tail(Path.Combine(session, name), 32768));
                FileSafety.RejectLink(Path.Combine(session, "proton"));
                Add("session/proton.log", Tail(Path.Combine(session, "proton", "steam-553850.log")));
                var mango = Path.Combine(session, "mangohud");
                FileSafety.RejectLink(mango);
                if (Directory.Exists(mango))
                    foreach (var csv in Directory.EnumerateFiles(mango, "*.csv").OrderByDescending(File.GetLastWriteTimeUtc).Take(2).Select((path, i) => (path, i)))
                        Add($"session/mangohud-{csv.i + 1}.csv", Tail(csv.path, 1024 * 1024));
            }
            else
            {
                var protonLog = Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "steam-553850.log");
                Add("proton.log", Tail(protonLog));
                Add("session.txt", "No recorded session is available. Selected Proton version is unknown; use recording setup and record a launch to capture the actual runtime version.");
            }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { Add("collection-note.txt", "Some session files were unavailable: " + ex.Message); }
            Add("README.txt", "Support report saved locally. Log excerpts are size-limited and redacted on a best-effort basis. Review before sharing. Missing metrics or a missing Proton log mean they were unavailable; they are not evidence of a successful capture. Full recordings remain in the game's mods_diagnostics/sessions folder.");
        }
        return output;
    }

    internal static void WritePrivate(string path, string text)
    {
        FileSafety.RejectLink(Path.GetDirectoryName(path)!); FileSafety.RejectLink(path);
        var temporary = FileSafety.Child(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                file.Write(Encoding.UTF8.GetBytes(text));
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
