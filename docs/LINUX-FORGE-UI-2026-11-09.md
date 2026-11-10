# Linux Forge UI review

The Forge desktop follows the Windows cleanup: one primary action for the current
state, mode options below the cards, Full / Lighter texture segments, maintenance
under More options, and a version-only footer. Session details retains the full
failure diagnostic across successful refreshes. Account sync opens its instructions.
An owned session's End session action takes priority over update offers and errors;
a failed bridge offers Reconnect. The terminal's `--plain status` uses the same
short readiness summary; ordinary `status` keeps its JSON output.

The 0.8.1 release keeps Full / Lighter textures and CIS droids in Clones, alongside
Delta Squad and Covenant. Empire retains published r45-v4 Full appearance with
fixed enemy models. Experimental Empire Lighter and enemy switches are hidden
and rejected by the selector and bridge. Unavailable saved choices remain readable;
the explicit **Use published Empire appearance** action selects the supported
configuration. Switching modes alone preserves them. The earlier r47 integration
remains separate research, not content shipped by this launcher release.

Linux launcher updates retain the existing release-page handoff: View update opens
the compatible Linux release, Not now returns to setup or play, and a failed
handoff offers Try update again. Checking updates and long setup operations show
indeterminate progress. This does not add an in-place Linux package updater.
The shared verified downloader and appearance exports use PR #20's bounded retries,
cancellable backoff and pinned-file cache-busting fallback, preserving size/hash checks.

## Render the actual Avalonia window

Restore from the local package cache or NuGet first, then on Windows run:

```powershell
powershell -NoProfile -File tools/render-linux-forge.ps1
```

The script builds with both telemetry opt-outs and writes every state at 100% and
150%, plus 700 × 600 minimum-size cases, to `D:\ClaudeWork\ui-shots\linux-after`.
It uses GuiSmoke's RenderTargetBitmap path and requires a desktop display, but no
HellForge resources, connection, preferences, game or account. Preview controls
cannot execute player actions. To render one state on either supported desktop:

```text
dotnet Clonedivers.Linux/bin/Release/net8.0/clonedivers.dll --gui --forge-preview ready --forge-preview-mode empire --forge-render /path/to/screenshots
```

States: ready, setup, running, sync, recovery, error, disconnected, installing,
update-available, update-checking, update-failed, details. Modes: regular,
clonedivers, empire. Optional `--forge-preview-size 700x600` exercises the minimum
viewport; overflow stays in the vertical scroll area.

## Verification

- `dotnet run --project Clonedivers.Linux.Tests -c Release -p:ForgeEdition=false --no-restore`: all 131 checks passed.
- Same command with `-p:ForgeEdition=true`: all 131 checks passed.
- `dotnet run --project Clonedivers.ForgeBridge.Tests -c Release --no-restore`:
  245 assertions passed before CompatibilityTests reached the real Forge CLI
  quiescence guard during ongoing live Forge activity. The suite must be
  rerun with both the game and transient Forge CLIs closed; no guard was bypassed.
- Desktop build succeeded; both test editions compile. The existing nullable
  warning in LinuxForgePlayerTests.cs:40 remains.
- Rendered 40 Avalonia PNGs and visually checked every primary state, all modes,
  minimum-size windows and 150% output. Rendered on the available Windows
  desktop; Linux gameplay/runtime qualification remains separate.
