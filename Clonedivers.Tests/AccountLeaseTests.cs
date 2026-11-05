using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Clonedivers.Tests;

static class AccountLeaseTests
{
    internal static void Hold(string install, bool review)
    {
        Validate(Path.GetDirectoryName(Path.GetFullPath(install))!);
        using var lease = new HellForgeAccount(install).Lease(review);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(install)!, "lease.ready"), "ready");
        Thread.Sleep(30000);
    }
    static void Validate(string root)
    {
        if (!root.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(root).StartsWith("clonedivers-account-lease-", StringComparison.Ordinal)) throw new IOException("Invalid account lease fixture root.");
    }
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var review in new[] { false, true })
        {
            var root = Path.Combine(Path.GetTempPath(), "clonedivers-account-lease-" + Guid.NewGuid().ToString("N"));
            var install = Path.Combine(root, "installation"); Directory.CreateDirectory(install);
            var account = new HellForgeAccount(install);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--hellforge-account-lease"); start.ArgumentList.Add(install); start.ArgumentList.Add(review ? "read" : "write");
            using var child = Process.Start(start)!;
            try
            {
                for (var i = 0; i < 500 && !File.Exists(Path.Combine(root, "lease.ready")); i++) { if (child.HasExited) throw new IOException("Lease fixture exited early."); await Task.Delay(10); }
                if (!File.Exists(Path.Combine(root, "lease.ready"))) throw new IOException("Lease fixture did not become ready.");
                async Task Refuses(Func<Task> action, string label)
                {
                    bool refused = false;
                    try { await action(); } catch (IOException error) { refused = error.Message.Contains("Another launcher"); }
                    check(refused, label);
                }
                if (review)
                {
                    using var shared = account.Lease(review: true);
                    check(!(await account.ReadAsync()).Captured && !child.HasExited, "read-only account reviews share the cross-process lease without inferring a capture");
                }
                else
                {
                    await Refuses(() => account.ReadAsync(), "an exclusive account lease blocks a second process's verification before reading profile bookkeeping");
                    if (OperatingSystem.IsWindows()) await Refuses(() => new HellForgeAccount(install.ToUpperInvariant()).ReadAsync(), "Windows case variants share the same exclusive account lease");
                    using var sibling = new HellForgeAccount(Path.Combine(root, "other-installation")).Lease();
                    check(true, "a sibling installation has an independent account lease");
                }
                await Refuses(() => account.ImportAsync(), "an account import cannot overlap another process's " + (review ? "review" : "write"));
                await Refuses(() => account.BindLegacyReceiptAsync(), "legacy receipt migration cannot overlap another process's " + (review ? "review" : "write"));
                if (!review)
                {
                    var commands = new NoCommands();
                    var launch = typeof(HellForgeClient).GetMethod("StartAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    await Refuses(() => (Task)launch.Invoke(new HellForgeClient(install, commands), [CancellationToken.None, "default"])!, "a launch cannot race account import bookkeeping");
                    check(commands.Calls == 0, "account-lease refusal sends no play/settings/profile command");
                }
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } // only the explicitly owned holding fixture
                FileStream? released = null;
                for (var attempt = 0; attempt < 100 && released == null; attempt++)
                {
                    try { released = account.Lease(); } catch (IOException) { await Task.Delay(20); }
                }
                using (released) check(released != null, "process exit releases the lease without deleting or resetting account state");
                Validate(root); Directory.Delete(root, true);
            }
        }
        await Committing(check);
    }
    static async Task Committing(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "clonedivers-account-lease-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "installation"); var folder = Path.Combine(install, "profiles", "default"); var snapshot = Path.Combine(install, "account-snapshot", "default");
        Directory.CreateDirectory(folder); Directory.CreateDirectory(snapshot);
        File.WriteAllText(Path.Combine(folder, "profile.json"), "{\"revision\":0}");
        File.WriteAllText(Path.Combine(snapshot, "snapshot.json"), "{\"progression\":true}");
        File.WriteAllText(Path.Combine(snapshot, "real-inventory.json"), "{\"items\":[]}");
        File.WriteAllText(Path.Combine(snapshot, "progression.json"), "{\"experience\":1000}");
        File.WriteAllText(Path.Combine(snapshot, "seal.json"), "{\"steamAccount\":\"1\",\"capturedUtc\":\"2026-11-01T00:00:00Z\"}");
        // Injected-verifier fixtures only; these are never passed to the actual account backend.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int verifications = 0, imports = 0;
        async Task<string> Backend(IReadOnlyList<string> args, CancellationToken ct)
        {
            if (args[1] == "import-preview") { verifications++; return "{\"categories\":{\"level\":{\"vanilla\":\"1,000 XP\"}}}"; }
            imports++; entered.SetResult(); await finish.Task;
            File.WriteAllText(Path.Combine(folder, "profile.json"), "{\"revision\":1}"); return "fixture committed";
        }
        using var cancellation = new CancellationTokenSource(); Task? committing = null;
        try
        {
            var first = new HellForgeAccount(install, Backend); var second = new HellForgeAccount(install, Backend);
            committing = first.ImportAsync(cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
            var policy = File.ReadAllText(Path.Combine(folder, "account-import.json")); var pending = File.ReadAllText(Path.Combine(folder, "clonedivers-account-import.pending.json"));
            foreach (var action in new Func<Task>[] { () => second.ReadAsync(), () => second.ImportAsync(), () => second.BindLegacyReceiptAsync() })
            {
                bool blocked = false; try { await action(); } catch (IOException) { blocked = true; }
                check(blocked && verifications == 1 && imports == 1, "a canceled but committing import keeps exclusive ownership ahead of any second verifier or writer");
            }
            check(File.ReadAllText(Path.Combine(folder, "account-import.json")) == policy && File.ReadAllText(Path.Combine(folder, "clonedivers-account-import.pending.json")) == pending,
                "refused second clients leave the active policy and pending journal byte-identical");
            finish.SetResult(); await committing;
            check((await second.ReadAsync()).Imported && !File.Exists(Path.Combine(folder, "clonedivers-account-import.pending.json")), "the canceled first caller completes its receipt and releases account ownership after commit");
            await second.ImportAsync(); check(imports == 1, "the next client cannot replay the already imported capture after lease release");
        }
        finally
        {
            finish.TrySetResult(); if (committing != null) { try { await committing; } catch (Exception) { } }
            Validate(root); Directory.Delete(root, true);
        }
    }
    sealed class NoCommands : IHellForgeCommands
    {
        internal int Calls;
        public Task<JsonElement> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default) { Calls++; throw new Exception("No CLI may run in this fixture."); }
    }
}
