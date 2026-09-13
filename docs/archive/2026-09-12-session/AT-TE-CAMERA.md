# Historical record — superseded

See [current status](../../STATUS.md). This file preserves the investigation as it happened; installation claims and pending actions below may be obsolete.

# AT-TE camera investigation — 2026-09-12

The requested fix is to move the follow camera farther behind the walker. Raising
vertical FOV widens the view without moving the camera. The user confirmed 75 was
better; the current setting is 85, with no gameplay confirmation at 85. No camera
distance patch was produced or installed during this investigation.

Read-only findings:

- The current user config exposes `vertical_fov`, but no camera distance/offset key.
- The extracted combat walker bone list has no named follow-camera control. It
  includes `attach_driver`; moving that is not established as a camera-only edit.
- The extracted boot Lua did not expose a camera-distance setting.
- The AT-TE ZIP's Walker Animation Edits patch contains only `transport_idle`
  (`a3ea645654761603`) and `transport_drop` (`a1dbed9520c86727`) animation resources.
  This is not evidence of an adjustable driving-camera offset.
- The extracted type library has 1,177 types and a zero-length type-info string
  table. Known type hashes resolve several vehicle types, but their member names
  are absent. Camera-named types concern effects/shake. No reliable follow-distance
  member has been identified; unnamed numeric fields must not be guessed at.
- No applicable vehicle camera-distance fix was found in the checked AT-TE author
  description/posts/bugs or the cinematic tool description. This does not prove
  such a mod is impossible.

The [AT-TE author's pinned notes](https://www.nexusmods.com/helldivers2/mods/6396?tab=posts)
warn that the optional seated-driver relocation may affect player damage. That
addon should not be represented as a verified camera-distance fix. The
[HD2 Cinematic Tool](https://www.nexusmods.com/helldivers2/mods/593?tab=description)
uses a first-person setup and does not establish an exosuit follow-camera solution.

Further implementation requires identifying the actual follow-camera anchor or
distance parameter and validating that it changes framing without relocating the
driver. Existing inspected assets are insufficient to make that patch reliably.
No game files or user settings were changed during this read-only investigation.
