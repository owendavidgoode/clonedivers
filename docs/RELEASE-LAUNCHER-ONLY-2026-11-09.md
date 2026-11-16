# Clonedivers 1.14.1 and Linux 0.8.1 Launcher Release Summary (2026-11-09)

Public release summary for **Windows 1.14.1** (`v1.14.1`), **Linux Forge 0.8.1**
(`linux-v0.8.1`), **Linux Steam 0.5.0**, and **bridge 0.4.0** (`HellForge.Launcher.ClonediversBridge`),
published on November 9–10, 2026 (feed activation `2aa3806`, `README.md` links
updated in `44abd2f`).

- **Single primary action and clearer diagnostics:** The launcher uses one primary
  action per state (`Set up`, `Install mods`, `Sync account`, `Launch`, `End session`,
  `Update`), consistent Regular/Clones/Empire mode views, plain-language error
  summaries, and diagnostics under **More options > Session details**.
- **Resilient verified downloads:** Launcher, pack, and mod downloads resume via
  HTTP Range, retry transient server failures, and fall back to cache-busting
  addresses while checking exact byte sizes and SHA-256 digests before use.
- **Appearance and gameplay scope:** Clones retains `r19` (Full/Lighter, Delta Squad,
  CIS droids, Covenant squids). Empire retains the published **`2026.11.08-r45-v4` Full**
  appearance (`pack-2026.11.08-r45-v4-files`) with fixed CIS enemy models. Gameplay
  remains on `build25480438-r3` (including live optional `clean-exit-0.2.0.zip` on
  `forge-mods-2026-11-08`). Experimental Empire Lighter and enemy switches are excluded;
  saved unavailable Empire options stay readable until the player explicitly chooses
  **Use published Empire appearance** or returns to Clones, and switching modes alone
  does not erase saved choices.
- **Platform boundaries:** This release does not include `r47` appearance changes or
  LEGO proportion repairs. Linux Proton gameplay and separate-PC multiplayer remain
  unqualified. Native Quit hanging/teardown crashes (`+0x626296`) remain open; use
  **End session** for supported cleanup.

Follow-on Wave 1 polish (`1.14.2` / `0.8.2` candidates) and the `r47-shuttles` Full
candidate are tracked in [v1.14.2](releases/v1.14.2.md), [linux-0.8.2](releases/linux-0.8.2.md),
and [pack-2026.11.09-r47-full](releases/pack-2026.11.09-r47-full.md).
