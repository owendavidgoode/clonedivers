# Bridge import fixture process isolation, 2026-11-09

`HellForgeClient.RequireIdleAsync` (`Clonedivers/HellForgeClient.cs`) checks
`Game.IsRunning()` before consulting CLI status. On Windows the default
`Game.IsRunningCheck` uses `Process.GetProcessesByName("helldivers2")`.
This production guard must continue to block mutations during a game session.

The bridge test program installs an idle hook for its parent process, but
`ImportTests.RecoveryAsync` launches a fresh test executable with
`--hard-exit-import-fixture`. That child previously inherited only mocked CLI
commands, not the parent's static process hook. A host game process made
`InstallPackAsync` fail at `RequireIdleAsync` before the intended `mod-add` /
`Environment.Exit(73)` checkpoint. Exception unwinding disposed the child's
fixture, recursively deleting the root it borrowed from the parent. The parent's
cleanup then threw `DirectoryNotFoundException`, masking the original failure.
No production path deleted or moved that fixture root.

The fixture now installs its own `Game.IsRunningCheck` backed by a separate
`ProcessRunning` flag, saves/restores the previous delegate, and only deletes
roots it created. Both the normal parent and crash child use this fixture.
CLI `Running` and process presence remain separately controllable. Added checks
exercise borrowed-root cleanup/hook restoration and recovery refusal before any
CLI call when a fixture game process is present. Production code is unchanged.

## Evidence

Only the D: worktree was built. A harmless copy of Windows `PING.EXE` was started
from a unique `%TEMP%\clonedivers-process-probe-*\helldivers2.exe` directory with
`-t 127.0.0.1`; its PID, executable path and `helldivers2` process name were
verified. No HellForge or real game session was started or stopped.

Commands used (with the .NET 8 directory prepended to PATH and
`DOTNET_CLI_TELEMETRY_OPTOUT=1`):

```powershell
Copy-Item "$env:WINDIR\System32\PING.EXE" (Join-Path $probeRoot 'helldivers2.exe')
$probe = Start-Process (Join-Path $probeRoot 'helldivers2.exe') -ArgumentList '-t 127.0.0.1' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $probeRoot 'ping.log')
Get-Process -Id $probe.Id
dotnet run --project Clonedivers.ForgeBridge.Tests -c Release
```

Before the fix: reproduced `DirectoryNotFoundException` in
`ImportTests.Fixture.DisposeAsync`, called from `RecoveryAsync`.
The faulted frontend remained alive after printing its unhandled exception;
only that known test process was terminated to finish collecting the failure.

After the fix, with the same probe process still running:
**253 bridge/plan/protocol assertions passed**, exit 0. This includes the fresh
crash child reaching exit 73 and complete official-CLI fixture recovery.
The owned probe PID/path was verified again, then that probe was stopped.

`dotnet run --project Clonedivers.Tests -c Release` was also run after stopping
the probe. **885 PASS lines** preceded the existing environment failure:
`FpvDefaultsTests.Run`, line 55, calls `FpvDefaults.Seed`, line 197,
whose `File.Replace` throws `UnauthorizedAccessException: Access to the path
is denied`. The Windows suite did not complete. The known faulted test frontend
was terminated after its exception was captured. No FPV logic or checks were
changed to work around this sandbox limitation.

Local full logs and inventory scratch files are retained under
`dist/housekeeping-evidence/` (ignored); the C: review CSVs are stored beside
this report. A real game was not needed for this process-name reproduction.

The assignment commit was blocked: `git add` could not create
`C:/Users/goode/OneDrive/Desktop/clonedivers/.git/worktrees/sw-housekeeping/index.lock`
(`Permission denied`). The lock does not already exist, and read-only ACL
inspection found explicit write-deny entries. No permission changes or alternate
Git-metadata workarounds were attempted. The five changed/new files remain ready
for the orchestrator to commit on `codex/housekeeping-20261109` with the requested
co-author trailer; nothing was pushed.
