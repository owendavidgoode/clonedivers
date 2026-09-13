using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

public static class SupportHistory
{
    public sealed record Entry(DateTimeOffset Utc, string Event, string? Pack, string? Profile, string ErrorCategory);
    static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers", "operation-history.json");
    public static IReadOnlyList<Entry> Read(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            FileSafety.RejectLink(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 128 * 1024) return Array.Empty<Entry>();
            var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new();
            return entries.Where(e => e is not null && e.Event is "installed" or "verified" or "error")
                .TakeLast(20).Select(e => e with { Pack = Token(e.Pack), Profile = Token(e.Profile), ErrorCategory = SafeCategory(e.ErrorCategory) }).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { return Array.Empty<Entry>(); }
    }
    public static void Record(string kind, string? pack, string? profile, string? error = null, string? path = null)
    {
        if (kind is not ("installed" or "verified" or "error")) return;
        path ??= DefaultPath;
        try
        {
            var entries = Read(path).Append(new Entry(DateTimeOffset.UtcNow, kind, Token(pack), Token(profile), Diagnostics.ErrorCategory(error))).TakeLast(20).ToArray();
            FileSafety.RejectLink(path); FileSafety.RejectLink(path + ".tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(entries));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { /* Support history is not required to apply a pack. */ }
    }
    static string? Token(string? value) => value is not null && Regex.IsMatch(value, "\\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}\\z") ? value : null;
    static string SafeCategory(string category) => category is "none reported" or "GameGuard 114 reported" or "disk space" or "file integrity" or "download or network" or "file access" ? category : "reported (message omitted for privacy)";
}
