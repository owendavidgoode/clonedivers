# Clonedivers Forge Linux 0.10.0

> **Paired with Windows `v2.1.0`:** Linux Forge `0.10.0` (`linux-v0.10.0`) ships in lockstep with [Windows `v2.1.0`](v2.1.0.md), **EmpireDivers `2026.11.22-r49`**, **Clonedivers `2026.11.22-r21`**, and gameplay catalog **`build25480438-r8`**.

## Highlights

- **All 5 Native Texture Tiers on Linux & Steam Deck:** Full support for `Full (Ultra)`, `High (Streamed)`, `1024px (Medium)`, `512px (Low / Deck)`, and `256px + Decimated Meshes (Ultra-Potato)` across `LinuxForgeHost`, `LinuxForgePreferences`, and `HellForge.Launcher.ClonediversBridge` — cutting active GPU VRAM by up to `-80%` (`1.73 GB` Empire / `1.93 GB` Clones) and pack disk footprint by `-66%`.
- **Empire `r49` & Clones `r21` Visual Repairs & Loadout Controls:** Includes the solid folded-wing HD Lambda shuttle, film-scale AT-ST across all 4 Exosuits, 500+ Empire and Clones armor/rig repairs, 3-way **Squid Skin** selector (`Mixed Covenant` default, `All Covenant`, `Native Squids`), and Empire **Encrypted Death Trooper Voice** toggle under **Customize → Advanced**.
- **Gameplay Preset `build25480438-r8` (`17` Verified Mods):** Ships `empire-atst-writer 0.2.2`, O(1) `empire-camera-pullback 0.1.2`, D11 Mega City OOM-safe `chaos-invasions 0.3.3`, 4-player co-op `dedicated-melee-slot 1.5.2-clonedivers.2`, host-gated `faster-corpse-cleanup 1.0.6-clonedivers.1`, and `heavy-seaf-presence 1.2.0-clonedivers.5`, with `r8-xxxx` Squad Match Code parity with Windows.
- **Launch Staging UX & Overhaul Guard:** Adds the unmistakable pre-window **`Launching Helldivers 2 — please wait…`** progress banner + secondary **`Cancel Launch`** button (omitted safely when attached to an unowned session) and automatic Star Wars overhaul-pack collision protection. See [v2.1.0](v2.1.0.md) for full details.

## Verification

- **Artifacts (`main @ 83e29ee`, CI [run 44999196686](https://github.com/owendavidgoode/clonedivers/actions/runs/44999196686)):** `Clonedivers-linux-x64.deb` (`61,165,574` B, SHA-256 `0e28e3565f56d18c6ed22406e1c504e5c3fb66bca6aae3ac4acb36467f782f84`), `Clonedivers-linux-x64.tar.gz` (`67,306,830` B, SHA-256 `3d672a13b3672f9902682d8a0e7fd7645ea1070c806f38501af58df9f87334f9`), `clonedivers-forge-channel.json` (`374` B, SHA-256 `8fe7309af30f89f5368ff754b9f71b7b7a25fa5b52afd08e7198f49e24cce3d6`).
- **CI Packaging & GUI Smoke:** Passed Ubuntu `22.04`, `24.04`, and `26.04` headless install, entry-point, checksum, and GUI smoke checks. Live Linux Proton gameplay remains unqualified pending attended playtesting.
