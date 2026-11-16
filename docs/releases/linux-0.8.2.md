# Clonedivers Forge Linux 0.8.2

> **Candidate release notes (unpublished):** `linux-v0.8.2` (`claude/release-1.14.2-all`,
> off `c90673f` `11/11` CI jobs green incl. Ubuntu `22.04`/`24.04`/`26.04` `xvfb`)
> is not published yet. It bundles the 4-variant Compact-LEGO-baked Empire `r47`
> UI/catalogs and `build25480438-r4` (`4` optional project mods), awaiting Wave 3
> `R1` asset upload (`pack-2026.11.09-r47-files`, `forge-mods-2026-11-15`) and `R2`
> attended checks (`manifest*.json` feeds remain unchanged until live).

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
- **4-variant Compact-LEGO-baked Empire `r47` (`Full`/`Lighter` × `CIS Droids`):**
  Linux Forge (`0.8.2`) and `HellForge.Launcher.ClonediversBridge` (`0.4.0`) embed
  both Compact-LEGO-baked `r47` manifests (`hellforge-empire-r47.json` and
  `hellforge-empire-r47-lighter.json`) and restore the 4-way appearance controls
  (`Full` | `Lighter` textures and `CIS droids` ON | OFF): `8,738,707,008` B
  (`Full + Droids`, `34` reused `r45-v4` assets), `6,013,620,208` B (`Full + Native`,
  `32` reused `r45-v4` assets), `8,961,480,892` B (`Lighter + Droids`, `2,542,440,456` B
  GPU), and `6,172,775,164` B (`Lighter + Native`). Updating from `r45-v4` Full
  requires `~25 GB` free and `~7.71 GiB` (`8,282,726,720` B) download across `62`
  updated files (`r45-v4` remains the live fallback until `R1`/`R2`).
- **Gameplay catalog `build25480438-r4` (`11` downloads, `8,063,255` bytes):**
  Adds `lego-compact-leaf-writer` `0.0.9` (`199,207` B), `empire-lego-yoda` `0.1.2`
  (`45,907` B, dual Compact LEGO & `r45`/`r47` stilt torso hashes), and
  `chaos-invasions` `0.2.0` (`29,526` B, `gameplay: true`) alongside `clean-exit`
  `0.2.0` (`8,192` B) and the `7` required archives (`7,780,423` B). All `4` optional
  mods are enabled by default on fresh `r4` setup and offered once on `r3` -> `r4`
  upgrades while preserving player opt-outs.
- **Platform and non-shipping boundaries:** Clones (`r19`), Regular, legacy
  `--steam` (`0.5.0`), and the bridge protocol retain their existing behavior. Keep
  the launcher binary and its `forge/` companion together when upgrading the
  portable package, or install the `.deb` over the prior package. Non-shipping/held
  items (`at-st` Hulk rig exceeding 64 palettes/128 nodes, `empire-weapons`,
  `mixed-appearances`, `empire-tie-switch` `0.2.1` pending HellForge `>=0.4.1-beta`
  `eagle_base`, plus diagnostic-only `empire-lego-camera` `0.0.2` and `chaos-probe`
  `0.1.0`) stay outside `0.8.2`. Ubuntu `22.04`/`24.04`/`26.04` package upgrades
  and `xvfb` GUI smoke pass CI; attended `R2` checks, live Linux Proton gameplay,
  account capture, extraction, and multi-PC co-op remain unqualified. See
  [v1.14.2](v1.14.2.md) and [r47 Full notes](pack-2026.11.09-r47-full.md).
