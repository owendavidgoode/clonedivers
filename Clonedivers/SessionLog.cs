using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

/// <summary>Bounded, local, allowlisted observations. Never reads game memory or uploads data.</summary>
public static class SessionLog
{
    public sealed record Context(string? Pack, string? GameBuild, string? Profile, string? Mode,
        bool? Delta, bool? Droids, bool? AimPoints);
    public sealed record Entry(DateTimeOffset Utc, string Event, Context Selection, double? Seconds = null);
    public const int Limit = 250;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clonedivers", "session-history.json");
    static readonly object Gate = new();
    static readonly HashSet<string> Events = new(StringComparer.Ordinal) {
        "launcher-opened", "launcher-closed", "previous-monitor-interrupted", "launch-requested", "steam-handoff-failed",
        "game-observed-running", "game-exit-observed", "launch-not-observed", "process-check-unavailable"
    };
    static string? Version(string? value) => value is not null && Regex.IsMatch(value, @"\A[0-9][0-9A-Za-z._-]{0,63}\z") ? value : null;
    static Context Safe(Context? c) => new(Version(c?.Pack), Version(c?.GameBuild),
        c?.Profile is "full" or "lighter" or "reduced1024" or "reduced512" ? c.Profile : null,
        c?.Mode is "Helldivers" or "Clonedivers" or "Commandodivers" ? c.Mode : null, c?.Delta, c?.Droids, c?.AimPoints);
    static Entry Clean(Entry e) => e with { Selection = Safe(e.Selection), Seconds = e.Seconds is double s && double.IsFinite(s) && s >= 0 && s <= 31536000 ? Math.Round(s, 1) : null };

    public static IReadOnlyList<Entry> Read(string? path = null)
    {
        lock (Gate)
        {
            try
            {
                path ??= DefaultPath;
                FileSafety.RejectLink(Path.GetDirectoryName(Path.GetFullPath(path))!);
                FileSafety.RejectLink(path);
                if (!File.Exists(path) || new FileInfo(path).Length > 512 * 1024) return Array.Empty<Entry>();
                return (JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new())
                    .Where(e => e is not null && Events.Contains(e.Event)).TakeLast(Limit).Select(Clean).ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { return Array.Empty<Entry>(); }
        }
    }

    public static void Record(Entry entry, string? path = null)
    {
        if (!Events.Contains(entry.Event)) return;
        lock (Gate)
        {
            try
            {
                path ??= DefaultPath;
                var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
                FileSafety.RejectLink(directory); FileSafety.RejectLink(path); FileSafety.RejectLink(path + ".tmp");
                var entries = Read(path).Append(Clean(entry)).TakeLast(Limit).ToArray();
                Directory.CreateDirectory(directory);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(entries));
                File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { /* Diagnostics must never prevent launch or installation. */ }
        }
    }

    public static string Format(IReadOnlyList<Entry> entries)
    {
        var b = new StringBuilder("Local session observations (up to 250; only while launcher is open):\n");
        foreach (var raw in entries.Where(e => e is not null && Events.Contains(e.Event)).TakeLast(Limit))
        {
            var e = Clean(raw); var c = e.Selection;
            static string Flag(bool? value) => value is bool on ? on ? "on" : "off" : "unknown";
            b.AppendLine(FormattableString.Invariant($"  {e.Utc:O} | {e.Event} | pack={c.Pack ?? "unknown"} build={c.GameBuild ?? "unknown"} profile={c.Profile ?? "unknown"} mode={c.Mode ?? "unknown"} delta={Flag(c.Delta)} droids={Flag(c.Droids)} aimpoints={Flag(c.AimPoints)} elapsedSeconds={e.Seconds?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}"));
        }
        if (entries.Count == 0) b.AppendLine("  No recorded sessions.");
        b.AppendLine("Game exit cause: not determined by process observation. Closing the launcher stops observation; very brief launches may be missed.");
        return b.ToString();
    }
}

/// <summary>Pure transition tracker: unknown process state is never treated as an exit.</summary>
public sealed class SessionObserver(Action<string, double?> emit)
{
    DateTimeOffset? requested, runningSince;
    bool unavailable;
    public void Request(DateTimeOffset now) { requested = now; emit("launch-requested", null); }
    public void HandoffFailed() { requested = null; emit("steam-handoff-failed", null); }
    public void Observe(bool? running, DateTimeOffset now)
    {
        if (running is null)
        {
            if (!unavailable) emit("process-check-unavailable", null);
            unavailable = true;
            return;
        }
        unavailable = false;
        if (running.Value && runningSince is null)
        {
            runningSince = now;
            emit("game-observed-running", requested is {} start ? Math.Max(0, (now-start).TotalSeconds) : null);
            requested = null;
        }
        else if (!running.Value && runningSince is {} start)
        {
            emit("game-exit-observed", Math.Max(0, (now-start).TotalSeconds));
            runningSince = null;
        }
        if (!running.Value && requested is {} launch && now-launch >= TimeSpan.FromSeconds(90))
        {
            emit("launch-not-observed", Math.Max(0, (now-launch).TotalSeconds));
            requested = null;
        }
    }
}
