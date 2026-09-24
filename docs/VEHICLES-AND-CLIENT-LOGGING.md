# Vehicle and client logging follow-up — September 23

Local candidate: `dist/vehicle-update-2026-09-23/feed/manifest.json`,
`2026.09.23-player-preview3`. The launcher is built separately in that directory's
`launcher/` folder as `1.6.0-preview2`. Public releases are unchanged.

## Vehicles

- Falchion v1.0.1, Nexus mod 11329/file 54986, replaces the Bastion. Source ZIP
  SHA-256: `7ab635e3c83fee43c4f24213f42e866dc37073afb9f305a16903817e063ea7ec`.
  Source: <https://www.nexusmods.com/helldivers2/mods/11329>.
  Visual bundle contents are preserved through serialization. Existing rebased
  PEW-PEW bundle 149 already supplies AT-TE Bastion cannon audio.
- Supply FRV is a separate unit, `9b2140378640432e`, and was not covered by the
  existing TX-130 mod. A prototype grafts TX-130 geometry onto its native rig,
  remapping mesh/skeleton references by bone hashes. All 201 native joint records
  and original non-geometry header fields remain unchanged. It does not override
  physics, the supply rack or state-machine resources. This proves structural
  preservation, not correct gameplay: donor bind matrices remain, and the donor
  has 9 meshes versus the supply unit's original 10. Check deformation, seating,
  remote weapon visibility, supply access and destruction before release.
- The walker toggle now also restores two original War Strider weapon units that
  the spider-droid source reduces to tiny hidden meshes. Original bones/state
  machines for those two weapons are restored alongside them. The spider body
  stays. The toggle is labeled **Walker weapons** to describe its actual scope.

The spider body changes 58 joint transforms and 47 joint matrices relative to the
current stock rig. Eye/vent weak-point markers are **not implemented**. A guessed
dot or stock-position mesh could mark the wrong place on the animated replacement.
The user has been asked whether to retain the spider and investigate attached
markers or use the original War Strider as a visibility fallback. No fallback
model swap has been applied. Visible weapons are only partial progress on aim points.

Full/Lighter vehicle variants are receipt-verified and preserve texture resolution.
The first optimization attempt used final indices 315–317 and correctly failed the
standalone gap check; it is not a valid variant. `full-ordered/` uses indices 0–2,
and `lighter-verified/` is the successful, sealed variant. Assembly maps these to
315–317. All 16 combined option/profile checks pass. Structural checks cannot
establish HD2 7.1 compatibility or alignment in gameplay.

## Client-side logging

The new launcher records a small local history in
`%APPDATA%\Clonedivers\session-history.json`. It keeps the latest 250 observations:

- Launcher open/close, and previous observation ending without a close record.
- Steam launch request or handoff failure.
- Game first observed running and later observed exited, with elapsed time.
- Launch not observed within 90 seconds, and unavailable process inspection.
- Recorded pack/build/profile/mode and Delta/droid/aim-point option values.

It polls process presence every five seconds while the launcher is open, including
when minimized. Very short launches may be missed. Closing the launcher stops
observation. It does not hook the game, read game memory, capture FPS, collect raw
game logs, infer a crash from every exit, or upload anything. A prior interrupted
monitor can mean reboot, force-close, or launcher failure; it is not proof of a
game crash. Exact GameGuard messages still need the player's report/screenshot.

Existing operation history supplies recent installation/verification/error
categories. **Help & diagnostics → Save report…** exports both histories plus the
existing hardware/install snapshot as a text file. Copy report remains available.
Only allowlisted fields are read/exported; paths, account IDs and arbitrary error
messages are excluded. Corrupt, oversized or unwritable logs do not block launch.

Native launcher tests pass, including log rotation, tampered fields, corrupt and
oversized history, failed writes, unknown process state and one-shot timeout
behavior. The separate 12 diagnostics checks also pass. Players need the updated
launcher to get this logging; asset downloads alone do not add it.

## Gameplay acceptance

Test the Bastion's appearance, driving, seats, firing, doors and destruction. Test
the Supply FRV's seats, weapon, ammo pickup and model deformation. Inspect spider
weapons with the toggle on/off, including whether they are obscured or detached.
The eye/vent marker request remains open. Preserve the prior candidate's pending
Watcher audio, additional armor and multiplayer voice checks.
