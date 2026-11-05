using System.Text;
using System.Text.Json;

namespace Clonedivers.ForgeBridge;

/// <summary>A persistent Windows-side CLI proxy so Linux never re-enters another namespace for status/stop.</summary>
internal sealed class BridgeSession(Func<IReadOnlyList<string>, CancellationToken, Task<JsonElement>> execute)
{
    internal const int MaxRequest = 16 * 1024;
    internal const string Build = "25480438";
    string? ownedRun;

    internal async Task ServeAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        try
        {
            while (await ReadLineAsync(input, MaxRequest, ct) is { } line)
            {
                long? id = null;
                object reply;
                try
                {
                    using var request = JsonDocument.Parse(line);
                    id = request.RootElement.GetProperty("id").GetInt64();
                    if (id <= 0) throw new InvalidDataException("Request id must be positive.");
                    var args = request.RootElement.GetProperty("args").EnumerateArray().Select(a => a.GetString()
                        ?? throw new InvalidDataException("Command arguments must be strings.")).ToArray();
                    Validate(args);
                    if (args[0] is not ("status" or "mods" or "profiles" or "settings" or "logs" or "account-status" or "player-status" or "gameplay-status") || args.Length > 1)
                    {
                        var status = await execute(["status"], ct);
                        // Cleanup must remain available when readiness/version checks have changed.
                        if (args[0] != "stop") RequirePinnedIsolation(status);
                        var running = status.GetProperty("session").GetProperty("running").GetBoolean();
                        if (args[0] == "stop")
                        {
                            if (!running) { ownedRun = null; reply = new { id, ok = true, result = new { @event = "already-stopped" } }; await WriteAsync(output, reply, ct); continue; }
                            var current = status.GetProperty("session").GetProperty("lastRun").GetString();
                            if (ownedRun == null || !string.Equals(current, ownedRun, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("This bridge does not own the running session. End it in the launcher that started it.");
                            args = ["stop", "--run", ownedRun, "--wait", "yes"];
                        }
                        else if (running) throw new InvalidOperationException("Finish the running session before changing or launching Forge.");
                    }
                    var result = await execute(args, ct);
                    if (args[0] == "play")
                    {
                        if (result.GetProperty("event").GetString() != "started" || result.GetProperty("run").GetString() is not { Length: > 0 } run)
                            throw new InvalidDataException("Official CLI did not acknowledge a session with a run folder.");
                        ownedRun = run;
                    }
                    if (args[0] == "stop") ownedRun = null;
                    reply = new { id, ok = true, result };
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    reply = new { id, ok = false, error = error.Message };
                }
                await WriteAsync(output, reply, ct);
            }
        }
        finally
        {
            // EOF is a crashed/closed native frontend. Ask the official CLI to end only our session.
            // Never kill Wine/game/process trees: the official runner owns all cleanup.
            if (ownedRun != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var status = await execute(["status"], cleanup.Token);
                var session = status.GetProperty("session");
                if (session.GetProperty("running").GetBoolean() &&
                    string.Equals(session.GetProperty("lastRun").GetString(), ownedRun, StringComparison.OrdinalIgnoreCase))
                    await execute(["stop", "--run", ownedRun, "--wait", "yes"], cleanup.Token);
            }
        }
    }

    static async Task WriteAsync(TextWriter output, object reply, CancellationToken ct)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(), ct);
        await output.FlushAsync(ct);
    }

    internal static async Task<string?> ReadLineAsync(TextReader reader, int limit, CancellationToken ct)
    {
        var line = new StringBuilder();
        var one = new char[1];
        while (await reader.ReadAsync(one.AsMemory(), ct) != 0)
        {
            if (one[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= limit) throw new InvalidDataException("Bridge input exceeds the request limit.");
            line.Append(one[0]);
        }
        return line.Length == 0 ? null : line.ToString();
    }

    internal static void RequirePinnedIsolation(JsonElement status)
    {
        if (status.GetProperty("gameBuild").GetString() != Build || status.GetProperty("engine").GetString() != "0.1.41" ||
            status.GetProperty("modSdk").GetInt32() != 15 || status.GetProperty("hellforge").GetString()?.StartsWith("0.3.0-beta", StringComparison.Ordinal) != true)
            throw new InvalidDataException("This Forge version is outside the pinned Clonedivers trial. No engine upgrade is applied.");
        if (status.GetProperty("networkBlock").GetString() != "linux-netns" ||
            !status.GetProperty("ready").GetProperty("linuxNamespace").GetBoolean() ||
            !status.GetProperty("settings").GetProperty("sandbox").GetBoolean())
            throw new InvalidOperationException("Official Linux namespace and sandbox checks must pass; no isolation setting will be changed.");
    }

    internal static void Validate(string[] args)
    {
        if (args.Length == 0 || args.Length > 17 || args.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 4096 || a.Contains('\0') || a.Contains('\n') || a.Contains('\r')))
            throw new ArgumentException("Invalid bridge command.");
        if (args.Length == 1 && args[0] is "status" or "mods" or "profiles" or "settings" or "logs" or "stop" or "account-status" or "account-import" or "player-status" or "prepare-regular" or "recover-pack" or "open-manager" or "gameplay-status" or "recover-gameplay") return;
        if (args.SequenceEqual(new[] { "play", "--profile", "default" })) return;
        var allowed = args[0] switch {
            "mod-switch" => new[] { "--id", "--on" },
            "mod-order" => ["--ids"],
            "mod-pack" => ["--id", "--option", "--on", "--variant", "--all"],
            "mod-add" => ["--file"],
            "prepare-pack" => ["--file"],
            "install-gameplay" => ["--file"],
            "select-mode" => ["--mode", "--delta", "--texture", "--droids", "--covenant"],
            "settings" => ["--profile", "--local-playfab", "--multiplayer", "--war"],
            _ => throw new ArgumentException("Unsupported bridge command.")
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
            if (i + 1 == args.Length || !allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown, duplicate or incomplete command option.");
        if (values.Count == 0) throw new ArgumentException("Missing command options.");
        foreach (var key in new[] { "--on", "--all" })
            if (values.TryGetValue(key, out var on) && on is not ("yes" or "no")) throw new ArgumentException("Expected yes or no.");
        if (args[0] == "mod-switch" && (!values.ContainsKey("--id") || !values.ContainsKey("--on"))) throw new ArgumentException("Missing mod switch options.");
        if (args[0] == "mod-pack" && (!values.ContainsKey("--id") || (!values.ContainsKey("--all") && !values.ContainsKey("--option")))) throw new ArgumentException("Missing pack options.");
        if (args[0] is "mod-add" or "prepare-pack" or "install-gameplay" && (!values.TryGetValue("--file", out var file) || !file.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase) || file.Split('\\').Contains("..")))
            throw new ArgumentException("Import the author artifact from the prefix C: drive.");
        if (args[0] == "select-mode" && (values.Count != 5 || values.GetValueOrDefault("--mode") is not ("regular" or "clonedivers" or "empire") ||
            values.GetValueOrDefault("--texture") is not ("full" or "lighter") || new[] { "--delta", "--droids", "--covenant" }.Any(k => values.GetValueOrDefault(k) is not ("yes" or "no"))))
            throw new ArgumentException("Choose Regular, Clones or Empire with known texture/options values.");
        if (args[0] == "settings")
            foreach (var (key, value) in values)
                if (key == "--profile" && value != "default" || key is "--local-playfab" or "--multiplayer" && value != "yes" || key == "--war" && value != "live")
                    throw new ArgumentException("Player settings use the default account, local backend, Steam multiplayer and live war.");
    }
}
