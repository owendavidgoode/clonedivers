# Updating Clonedivers to Forge

Windows 1.14.0 and Linux x86-64 0.8.0 target official HellForge 0.4.0-beta,
engine 0.1.47, SDK 16, game 25480438. Use Regular, Clones or Empire here; start the
official online game directly through Steam. Progress imports HD2 → Forge only.

## Windows

Close HD2 and the official Forge manager, then update Clonedivers through its
existing Check updates action. All three old update feeds preserve their Steam
pack data and offer the same verified new executable.

An existing0.3 installation offers **Update HellForge**. Clonedivers verifies the
new author installer and retains the verified previous installer, snapshots
account/profile/settings files privately, and runs the official supported
`--unpack` update. Failed known outcomes restore the previous official application;
an unknown outcome stays pending for explicit recovery. A profile backup is never
replayed automatically. The supported game copy, account and mods are retained.

For a new player, **Set up Clonedivers** downloads and verifies the author app,
opens official game setup, detects completion and continues automatically into
the gameplay preset and appearance. Steam sign-in/QR approval and Windows system
approval remain player actions. No manual engine download, Locate or mod import
is part of the standard path.

## Linux

Install the complete .deb, or extract the entire portable archive including its
forge/ folder. The package replaces the same application/menu entry. Linux update
notices still require package installation; close the old launcher first.

Press **Set up / update Forge**. Clonedivers installs missing Ubuntu/Debian tools
through desktop approval, downloads/verifies the author's native Linux installer,
and opens it. Keep an existing player's prefix selected; the default prefix is
found automatically. Pick Proton and continue the official game/ownership setup.
Clonedivers reconnects and continues the selected preset when game setup is ready.
A custom existing installation can be selected with Locate; it is never silently
replaced with a new character in another prefix.

The0.7 controller uses the installed native CLI, native account verifier and
native manager. The official SessionRunner starts Proton, the network namespace,
Valve relay whitelist and sandbox supervisor. No 0.3 scripts are copied over the
new runtime, and neither a signed-in account nor namespace entry is invented with
environment values. `pasta` (package passt) and Python3 are required for this squad
build; missing relay tools block readiness. The .deb recommends passt and the
setup action requests it explicitly. On distributions without it, co-op remains
unqualified and blocked rather than silently falling back to solo operation.

## First Forge setup

**Set up selected mode** obtains eight additional pinned gameplay archives:
Heavy SEAF, Squad Stratagems, native Smarter SEAF, Operation Reroller, Bingus Shared
Loader, Mod Options Menu, Smarter Guard Dogs and optional Clean Exit. Three baseline mods come from
Forge. Appearance assets are verified and reused/downloaded separately with the
large-download prompt. Credits and permission scope are retained.

Empire uses r45-v4 with Full textures, CIS droids and walker weapons enabled,
Covenant disabled. Prepare Empire again after updating. Clones retains the r19
catalog and saved appearance choices. Interrupted exports reuse verified files.

Setup applies load order, loader/menu options, Guard Dogs AI/safety ON and target
markers OFF. It disables known conflicting alternatives and the retired private
mine-boundary fix: engine 0.1.47 now supplies that fix. The old fix is kept disabled
for recovery rather than deleted. Automatic host gameplay-mod sharing is set OFF
through the author's documented atomic preferences-file contract, preserving
other fields. Each player acquires their own matching preset through setup.

For account sync, the default profile is selected automatically. Use official
Forge's Profile > Account sync > Sync Inventory, approve the system helper and
pass the HD2 title screen. Forge 0.4 closes the normal game after capture. Keep
Clonedivers open: it finishes a requested, verified one-way import after cleanup.
An old snapshot is never replayed at startup. A character previously imported in
native Forge needs a fresh verified capture when first linked; its earned Forge
progress is merged forward rather than reset or refunded from an old snapshot.
Never share account snapshots, seal keys, profiles or import receipts.

## Recovery and testing

A pending application update offers **Recover Forge upgrade**. Gameplay and
appearance transactions have their own recovery actions. End sessions through the
launcher that started them; an active staged gameCopy=false is never a repair
request. Ordinary HD2 and its Steam fallback files remain separate.

Windows update/profile-preservation tests, real official-CLI preset imports and
initialization, and Linux source/package checks are distinct qualifications.
Extraction/rewards and real two/three/four-PC Windows/Linux squads still need
acceptance tests. See [the 0.4 integration record](FORGE-0.4-TURNKEY-2026-11-05.md)
and [squad acceptance](HELLFORGE-SQUAD-ACCEPTANCE.md).
