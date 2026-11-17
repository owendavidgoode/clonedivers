using System.Net;
using System.Reflection;

namespace Clonedivers.Tests;

static class ForgeFormRecoveryTests
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(HellForgeForm form, string name) => (T)typeof(HellForgeForm).GetField(name, Members)!.GetValue(form)!;
    static void Set(HellForgeForm form, string name, object? value) => typeof(HellForgeForm).GetField(name, Members)!.SetValue(form, value);
    static Task Invoke(HellForgeForm form, string name, params object[] args) => (Task)typeof(HellForgeForm).GetMethod(name, Members)!.Invoke(form, args)!;
    static void View(HellForgeForm form) => typeof(HellForgeForm).GetMethod("UpdateView", Members)!.Invoke(form, null);
    static string? Diagnostic(HellForgeForm form) => (string?)typeof(HellForgeForm).GetProperty("LauncherDiagnostic", Members)!.GetValue(form);
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct); }
    static HttpMessageHandler ReplaceHandler(HttpMessageHandler next)
    {
        var client = (HttpClient)typeof(Pack).GetField("Http", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var field = typeof(HttpMessageInvoker).GetField("_handler", Members)!;
        var previous = (HttpMessageHandler)field.GetValue(client)!; field.SetValue(client, next); return previous;
    }
    static async Task<bool> Refused(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { return true; }
    }
    internal static async Task Run(string area, Action<bool, string> check)
    {
        var context = SynchronizationContext.Current;
        var game = Game.IsRunningCheck; var capture = HellForgeForm.CaptureBlockCheck;
        SynchronizationContext.SetSynchronizationContext(null);
        Game.IsRunningCheck = () => false; HellForgeForm.CaptureBlockCheck = () => null;
        try { await UpdateTimeout(check); await MissingInstallations(area, check); }
        finally { Game.IsRunningCheck = game; HellForgeForm.CaptureBlockCheck = capture; SynchronizationContext.SetSynchronizationContext(context); }
    }
    static async Task UpdateTimeout(Action<bool, string> check)
    {
        using var form = new HellForgeForm(new HellForgePreferences());
        SynchronizationContext.SetSynchronizationContext(null);
        var requests = new List<string>();
        // Exercise both real metadata deadlines without contacting the network.
        using var handler = new Handler(async (request, ct) => {
            requests.Add(request.RequestUri!.Host);
            await Task.Delay(Timeout.Infinite, ct);
            throw new Exception("The metadata deadline did not cancel its request.");
        });
        var previous = ReplaceHandler(handler);
        var apiTimeout = Pack.ManifestApiTimeout; var fallbackTimeout = Pack.ManifestFallbackTimeout;
        check(apiTimeout == TimeSpan.FromSeconds(10) && fallbackTimeout == TimeSpan.FromSeconds(12), "production metadata deadlines remain 10 and 12 seconds");
        Pack.ManifestApiTimeout = Pack.ManifestFallbackTimeout = TimeSpan.FromMilliseconds(50);
        try
        {
            Field<Label>(form, "message").Text = "No newer Forge launcher is available.";
            var pending = Invoke(form, "CheckLauncherUpdateAsync", true);
            check(Field<Label>(form, "message").Text == "" && Field<bool>(form, "checkingUpdates"),
                "manual update check clears stale up-to-date text while checking");
            await pending;
            var message = Field<Label>(form, "message").Text;
            check(requests.SequenceEqual(new[] { "api.github.com", "raw.githubusercontent.com" }) &&
                message.Contains("timed out") && message.Contains("Check updates again") && Field<Label>(form, "detail").Text == message,
                "API and fallback deadlines produce a visible manual update-check retry instruction");
            check(Diagnostic(form)?.Contains("TimeoutException") == true && Diagnostic(form)!.Contains(Pack.ManifestUrl) &&
                !Field<bool>(form, "checkingUpdates") && Field<ToolStripMenuItem>(form, "updates").Enabled,
                "metadata timeout retains its source diagnostic and re-enables Check updates");
            check(!Field<Label>(form, "message").Visible, "manual timeout shown in the detail line is not repeated in the message label");
            Set(form, "failureDetail", "Real CLI failure");
            await Invoke(form, "CheckLauncherUpdateAsync", false);
            check(Diagnostic(form) == "Real CLI failure", "silent startup timeout preserves the existing launcher diagnostic");
            Set(form, "failureDetail", null);
            await Invoke(form, "CheckLauncherUpdateAsync", false);
            check(Diagnostic(form) == null, "silent startup timeout does not create a launcher diagnostic");
            Field<HellForgePreferences>(form, "preferences").InstallRoot = Path.GetTempPath(); // no auto-detection or official CLI
            await Invoke(form, "RefreshAsync");
            check(!Field<Label>(form, "message").Text.Contains("timed out"), "a changed installation headline clears the stale update timeout message");

            using var closing = new HellForgeForm(new HellForgePreferences());
            SynchronizationContext.SetSynchronizationContext(null);
            Field<CancellationTokenSource>(closing, "lifetime").Cancel();
            await Invoke(closing, "CheckLauncherUpdateAsync", true);
            check(Diagnostic(closing) == null && Field<Label>(closing, "message").Text == "",
                "only form-lifetime cancellation silently ends an update check");
        }
        finally { ReplaceHandler(previous); Pack.ManifestApiTimeout = apiTimeout; Pack.ManifestFallbackTimeout = fallbackTimeout; }

        using var fallback = new Handler((request, _) => request.RequestUri!.Host == "api.github.com"
            ? throw new TaskCanceledException("API timeout fixture")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"format\":2}") }));
        previous = ReplaceHandler(fallback);
        try
        {
            using var recovered = new HellForgeForm(new HellForgePreferences());
            SynchronizationContext.SetSynchronizationContext(null);
            await Invoke(recovered, "CheckLauncherUpdateAsync", true);
            check(Diagnostic(recovered) == null && Field<Label>(recovered, "detail").Text == "No newer Forge launcher is available.",
                "an API timeout still accepts successful fallback metadata");
        }
        finally { ReplaceHandler(previous); }
    }
    static async Task MissingInstallations(string area, Action<bool, string> check)
    {
        foreach (var moved in new[] { false, true })
        {
            var root = Path.Combine(area, moved ? "forge-saved-moved" : "forge-saved-deleted");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "hellforge-install.json"), "{\"schemaVersion\":1}");
            var preferences = new HellForgePreferences { InstallRoot = root };
            using var form = new HellForgeForm(preferences);
            SynchronizationContext.SetSynchronizationContext(null);
            if (moved) Directory.Move(root, root + "-new"); else Directory.Delete(root, true);
            Set(form, "lastError", "Previous CLI read failed");
            Set(form, "lastStatus", new HellForgeStatus(root, "fixture", HellForgePack.SupportedBuild, true, true, true, true, true, "observing", null));
            await Invoke(form, "RefreshAsync");
            var primary = Field<Button>(form, "launch");
            check(Field<bool>(form, "installationMissing") && primary.Enabled && primary.Text == "SET UP AGAIN" &&
                Field<Label>(form, "detail").Text.Contains("Locate") && !Field<bool>(form, "retryStatus") &&
                Field<HellForgeStatus?>(form, "lastStatus") == null,
                (moved ? "moved" : "deleted") + " saved installation clears stale running/error state and directly offers setup and Locate");
            check(Field<Button>(form, "locateMissing").Text == "Locate Forge" &&
                !Field<ToolStripMenuItem>(form, "prepare").Enabled && !Field<ToolStripMenuItem>(form, "accountSync").Enabled,
                "missing installation exposes a Locate action while installed-only operations stay disabled");
            await Invoke(form, "RequireUpdateIdleAsync");
            check(true, "a positively missing saved installation permits launcher replacement without querying its CLI");
            Game.IsRunningCheck = () => true;
            check(await Refused(() => Invoke(form, "RequireUpdateIdleAsync")), "missing-install self-update still refuses a running game");
            View(form);
            check(primary.Text == "VIEW DETAILS" && Field<bool>(form, "detailsAction"),
                "a running game keeps its details action ahead of missing-install setup");
            Game.IsRunningCheck = () => false; HellForgeForm.CaptureBlockCheck = () => "Fixture capture still running";
            check(await Refused(() => Invoke(form, "RequireUpdateIdleAsync")), "missing-install self-update still refuses account capture/cleanup");
            HellForgeForm.CaptureBlockCheck = () => null;
            Set(form, "launcherRelease", new AppRelease { Version = "999.0.0" }); View(form);
            check(primary.Text == "UPDATE LAUNCHER", "launcher update is offered even when the saved installation has disappeared");
            Set(form, "launcherRelease", null);
            // An empty folder offers setup; a marker-only folder with missing programs offers repair + Locate without clearing syncRequested.
            if (moved) Directory.Move(root + "-new", root); else Directory.CreateDirectory(root);
            var locateButton = Field<Button>(form, "locateMissing");
            locateButton.Parent!.Controls.Remove(locateButton);
            using var detachedLocate = locateButton;
            Set(form, "syncRequested", true);
            await Invoke(form, "RefreshAsync");
            if (moved)
            {
                check(!HellForgeForm.InstallationMissing(root) && HellForgeForm.ProgramsMissing(root) &&
                    !Field<bool>(form, "installationMissing") && Field<bool>(form, "syncRequested") &&
                    primary.Text == "RETRY" && locateButton.Visible &&
                    Field<Label>(form, "detail").Text == "Repair HellForge with the official installer, or Locate another folder",
                    "marker-only folder with missing programs offers repair or Locate without offering a fresh setup or dropping syncRequested");
            }
            else
            {
                check(HellForgeForm.InstallationMissing(root) && Field<bool>(form, "installationMissing") &&
                    primary.Text == "SET UP AGAIN" && locateButton.Visible,
                    "a returned empty folder offers setup rather than a Retry loop");
            }
            await Invoke(form, "RequireUpdateIdleAsync");
            check(true, "an incomplete or program-missing installation does not block launcher self-update");
        }
        var present = Path.Combine(area, "forge-transient-failure"); Directory.CreateDirectory(present);
        File.WriteAllText(Path.Combine(present, "hellforge-install.json"), "{\"schemaVersion\":1}");
        Directory.CreateDirectory(Path.Combine(present, "app", "cli"));
        File.WriteAllText(Path.Combine(present, "app", "cli", "HellForge.Cli.exe"), "fixture");
        File.WriteAllText(Path.Combine(present, "HellForge.exe"), "fixture");
        using var retry = new HellForgeForm(new HellForgePreferences { InstallRoot = present });
        SynchronizationContext.SetSynchronizationContext(null);
        Set(retry, "lastError", "HellForge CLI timed out"); View(retry);
        check(!HellForgeForm.InstallationMissing(present) && Field<Button>(retry, "launch").Text == "RETRY" &&
            Field<Label>(retry, "detail").Text.Contains("timed-out"), "a transient CLI timeout keeps Retry rather than treating an existing folder as missing");
        Set(retry, "failureDetail", "Old manual update timeout");
        await Invoke(retry, "RefreshAsync"); // text fixture cannot execute as a CLI
        check(Diagnostic(retry) != "Old manual update timeout" && Diagnostic(retry) == Field<string>(retry, "lastError"),
            "a new refresh failure replaces an older update diagnostic");
        Set(retry, "launcherRelease", new AppRelease { Version = "999.0.0" }); Set(retry, "updateFailed", true);
        Set(retry, "failureDetail", "HTTP 504 from the launcher update");
        await Invoke(retry, "RefreshAsync");
        check(Diagnostic(retry) == "HTTP 504 from the launcher update" && Field<Label>(retry, "detail").Text.StartsWith("GitHub"),
            "a failed update keeps its own diagnostic while Retry update is the primary action");
    }
}
