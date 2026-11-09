using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clonedivers;

internal static partial class HellForgeSetup
{
    [DllImport("kernel32.dll")]
    static extern uint GetOEMCP();
    // With CreateNoWindow the pinned .NET CLI uses Windows' OEM code page,
    // even when the invoking terminal uses UTF-8. Half-block bytes must survive.
    internal static Encoding TerminalEncoding => CodePagesEncodingProvider.Instance.GetEncoding((int)GetOEMCP()) ?? Encoding.UTF8;
    // The official unpacker already creates the layout. create-install refuses an
    // existing marker, so use it only for payloads which have not created one.
    internal static async Task CreateLayoutAsync(string root, CancellationToken ct,
        Func<IReadOnlyList<string>, CancellationToken, Task<JsonElement>>? run = null)
    {
        if (File.Exists(Path.Combine(root, "hellforge-install.json"))) return;
        HellForgeGameplayCatalog.RejectLinks(root);
        if (run != null) await run(["create-install", "--root", root], ct);
        else await RunSetupCommandAsync(root, ["create-install", "--root", root], null, ct);
    }

    internal static async Task PrepareGameAsync(string root, IProgress<ForgeSetupProgress>? progress, CancellationToken ct,
        HellForgeClient? client = null, Func<IReadOnlyList<string>, IProgress<ForgeSetupProgress>?, CancellationToken, Task<JsonElement>>? run = null,
        Action<string>? spaceCheck = null, string? steamRoot = null)
    {
        root = HellForgeCommands.ValidateRoot(root);
        client ??= new HellForgeClient(root);
        run ??= (args, report, token) => RunSetupCommandAsync(root, args, report, token);
        using var operation = client.LockPackChange();
        client.RequireCliQuiescent();
        async Task<HellForgeStatus> Idle()
        {
            ct.ThrowIfCancellationRequested();
            var status = await client.StatusAsync(ct);
            if (status.Running || Game.IsRunning()) throw new InvalidOperationException("End the game session before continuing setup.");
            if (!SupportsVersion(status.Version) || status.Engine != HellForgeRelease.Engine || status.ModSdk != HellForgeRelease.Sdk || status.Build != HellForgePack.SupportedBuild)
                throw new InvalidDataException("Game setup requires the pinned HellForge, engine, SDK and game build.");
            return status;
        }
        var before = await Idle();
        if (!before.RunnerReady) throw new InvalidDataException("Forge's session runner is missing. Use the official setup fallback to repair the application.");
        if (!before.GameCopyReady)
        {
            var game = FileSafety.Child(root, "game");
            RejectGameLinks(game, ct);
            (spaceCheck ?? RequireGameSpace)(game);
            await Idle();
            string? source = null;
            steamRoot ??= GameLocator.SteamPath();
            if (steamRoot != null)
            {
                progress?.Report(new("Checking Steam for the supported game build…", CanCancel: true));
                try
                {
                    var manifests = await run(["discover", "--steam-root", steamRoot], null, ct);
                    foreach (var manifest in manifests.EnumerateArray().Take(16))
                    {
                        var path = manifest.GetString();
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        var inspected = await run(["inspect", "--manifest", path], null, ct);
                        if (inspected.GetProperty("pin").GetProperty("buildId").GetString() == HellForgePack.SupportedBuild) { source = path; break; }
                    }
                }
                catch (HellForgeCommandOutcomeUnknownException) { throw; }
                catch (Exception e) when (e is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
                { progress?.Report(new("Steam's local copy could not be reused. Getting the supported build through Steam sign-in instead.", CanCancel: true)); }
            }
            await Idle();
            progress?.Report(new(source == null ? "Getting the supported game build from Steam. Approve the QR sign-in with Steam Guard on your phone." : "Copying the supported build from Steam…", CanCancel: true));
            RejectGameLinks(game, ct);
            var result = await run(source == null ? ["steam-download", "--out", game, "--sign-in", "qr"] : ["install-game", "--manifest", source, "--destination", game], progress, ct);
            if (!result.TryGetProperty("buildId", out var build) || build.GetString() != HellForgePack.SupportedBuild ||
                !result.TryGetProperty("destination", out var destination) ||
                !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination.GetString()!)), game, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Steam download acknowledged a different build or destination. Retry setup to verify the saved files.");
            if (!(await Idle()).GameCopyReady) throw new InvalidDataException("The game copy is incomplete. Continue to resume and verify it, or use official setup.");
        }
        // play installs/verifies the network block itself, before any game runs.
        await Idle();
        await run(["settings", "--sandbox", "yes"], null, ct);
        var after = await Idle();
        if (after.CompatibilityProblem != null) throw new InvalidDataException(after.CompatibilityProblem);
        progress?.Report(new("Game copy ready. Windows may ask for network approval on your first launch."));
    }

    internal static void RejectGameLinks(string game, CancellationToken ct)
    {
        HellForgeGameplayCatalog.RejectLinks(game);
        if (!Directory.Exists(game)) return;
        // Inspect every entry before descending: the CLI opens even .partial files
        // with FileMode.Create, and a dangling link must not be mistaken for absence.
        var pending = new Stack<DirectoryInfo>(); pending.Push(new(game));
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = entry.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Forge game setup refuses a linked file or folder: {entry.FullName}. Remove the link from the game folder, then continue.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(new(entry.FullName));
            }
        }
    }

    internal static void RequireGameSpace(string game)
    {
        // Completed files count towards the 50 GB budget on a resumed download.
        // The official downloader additionally checks its exact chunk plan.
        long saved = 0;
        if (Directory.Exists(game))
            foreach (var path in Directory.EnumerateFiles(game, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                saved = Math.Min(50L << 30, saved + new FileInfo(path).Length);
        var drive = new DriveInfo(Path.GetPathRoot(game)!);
        CheckGameSpace(drive.AvailableFreeSpace, saved);
    }
    internal static void CheckGameSpace(long free, long saved)
    {
        var needed = Math.Max(2L << 30, (50L << 30) - Math.Clamp(saved, 0, 50L << 30));
        if (free < needed) throw new IOException($"Free {Pack.FormatBytes(needed - free)} more on the Forge drive, then continue. Downloaded files are kept for resume.");
    }

    internal static ProcessStartInfo SetupStartInfo(string root, IReadOnlyList<string> args)
    {
        var exe = Path.Combine(root, "app", "cli", "HellForge.Cli.exe");
        HellForgeGameplayCatalog.RejectLinks(exe);
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = TerminalEncoding };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
    internal static TimeSpan SetupCommandTimeout(string command) =>
        command is "steam-download" or "install-game" ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(2);
    static Task<JsonElement> RunSetupCommandAsync(string root, IReadOnlyList<string> args, IProgress<ForgeSetupProgress>? progress, CancellationToken ct) =>
        RunSetupProcessAsync(args[0], SetupStartInfo(root, args), progress, ct);
    internal static Task<JsonElement> RunSetupProcessAsync(string command, ProcessStartInfo start, IProgress<ForgeSetupProgress>? progress, CancellationToken ct) =>
        HellForgeCommands.TimedAsync(command, SetupCommandTimeout(command), token => ReadSetupProcessAsync(start, progress, token), ct);

    internal static async Task<JsonElement> ReadSetupProcessAsync(ProcessStartInfo start, IProgress<ForgeSetupProgress>? progress, CancellationToken ct)
    {
        using var process = Process.Start(start) ?? throw new IOException("Forge setup CLI did not start.");
        using var pipes = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var parser = new SteamSetupOutput(progress);
        var output = HellForgeCommands.ReadBoundedOutputAsync(process.StandardOutput, 64 * 1024, false, pipes.Token);
        async Task ReadError()
        {
            var line = new StringBuilder(); var buffer = new char[1024]; int count;
            while ((count = await process.StandardError.ReadAsync(buffer.AsMemory(), pipes.Token)) > 0)
                foreach (var ch in buffer.AsSpan(0, count).ToArray())
                {
                    if (ch == '\n') { parser.Line(line.ToString().TrimEnd('\r')); line.Clear(); }
                    else { if (line.Length >= 4096) throw new InvalidDataException("Forge setup output line is oversized."); line.Append(ch); }
                }
            if (line.Length > 0) parser.Line(line.ToString());
        }
        var error = ReadError();
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Observe(Task task) { try { await task; } catch (Exception e) { failed.TrySetResult(e); } }
        var readOut = Observe(output); var readError = Observe(error);
        try
        {
            var exited = process.WaitForExitAsync(ct);
            if (await Task.WhenAny(exited, failed.Task) == failed.Task) throw await failed.Task;
            await exited; await error;
            if (process.ExitCode != 0) throw new IOException("Forge game setup stopped. " + parser.Failure + " Continue to retry using the saved files, or choose Official setup.");
            using var json = JsonDocument.Parse(await output);
            return json.RootElement.Clone();
        }
        catch { await HellForgeCommands.StopOwnedCliAsync(process); throw; }
        finally { pipes.Cancel(); await Task.WhenAll(readOut, readError); }
    }
}

// The pinned TerminalPrompt emits inverted half-block modules, not the URL.
// Keep QR challenges in memory only. Never put them in diagnostics or logs.
internal sealed class SteamSetupOutput(IProgress<ForgeSetupProgress>? progress)
{
    readonly List<string> rows = [];
    public string Failure { get; private set; } = "Steam did not complete the download.";
    internal void Line(string line)
    {
        const string prefix = "\u001b[97;40m", suffix = "\u001b[0m";
        if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(suffix, StringComparison.Ordinal))
        {
            var row = line[prefix.Length..^suffix.Length];
            if (row.Length is < 29 or > 185 || row.Any(c => c is not ('\u2588' or '\u2580' or '\u2584' or ' ')) || rows.Count >= 93)
                throw new InvalidDataException("Steam QR output is invalid. Use official setup or retry.");
            rows.Add(row); return;
        }
        if (rows.Count > 0)
        {
            var size = rows[0].Length;
            if (size % 2 != 1 || rows.Count != (size + 1) / 2 || rows.Any(r => r.Length != size))
                throw new InvalidDataException("Steam QR output is incomplete. Retry setup.");
            progress?.Report(new("In the Steam mobile app, open Steam Guard, tap the QR scanner and approve this sign-in.", CanCancel: true, QrRows: rows.ToArray()));
            rows.Clear();
        }
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Open the Steam app", StringComparison.Ordinal)) return;
        if (line.Contains('\u001b') || line.Contains("s.team", StringComparison.OrdinalIgnoreCase) || line.Contains("://", StringComparison.Ordinal)) return;
        Failure = line.Length > 600 ? line[..600] : line;
        var match = Regex.Match(line, @"^(?:Copied )?(\d{1,3})%(?=[: ])");
        progress?.Report(match.Success && int.TryParse(match.Groups[1].Value, out var percent) && percent <= 100
            ? new(line, percent, 100, true) : new(line, CanCancel: true));
    }
    internal static Bitmap RenderQr(string[] rows, int maxPixels = 330)
    {
        var size = rows[0].Length;
        var scale = Math.Max(1, maxPixels / size);
        var bitmap = new Bitmap(size * scale, size * scale);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var ch = rows[y / 2][x];
                var light = y % 2 == 0 ? ch is '\u2588' or '\u2580' : ch is '\u2588' or '\u2584';
                if (!light) graphics.FillRectangle(Brushes.Black, x * scale, y * scale, scale, scale);
            }
        return bitmap;
    }
}
