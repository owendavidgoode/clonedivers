using System.Text.Json;

namespace Clonedivers.Tests;

static class SessionLogTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "sessions", "history.json");
        var selection = new SessionLog.Context("2026.09.23-preview", "25327279", "full", "Clonedivers", true, true, false);
        var now = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        var events = new List<(string Kind, double? Seconds)>();
        var observer = new SessionObserver((kind, seconds) => events.Add((kind, seconds)));
        observer.Request(now);
        observer.Observe(false, now.AddSeconds(5));
        observer.Observe(true, now.AddSeconds(10));
        observer.Observe(null, now.AddSeconds(20));
        observer.Observe(null, now.AddSeconds(25));
        observer.Observe(true, now.AddSeconds(30));
        observer.Observe(false, now.AddSeconds(40));
        check(events.Select(e => e.Kind).SequenceEqual(new[] {"launch-requested", "game-observed-running", "process-check-unavailable", "game-exit-observed"}), "unknown process observations do not report false exits or repeat errors");
        check(events.Last().Seconds == 30, "observed session duration is recorded without claiming crash cause");
        observer.Request(now.AddMinutes(1)); observer.Observe(false, now.AddMinutes(3)); observer.Observe(false, now.AddMinutes(4));
        check(events.Count(e => e.Kind == "launch-not-observed") == 1, "unobserved launch is reported once after timeout");
        observer.Request(now.AddMinutes(5)); observer.HandoffFailed(); observer.Observe(false, now.AddMinutes(7));
        check(events.Count(e => e.Kind == "launch-not-observed") == 1, "failed Steam handoff clears pending observation");
        for (int i = 0; i < SessionLog.Limit + 3; i++) SessionLog.Record(new(now.AddSeconds(i), "launcher-opened", selection), path);
        check(SessionLog.Read(path).Count == SessionLog.Limit && SessionLog.Read(path)[0].Utc == now.AddSeconds(3), "session history rotates at bounded entry count");
        var unsafeContext = new SessionLog.Context(@"C:\Users\SecretName", "https://secret.invalid/token", "SecretName", "SecretName", null, null, null);
        File.WriteAllText(path, JsonSerializer.Serialize(new[] {new SessionLog.Entry(now, "launch-requested", unsafeContext, -99), new SessionLog.Entry(now, "secret event", selection)}));
        var report = SessionLog.Format(SessionLog.Read(path));
        check(!report.Contains("SecretName") && !report.Contains("secret.invalid") && !report.Contains("secret event") && !report.Contains("-99"), "tampered session fields are allowlisted on read and export");
        File.WriteAllText(path, "broken");
        check(SessionLog.Read(path).Count == 0, "corrupt session log is nonfatal");
        SessionLog.Record(new(now, "launcher-opened", selection), path);
        check(SessionLog.Read(path).Count == 1, "recording recovers from corrupt log");
        using (var stream = File.Create(path)) stream.SetLength(512 * 1024 + 1);
        check(SessionLog.Read(path).Count == 0, "oversized logs are ignored before parsing");
        SessionLog.Record(new(now, "launcher-opened", selection), root); // directory is not a writable log file
        check(Directory.Exists(root), "logging I/O failure does not block launcher operation");
    }
}
