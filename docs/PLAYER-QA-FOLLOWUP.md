# September 23 QA follow-up

The user passed ADS on the HD2 7.1 candidate. Factory Strider/MTT cannon visibility
remains unconfirmed; "presumably on" is not an acceptance result. A previous launch
crashed without a captured cause, followed by a successful relaunch.

Local follow-up: `dist/player-followup-2026-09-23/feed/manifest.json`, version
`2026.09.23-player-preview2`. Public launcher 1.5.0 / pack r9 remain unchanged.

## Equipment names and variety

Both voice presets now name the four body replacements as Commando Boss, Fixer,
Scorch and Sev armor, retaining their original item codes. Body/helmet name keys
are shared, so labels explicitly qualify the equipment type. CM-09 names both its
212th armor and Commando Sev helmet; the B-01 variants share a combined Clone /
Commando label. Unique names for Boss/Fixer/Scorch B-01 helmets remain unsupported.
Only English names change. Item ownership, stats and the Brawny requirement remain.

Eight additional equipment groups expand the mixed clone roster from 7 to 15:

| Representative armor | Style |
| --- | --- |
| FS-37 Ravager | White regular |
| FS-55 Devastator | 327th |
| CW-22 Kodiak | 104th |
| CM-21 Trench Paramedic | Coruscant Guard |
| EX-03 Prototype 3 | 212th |
| B-27 Fortified Commando | 41st camouflage |
| AF-52 Lockdown | 187th |
| AD-11 Livewire | White regular |

Shared model groups can include additional stock items; the complete mapping is in
`roster-full/roster-report.json`. These reuse the existing legion palette rather
than adding new legion designs. Delta body and helmet unit IDs are excluded from
the roster overrides. Geometry, rigs, shader payloads and texture pixels are
preserved; Full/Lighter variants pass receipt checks. The expanded Full roster GPU
file is about 604 MiB, up from about 316 MiB. This is additional download/model
payload, not an established performance improvement.

## Probe audio transfer — experimental

Logical bundle 177 (Probe Droid Guard Dog) is removed. The current-game rebased
PEW-PEW Guard Dog bank in bundle 147 remains, preserving its weapon audio.

New bundle 314 changes one embedded sample in current Watcher/Illuminate Observer
bank `d8b809d8749c06c0`. Media 591126644 belongs to sound 851622742 in single-source
infinite-loop container 87182188. Existing play event 1790836314 and stop event
2157255410 retain their routing and properties. The exact gameplay meaning of
this loop has not been established, so this must not be published as proven patrol
chatter or substituted into arbitrary Watcher barks.

The donor recording is downmixed to mono PCM for spatial playback, with no gain
normalization: 48 kHz, 16 bit, 134.78 seconds. The serialized bank changes only its
sample plus that sound's codec and byte length. All 196 other embedded samples,
other chunks and dependency data are unchanged. Report and hashes are bound into
the candidate receipt.

## Validation and remaining gameplay checks

All 16 Delta/droid/cannon/Full-Lighter selections pass manifest, target, asset hash,
scope dependency and armor inclusion checks. Both presets retain 3,246 English
keys; the presets differ at exactly eight voice-name keys. Twelve equipment-name
keys are common. Byte checks also confirm Watcher routing preservation and no
roster overlap with protected Delta units.

Still test:

- Guard Dog no longer plays probe chatter; its weapon sounds still work.
- Watcher plays the probe loop at a sensible distance/time and stops after death
  or leaving the area. If the loop corresponds to the wrong event, keep this patch
  out of the public release.
- Commando names are readable in the armory, and all four bodies/helmets remain
  correct; inspect newly assigned clone equipment and Full/Lighter visuals.
- Confirm MTT cannon visibility/alignment separately. War Strider geometry is not
  addressed by this option.
- Complete remaining combined-update voice/combat and weaker-machine testing.
