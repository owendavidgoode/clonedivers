# Clonedivers Forge Linux 0.8.2

> **Candidate release notes (unpublished):** `linux-v0.8.2` is staged on
> `claude/polish-20261115` (`c90673f`, `11/11` CI jobs green incl. Ubuntu
> `22.04`/`24.04`/`26.04` `xvfb`) and is not published yet. Wave 2's `W4`
> `12+12` D11 Automaton A/B gate (`pack-gate.ps1`) passed on `r47-shuttles`;
> bundling `r47` in `0.8.2` awaits Wave 3 `R1` asset upload and `R2` attended checks.

## Linux Launcher and Forge Bridge Fixes (Wave 1 + `c90673f`)

- **Fresh-install `PlanAppearanceAsync` fix (`c90673f`):** `LinuxForgePlayer.PlanAppearanceAsync`
  runs `FileSafety.RejectLink(mods)` and passes `<root>/mods` to `HellForgePack.PlanAsync`
  only when `Directory.Exists(mods)`, preventing `DirectoryNotFoundException` when
  planning pack exports on a fresh Forge installation before `mods/` exists.
- **Read-only background timer polls (`#15`):** `ForgeOperations.PollAsync` and
  `LinuxForgePlayer.RefreshAsync(..., allowImport: false)` keep 5-second polls
  strictly read-only so background ticks never run `PrepareAsync` or auto-import
  while controls look idle; when setup completion or a pending import becomes due,
  `ForgeWindow` dispatches a foreground `Begin(RefreshPlayerAsync)` operation with
  `Busy=true`, progress display, control locks, and close guard.
- **Background poll error auto-clear (`#16`):** `ForgeOperations` and
  `LinuxForgePlayer` (`errorFromPoll`) clear transient background poll errors
  automatically on the next successful poll so `CanLaunch` recovers, while
  foreground operation errors remain sticky until user action.
- **Deferred Warbond retry in bridge (`#17`):** `PlayerCommands` tracks
  `warbondsPending` across its 5-minute account cache (via `reviewWarbondsWithStatus`
  returning `null` when HellForge is busy/open) and retries pending owned Warbond
  unlocking on subsequent `player-status` polls and before `play`.
- **Steam user in bridge account cache key (`#18`):** `PlayerCommands` parses the
  active Linux Steam user from `~/.steam/registry.vdf` (`ActiveUser`) or
  `loginusers.vdf` (`MostRecent`), invalidates the 5-minute cache on account switches,
  and clears `lastAccount` if `EnsureAccountAsync` fails during `play`.
- **Explicit `pasta` (`passt`) distribution guidance (`#19`):** `LinuxForgeBootstrap`,
  `LinuxForgePlayer.MissingToolsMessage`, and `ForgePresentation.FailureSummary`
  surface the `Ubuntu 24.04+ / Debian 13+` (`passt`) requirement across non-apt
  checks, tool-install failures, and missing-tool errors for both solo and co-op.
- **Preserve prefix alias in `forge.json` (`#20`):** `LinuxForgePreferences.InstallRoot`
  keeps the player's chosen path/alias while `LinuxForgePlan.ResolveInstall` resolves
  symlinks above the prefix into `LinuxForgePlayer.ResolvedInstallRoot` for cache
  and `WindowsPath` calculations.
- **Additional Linux, bridge, and CI improvements:** `ForgeSyncHandoff` / `SyncRequest`
  preserves pending account sync across pre-game fresh-capture refusals and bridge
  reconnects; `LastFailure` annotates `Ready to play` / diagnostics instead of taking
  over setup, recovery, sync, or prepare buttons; `LastNotice` no longer replaces
  `Prepare` instructions; `BridgeSession` validates `result.json` cleanup keys
  (`RequireCompletedCleanup`) before reporting `End session` success; `play` gets
  a 10-minute CLI timeout; and CI runs headless `xvfb` Linux Forge GUI smoke checks
  (`--forge-preview` PNG + live startup test) across Ubuntu 22.04, 24.04, and 26.04.

## Shared Storage, Empire r47 Candidate, and Experimental Status

- **Shared storage and download improvements (Wave 1 #1–#4):** Linux shares
  `HellForgePack` completed-backup reuse (`previous-pack`), scoped `PrunePackStorage`
  with atomic `clonedivers-export.json` writes, resumable `deleting-*` folder cleanup,
  and resumable `.partial` appearance downloads.
- **Empire `r47-shuttles` candidate vs `r45-v4`:** Wave 2's `W4` `12+12` D11
  Automaton A/B gate passed (`14` cycles, `0` events). Once `pack-2026.11.09-r47-files`
  publishes in Wave 3 (`R1`/`R2`), Linux Forge selects the same
  `EmpireDivers 2026.11.09-r47` Full catalog (`~25 GB` free on the cache/prefix
  drive and `~7.71 GiB` / `8,282,726,720` bytes download when re-preparing from
  `r45-v4`, reusing `34` files); `r45-v4` remains the fallback baseline until upload.
- **Platform and experimental boundaries:** Clones (`r19`), Regular, `build25480438-r3`
  gameplay mods, and the bridge protocol retain their existing behavior. Keep the
  launcher binary and its `forge/` companion together when upgrading the portable
  package, or install the `.deb` over the prior package. Experimental tracks
  (`empire-lego-yoda` `0.1.1` / `empire-lego-camera` `0.0.2` Sub-Run 1A pass,
  `lego-compact-leaf-writer` `0.0.9` Sub-Run 1E pass on `claude/exp-lego-20261115`,
  `empire-tie-switch` `0.2.1` Sub-Run 1D Part 1 pass on patched `tie-held-root` w/
  `upstream-pr-eagle-base.patch` prepared for `>=0.4.1-beta`, 4-variant offline matrix
  + `0ed1ac8` staged UI, `chaos-probe` `0.1.0` Sub-Run 3 clean diagnostic, and shelved
  combat lanes) remain outside `0.8.2`. Ubuntu `22.04`/`24.04`/`26.04` package
  upgrades and `xvfb` GUI smoke pass CI; attended `R2` checks, live Linux Proton
  gameplay, account capture, extraction, and multi-PC co-op remain unqualified. See
  [v1.14.2](v1.14.2.md) and [r47 Full notes](pack-2026.11.09-r47-full.md).
