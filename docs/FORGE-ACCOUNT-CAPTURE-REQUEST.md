# CLI account capture request

Inspected read-only on November 8, 2026 against HellForge 0.4.0-beta, commit
d8a8b46. No capture, game launch or system change was performed for this work.

## Current executable boundary

`src/HellForge.Core/InventorySync.cs` provides the capture lifecycle as library
methods. `src/HellForge.Launcher/InventorySyncFlow.cs` orchestrates those methods
inside the app. `src/HellForge.CaptureProxy/Program.cs` implements the elevated
capture helper. `src/HellForge.Cli/Program.cs` exposes account import tooling,
but no command that supervises inventory capture.

The helper accepts `--timeout-min`, `--output`, `--progression`, `--evidence`,
`--seal-key`, `--steam-account`, `--close-game` and `--grant-sid`. Those arguments
alone do not complete an account sync. The app checks the Steam account, refuses
active games and leftover redirects, waits for the proxy's listening state,
opens the normal Steam game, waits for sealing, calls `FinalizeCapture`, handles
missing progression, observes game exit and verifies cleanup. Finalization
verifies the seal using the installation key and promotes inventory, progression,
seal and metadata into the selected profile's snapshot directory. The proxy
owns a temporary hosts redirect and trusted certificate until the game exits.

The existing executables provide no supported headless supervisor for these
steps. Driving the proxy alone would require Clonedivers to duplicate the app's
finalization and recovery protocol or dynamically load private implementation
DLLs. We cannot establish the complete sealed-capture and cleanup guarantees
through that executable interface. Capture therefore continues through the
official app. Clonedivers retains its existing sealed import verification,
Steam-account binding, receipt/journal checks and forward-only merge.

## Proposed upstream interface

Please expose the app's existing capture supervisor through these commands.

```text
HellForge.Cli.exe sync-inventory --profile default --wait yes
HellForge.Cli.exe sync-inventory-status --run <capture-id>
HellForge.Cli.exe sync-inventory-stop --run <capture-id> --wait yes
HellForge.Cli.exe sync-inventory-cleanup --wait yes
```

The CLI should infer the installation exactly as `play` does. `--profile` must
select an existing profile and must never create, reset or change its template.
Capture should use the current signed-in Steam account and refuse a simultaneous
game, Forge session, capture, import or unresolved cleanup. Keep elevation inside
the official helper and preserve the initiating Windows user's SID grant.

With `--wait yes`, stdout should contain versioned JSON Lines for `starting`,
`awaiting-windows-approval`, `listening`, `awaiting-title-screen`, `sealed`,
`closing-game`, `cleaning-up`, and a terminal `completed`, `cancelled` or `failed`.
Every event should include the capture ID and profile. Useful fields include
`message`, `failureCode`, `inventoryComplete`, `progressionComplete`,
`capturedUtc`, `snapshotDigest` and `cleanupComplete`. Keep credentials, login
sessions, signing keys and raw account responses out of stdout and diagnostics.

The official supervisor should open Steam's normal game only after listening,
verify the installation seal and Steam binding, require both inventory and
progression for success, finalize exactly this run's capture, and report success
only after game closure and removal of the run's redirect and certificate.
Cancellation and launcher loss must leave cleanup supervised. `stop` should
wait for that cleanup and must only affect the capture's own game. Status and
cleanup must work after the invoking launcher has exited or crashed.

Finalization should preserve the last complete snapshot on failure or partial
capture, reject stale or foreign runs, and provide a durable identity for the
new sealed snapshot. It must never replay an old capture or apply account
progress itself. Clonedivers will then verify and merge through its existing
one-way import flow, which rejects stale receipts and refuses reset/replay.
Return nonzero for declined approval, partial capture, failed finalization or
unfinished cleanup, with distinct machine-readable failure codes.

## Remaining player steps today

Clonedivers selects `default` and opens HellForge. The player chooses
Profile > Account sync > Sync Inventory, approves Windows and passes the normal
HD2 title screen. When the game closes, the player returns to Clonedivers and
leaves HellForge's Apply import and auto-apply off. Clonedivers verifies and
merges the completed capture automatically while it remains open.

This request is recorded locally. No upstream issue or message was sent.
