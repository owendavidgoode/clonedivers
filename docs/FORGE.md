# Clonedivers Forge Status

Public downloads are **Windows 1.14.1** (`v1.14.1`) and **Linux x86-64 Forge 0.8.1**
(`linux-v0.8.1`) with native controller **0.4.0** (`HellForge.Launcher.ClonediversBridge`),
targeting official **HellForge 0.4.0-beta / engine 0.1.47 / SDK 16 / game 25480438**.
Linux updates require installing the complete `.deb` or `.tar.gz` package. See
[the migration guide](FORGE-MIGRATION.md) for rollout and recovery.

## Live Preset and Published Assets (`1.14.1` / `0.8.1`)

The live launcher installs the complete pinned gameplay preset through the
official CLI. Embedded catalog `build25480438-r3` (`forge-gameplay-build25480438-r3.json`)
pins **7 required** gameplay downloads (`7,780,423` bytes) plus **1 optional**
client-only `Clonedivers Clean Exit 0.2.0` archive (`8,192` bytes) for **8 downloads**
totaling **`7,788,615` bytes (`~7.43 MiB`)** with immutable URLs, archive sizes,
SHA-256 digests, and official content hashes. Three baseline mods come from
official Forge setup (`HellForge Editor 0.4.2`, `Hell Forge Difficulty 0.16.1`,
`Vanilla Plus 0.2.0`), and Clones/Empire add one selected appearance pack (`12`
active mods with defaults enabled).

The Heavy (`1.2.0-clonedivers.3`) and Squad (`0.1.4-clonedivers.2`) compatibility
files are distributed under the owner's confirmed written author permission, with
credits retained. Five other dependencies are obtained directly from authors. See
[permission scope](SEAF-COMPATIBILITY-PERMISSION.md) and the embedded gameplay
catalog. Files are staged and verified before imports, with backups and durable
recovery. Accounts and game files are not bundled.

Clean Exit `0.2.0` is live at [the Clean Exit release asset](https://github.com/owendavidgoode/clonedivers/releases/download/forge-mods-2026-11-08/clean-exit-0.2.0.zip)
(`8,192` bytes, SHA-256 `6835354fff8264c6f29c33dfac71a0b6e4c4da7a401920bb76e1b604d2435932`,
official CLI content hash `0006a02101583cf05e3bd9fa4e431409c18ff4ac72f59ae1ece8c7d9831a888a`).
Clean Exit is enabled on the first `r3` setup. Its `gameplay: false` manifest keeps
it outside HellForge's co-op modlist matching, so friends can leave it out.
After a successful `r3` setup, players can disable, replace or remove it in
HellForge. The completed setup record preserves that choice across restarts,
required-mod repairs and repair rollback. An already disabled copy is also
respected on first setup. Required artifacts continue to be verified at launch.
Clean Exit's absence also permits launch before the first `r3` setup. Completed
`r1`/`r2` records remain readable; pending older-catalog records retain their
backup through recovery review.

Live appearance selections in `1.14.1` / `0.8.1` are **Clones** (`2026.09.27-r19`,
Full/Lighter textures, Delta Squad, CIS droids, Covenant squids) and **Empire**
(`2026.11.08-r45-v4` Full, `8,435,474,224` bytes / `~7.86 GiB`, fixed CIS models on
`pack-2026.11.08-r45-v4-files`).

## Staged Candidates (`1.14.2` / `0.8.2` / Wave 2)

- **Launcher polish (PR #25, `claude/polish-20261115`, targeting `1.14.2` / `0.8.2`):**
  Integrates Wave 1 storage, Windows UI/Warbonds, and Linux/bridge polish fixes
  plus commit `c90673f` (skip missing `<root>/mods` when planning fresh Linux pack
  exports), `70 GB` (`70 GiB`) setup space checks (`HellForgeSetup.cs` /
  `HellForgeGameSetup.cs`), resumable `.partial` appearance downloads, and headless
  `xvfb` Linux GUI smoke checks across Ubuntu 22.04/24.04/26.04 (11/11 CI jobs
  green on `c90673f`). See [v1.14.2](releases/v1.14.2.md) and
  [linux-0.8.2](releases/linux-0.8.2.md).
- **Empire `r47-shuttles` candidate (unpublished, Wave 2 gate pending):**
  `D:\ClaudeWork\empire-forge\empire-forge-r47-shuttles` (`96` archives,
  `8,738,707,008` bytes; `102` total files with metadata/credits) builds on `r47`
  Full with the `lambda-fix` SEAF personnel Lambda `+0.20 m` ground clearance
  repair (`18f11a4e18192cfe`), while keeping extraction Pelican `ef3a4136b21592cb`
  unchanged. Offline `verify_candidate.py` passed (`6,880` resources, `0`
  findings). Catalog promotion requires passing Wave 2's `W4` 12+12 D11 Automaton
  A/B gate and uploading `62` new files (`8,282,726,720` bytes / `~7.71 GiB`) to
  `pack-2026.11.09-r47-files` (`34` files / `455,980,288` bytes reused from
  `r45-v4`; re-prepare requires `~25 GB` free). See
  [pack-2026.11.09-r47-full](releases/pack-2026.11.09-r47-full.md).
- **Experimental gameplay catalog `build25480438-r4` (`claude/exp-yoda-011-20261115`):**
  Staged offline (not merged or published); adds optional `empire-lego-yoda` `0.1.1`
  (`45,502` bytes, `gameplay: false`, `coop: per-player`) for `9` catalog downloads
  (`7,834,117` bytes) pending a live death check and release asset publication.

## Qualification, Migration, and Cleanup

The owner's Windows account level/Warbonds and solo D11 SEAF call-ins were
verified on the live stack. Windows launch/End-session, isolated software fixtures
and Linux package install/GUI smoke checks are distinct evidence. Actual Linux
account/Proton gameplay, extraction/reward-spend persistence and separate-PC
multiplayer remain open. Follow [squad acceptance](HELLFORGE-SQUAD-ACCEPTANCE.md),
[mod coverage](FORGE-MOD-COVERAGE.md) and author receipts. Source/package CI is
not gameplay certification.

The `0.4` runtime migration updates the official Windows application and replaces
the Linux Wine bridge with native installation/control. Setup applies sharing OFF
automatically. The old standalone mine patch is disabled because the engine
includes its fix. See [upgrade evidence and remaining manual steps](FORGE-0.4-TURNKEY-2026-11-05.md).

The update preserves Steam files, settings and Forge account/profile data.
Windows retains a verified previous executable before old-file cleanup. Old
Steam pack metadata stays unchanged in compatibility feeds. Hidden `--steam` is
recovery only; ordinary official HD2 launches through Steam. Clean Exit `0.2.0`
produced clean quits in the reported 20-cycle D11 soak (though pre-fix `r47` mini
A/B cycle 3 still hit `+0x626296` when ending inside a mission). Ship distortion
remains open; official **End session** cleanup remains available.

[The original preview](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-preview-2026-11-04)
and its [artifact provenance](FORGE-REVIEW-ARTIFACTS-2026-11-04.md) remain historical
snapshots. PR8 integrated the initial source; subsequent migration work adds the
replacement update path and complete Ubuntu/Debian package. Current workflow
results and release checksums establish which exact binary was published.
