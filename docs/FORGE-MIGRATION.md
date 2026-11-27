# Setting Up and Updating Clonedivers — Forge

**Clonedivers 2.2.1** (Windows) and **Clonedivers Forge 0.11.1** (Linux x86-64, with native
controller `0.4.0` `HellForge.Launcher.ClonediversBridge`) target official **HellForge
`0.4.0-beta` / engine `0.1.47` / SDK `16` / game `25480438`**. Choose **Regular**, **Clones**,
or **Empire** in Clonedivers; launch official online Helldivers 2 directly through Steam.
Account progression imports one-way (**HD2 → Forge**) only.

## Windows

Close Helldivers 2 and the official HellForge manager before updating. Existing Clonedivers
installations update in one click from the launcher banner (**Update & Launch** / **Check
updates**), keeping a verified previous `Clonedivers.exe` for rollback and preserving all
settings, profiles, and Steam files.

For a new installation, run `Clonedivers.exe`, pick your mode (**Regular**, **Clones**, or
**Empire**) and **Texture Quality** (`Full (Ultra)`, `High (Streamed)`, `1024px (Medium)`,
`512px (Low / Deck)`, or `256px + Decimated Meshes (Ultra-Potato)`), and follow the in-window
**4-Step Setup Wizard**:
1. **Step 1 · Preflight Check**: confirms Steam is signed in and at least **70 GB (`70 GiB`)** is free on your chosen local folder outside OneDrive and Steam (for example `D:\HellForge`).
2. **Step 2 · Forge & Game Copy**: downloads and verifies the official HellForge installer and copies build `25480438` from Steam or downloads it via the in-app **Steam Mobile QR** flow (with **Official setup (fallback)** available). Steam sign-in and Windows UAC prompts remain player actions.
3. **Step 3 · Guided Steam Account Sync (Required)**: guides your one-time mandatory HD2 → Forge account capture so your real Warbonds, unlocks, and XP copy locally. Leave *Apply import* and *auto-apply* off — Clonedivers imports the sealed capture automatically as soon as Helldivers 2 and HellForge close, and automatically opens owned premium Warbonds. *(Never share account captures, seal keys, profiles, or import receipts.)*
4. **Step 4 · Pack Download & Ready**: downloads and verifies the `build25480438-r14` gameplay preset (`18` archives, `8,567,133` B) and your selected Star Wars pack (`Clonedivers 2026.11.27-r25` or `EmpireDivers 2026.11.27-r54`) with resumable `.partial` byte progress (`1 MiB` pooled streaming I/O), automatic completed-backup disk reclamation, and multi-volume space checks.

An existing HellForge `0.3.0-beta` installation offers **Update HellForge**, snapshotting
profile/settings privately and running the supported `--unpack` upgrade with automatic
rollback on known failures.

## Linux

Install the complete `.deb` (`Clonedivers-linux-x64.deb`) or extract the entire portable
archive (`Clonedivers-linux-x64.tar.gz`, keeping its `forge/` folder adjacent). Close any
running launcher before upgrading.

Press **Set up / update Forge** in `0.11.1`. Clonedivers offers installation of missing
Ubuntu/Debian networking tools (`pasta` from `passt` and `python3`, required on Ubuntu
24.04+ / Debian 13+ or equivalent for the network namespace and Valve relay whitelist),
downloads and verifies the official native Linux HellForge installer, and connects to your
selected dedicated Proton prefix once game/ownership setup finishes. Existing custom
installations can be chosen with **Locate** and are never silently replaced.

## First Forge Setup

**Set up selected mode** installs the pinned **`build25480438-r14`** catalog (`9` required
gameplay/author archives + `9` optional SDK-16 defaults enabled once on fresh setup, zero
`hf-mod.com` URLs) alongside HellForge's `3` bundled baseline mods and your chosen Star Wars
pack (`22` active mods in Clones/Empire, `21` in Regular). Both **Clones (`2026.11.27-r25`)**
and **Empire (`2026.11.27-r54`)** provide all **5 texture tiers**, **3 Enemy Skins** (`All CIS
Droids`, `Mixed Bots + CIS Droids`, `Native Automatons`), and **3-way Covenant + Squids**
(`Mixed Covenant + Squids` default, `All Covenant`, `Native Squids`), plus **Delta Squad** in
Clones, opt-in **Encrypted Death Trooper Voice** (OFF by default) in Empire, and host-driven
**Theater Scale (`L12 ULTRAKILL · Clone Wars` default, adding a genuine `Difficulty 12` slot after `Hell Forge` `11` while keeping `D1–11` intact, vs `Standard (D1–11)`)** under Customize -> Advanced. Regular installs
only the gameplay preset without Star Wars appearance packs.

Setup applies load order, loader/menu options, Guard Dogs AI/safety `ON` with `Targeting Marker`
`OFF`, and `clonedivers-host-options.json` defaults. It automatically disables conflicting
alternatives and retired mods (`chef.smarter-seaf`, standalone `Mod Options Menu`, and
`local.clonedivers-mine-boundary`, which engine `0.1.47` provides natively), and sets automatic
host mod sharing `OFF` so every squad member runs their own verified setup.

## Steam Cleanup and Recovery

Updating Clonedivers to Forge does not modify your live Steam `Helldivers 2\data` folder. If you
previously enabled mods in a 1.x Steam-era Clonedivers installation, run `Clonedivers.exe --steam`
once and switch to **Regular (Vanilla)** (or verify integrity in Steam) before treating your Steam
install as clean. Hidden `--steam` and `--classic` flags remain recovery tools only.

Pending Forge upgrades, interrupted gameplay installs, and interrupted appearance exports each
provide dedicated **Verify & Repair** / recovery actions and automatically reclaim orphaned
temporary `exports/` directories on low-disk failures. For multiplayer verification steps, see
[the squad acceptance checklist](https://github.com/owendavidgoode/clonedivers/blob/main/docs/HELLFORGE-SQUAD-ACCEPTANCE.md).
