using System.Diagnostics;
using System.Reflection;
using Clonedivers;

namespace Clonedivers.ForgeBridge.Tests;

internal static class CliTests
{
    internal static async Task OutputAsync(Action<bool, string> check)
    {
        check(await HellForgeCommands.ReadBoundedOutputAsync(new StringReader("12345678"), 8, false, CancellationToken.None) == "12345678", "output exactly at the bound remains valid");
        check(await HellForgeCommands.ReadBoundedOutputAsync(new StringReader("{\"ok\":1}\r\nignored independent session text"), 8, true, CancellationToken.None) == "{\"ok\":1}",
            "a single acknowledgement stops at its first newline and removes CR");
        check(await HellForgeCommands.ReadBoundedOutputAsync(new StringReader("{\n  \"ok\":1\n}"), 1024, false, CancellationToken.None) == "{\n  \"ok\":1\n}", "ordinary JSON retains all lines");
        foreach (var single in new[] { false, true })
        {
            bool refused = false;
            try { await HellForgeCommands.ReadBoundedOutputAsync(new StringReader(new string('x', 9)), 8, single, CancellationToken.None); } catch (InvalidDataException) { refused = true; }
            check(refused, "oversized output is refused during reading: " + (single ? "acknowledgement" : "ordinary JSON"));
        }
        foreach (var mode in new[] { "stdout-json", "stdout-line", "stderr" })
        {
            var root = Path.Combine(Path.GetTempPath(), "clonedivers-cli-flood-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var probe = Path.Combine(root, "pid.txt"); Process? child = null; Process? sentinel = null;
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--cli-flood-fixture"); start.ArgumentList.Add(probe); start.ArgumentList.Add(mode == "stderr" ? "stderr" : "stdout");
                var timer = Stopwatch.StartNew();
                bool refused = false;
                try { await HellForgeCommands.ReadJsonProcessAsync(start, mode == "stdout-line", limit.Token); } catch (InvalidDataException error) { refused = error.Message.Contains("limit"); }
                var pid = int.Parse(await File.ReadAllTextAsync(probe));
                bool exited;
                try { child = Process.GetProcessById(pid); exited = child.HasExited; } catch (ArgumentException) { exited = true; }
                check(refused && exited && timer.Elapsed < TimeSpan.FromSeconds(10), "a live pipe flood aborts before deadline and confirms only its owned CLI exited: " + mode);
                sentinel = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(probe + ".sentinel")));
                check(!sentinel.HasExited, "independent session-like descendants keep running after oversized CLI output: " + mode);
            }
            finally
            {
                if (child != null) { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } child.Dispose(); }
                if (sentinel == null && File.Exists(probe + ".sentinel"))
                {
                    try { sentinel = Process.GetProcessById(int.Parse(File.ReadAllText(probe + ".sentinel"))); } catch (ArgumentException) { }
                }
                if (sentinel != null) { if (!sentinel.HasExited) { sentinel.Kill(); await sentinel.WaitForExitAsync(); } sentinel.Dispose(); } // owned fixture only
                var full = Path.GetFullPath(root);
                if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(full).StartsWith("clonedivers-cli-flood-", StringComparison.Ordinal)) throw new IOException("Invalid fixture cleanup root.");
                Directory.Delete(full, true);
            }
        }
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        check(HellForgeCommands.ReadAfterUncertain(["status"]) && HellForgeCommands.ReadAfterUncertain(["stop", "--wait", "yes"]) &&
            !new[] { new[] { "play" }, new[] { "mod-add", "--file", "fixture" }, new[] { "settings" }, new[] { "profiles", "--create", "fixture" } }.Any(HellForgeCommands.ReadAfterUncertain),
            "uncertain CLI outcomes retain Stop/diagnostics while excluding launch, import, settings and profile mutations");
        var root = Path.Combine(Path.GetTempPath(), "clonedivers-cli-exit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var probe = Path.Combine(root, "pid.txt");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--cli-wait-fixture"); start.ArgumentList.Add(probe);
        Task? request = null; Process? child = null;
        try
        {
            request = HellForgeCommands.ReadJsonProcessAsync(start, false, cancel.Token);
            int pid;
            while (true)
            {
                cancel.Token.ThrowIfCancellationRequested();
                string text = "";
                try { if (File.Exists(probe)) text = await File.ReadAllTextAsync(probe, cancel.Token); } catch (IOException) { }
                if (int.TryParse(text, out pid)) break;
                if (request.IsCompleted) await request;
                await Task.Delay(10, cancel.Token);
            }
            child = Process.GetProcessById(pid);
            cancel.Cancel(); bool cancelled = false;
            try { await request; } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && child.HasExited, "a cancelled CLI is confirmed exited before the import caller can start recovery");
        }
        finally
        {
            cancel.Cancel();
            if (request != null) { try { await request; } catch (OperationCanceledException) { } }
            child?.Dispose();
            var full = Path.GetFullPath(root);
            if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("clonedivers-cli-exit-", StringComparison.Ordinal)) throw new IOException("Fixture cleanup escaped its temporary root.");
            Directory.Delete(full, true);
        }
    }
}
