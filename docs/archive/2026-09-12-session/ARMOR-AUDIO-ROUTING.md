# Historical record — superseded

See [current status](../../STATUS.md). This file preserves the investigation as it happened; installation claims and pending actions below may be obsolete.

# Armor-conditioned Republic Commando dialogue investigation

Investigated 2026-09-12. No live game files, active recipe, or audio banks were changed.

## Result

An automatic, asset-only armor-to-voice implementation is **not established**. The game has
emitter-scoped audio switches, so per-diver routing is technically compatible with its audio
engine. The missing piece is a verified signal identifying the speaking diver's particular armor
on the same audio emitter used for dialogue. Replacing recordings or adding a switch container
does not itself provide that signal.

This is a feasibility gap, not proof that the feature is impossible. The current Clonedivers
application installs files outside the running game; it does not observe equipment or choose
audio while a diver speaks. No existing automatic armor-conditioned voice mod was verified.

The desired rule remains based on the **speaker's body armor**, independent of helmet and chosen
voice slot: SC-30 -> Sev; CM-10 -> Fixer; CE-35 -> Scorch; DP-11 -> Boss; all other body armor ->
generic clone. A listener's armor must never control their teammates' voices.

## Evidence

### Emitter-specific switching exists

The installed game contains `core/wwise/lua/wwise_flow_callbacks.lua`, resource
`7251fdd9bb62480a`, in base archive `9ba626afa44a3aa3`. Extracted its LuaJIT bytecode with
filediver v0.7.51. Readable constants contain `set_switch`, `source_id`, `Group`, and `State`,
alongside distinct `set_source_parameter` and `set_global_parameter` callbacks. This establishes
an emitter-scoped API in the shipped assets; it does **not** establish that gameplay sets an
armor switch, or that an armor mesh's source and a character's dialogue source are identical.

This matches Audiokinetic's [SetSwitch API](https://www.audiokinetic.com/en/public-library/2024.1.8_8898/?id=ak_soundengine_setswitch.html&source=SDK):
a switch can target a game object. A global state would be wrong when simultaneous speakers
need different characters. A [switch container](https://blog.audiokinetic.com/en/public-library/2024.1.5_8803/?id=defining_contents_and_behavior_of_switch_containers&source=Help)
can select a child and provide a default branch, once the game supplies the relevant value.

Saved bytecode under `dist/rc-upgrade/armor-audio-extracted/core/wwise/lua/`; printable constants
are in `dist/rc-upgrade/wwise_flow_callbacks.lua.strings.txt`. This was a constants inspection,
not a complete decompilation or runtime execution trace.

### The known armor schema does not expose a voice assignment

The extractor maintainer's [armor schema](https://github.com/xypwn/filediver/blob/master/datalibrary/armor_sets.go)
models customization kit identity, archive, passive, body pieces, weights and material data.
There is no identified voice-prefix or Wwise-switch member in those structures. Some fields
remain unknown, and this public schema is not evidence about every possible gameplay structure.

The maintainer's [type-name catalog](https://github.com/xypwn/filediver/blob/master/hashes/dl_type_names.txt)
separately names `CharacterVoiceComponent`, `CharacterVoiceComponentData` and
`CharacterVoicePrefix`. The enum includes Purist, soldier_female1, soldier_female2 and
soldier_male1 as well as other prefixes. An enum name alone does not show that a voice is
selectable or tied to an armor. In particular, `Helldiver_ViperCommando` must not be treated as
proof of an implemented armor rule.

The [type-library dumper](https://github.com/xypwn/filediver/blob/master/cmd/tools/components/typelib-json-dumper/main.go)
notes that Arrowhead removed type/member strings after February 2024. The current downloaded
type library and extracted Wwise metadata contain binary IDs rather than useful plain-text armor
switch names. String searches therefore cannot rule out an existing, unidentified signal.

### Current voice banks do not reveal a simple armor switch

The parallel bank investigation extracted the installed `Helldiver_Standard_VO` and four US
voice banks and validated their HIRC chunk boundaries. All five use bank version **154**;
none contains a type-6 actor-mixer Switch Container. The four US banks each contain three
type-12 Music Switch Containers, whose purpose requires further parsing. These observations
apply to these five banks only, not every shared bank or gameplay component.

Details and binary evidence are in `dist/rc-upgrade/voice-bank-inventory.json` and
`dist/rc-upgrade/voice-bank-wwise/`. Do not infer that no routing exists merely because a
particular container type is absent; event names, gameplay dispatch and shared ancestors can
also perform selection.

[Wwise Teller's author](https://github.com/Dekr0/wwise-teller/wiki/Finding) has demonstrated new
sound data, source rewiring, Sound/Random/Action hierarchy additions and property changes in HD2.
Those capabilities support a future routing patch, but do not demonstrate adding an armor-aware
switch. The author's historical version-141 note is superseded by our installed-bank check.

### Remote dialogue is rendered locally

[Helldivers International's author](https://www.nexusmods.com/helldivers2/mods/14183) documents
per-voice-slot replacement and listener-local playback. A replacement can change how a remote
diver sounds on the modded listener's client. It does not send that replacement recording to
unmodded clients. Exact network event contents, maximum distance, and replication of every
grunt or exertion remain unmeasured.

Even though a client displays a teammate's armor, this does not prove the audio subsystem receives
that armor ID. These are distinct requirements. Both clients need the eventual voice mod if both
should hear RC recordings; no special audio transfer should be assumed.

## Next implementation gate

1. Parse the current shared/player hierarchy and `CharacterVoiceComponentData` sufficiently to
   trace one repeatable dialogue event from prefix/event to source. Record switch-group IDs,
   game parameters, their scope and any existing armor/material/weight assignments. Inspect
   movement/gear banks for candidate armor-related parameters as well as dialogue banks.
2. Prove a candidate selector varies by **specific kit**, not just light/medium/heavy, passive,
   helmet, player index or voice slot. If only a broad equipment signal exists, determine whether
   an armor asset can assign a more specific value to the dialogue emitter without changing
   gameplay stats. Adding a callback to a detached mesh source is insufficient.
3. Only after that link is identified, build a small staged diagnostic bank for one repeatable
   callout: distinct short RC samples for two armor choices and generic clone fallback. Preserve
   existing event IDs, spatial/radio routing and timing. Rebuild against current version154 banks.
4. Test two players with the **same voice slot and different armors**. Each speaks, then swap
   armors while keeping voices fixed. Repeat with one armor and different voices, changed helmet,
   respawn, a remote join, distance and overlapping speech. Two clients should observe independent
   speaker rules. One unmodded listener should retain their own installed sounds.
5. Expand to four characters and all callout/exertion events only after that test passes. Keep
   the generic clone path as the default for unknown and ordinary armor.

If no existing or asset-configurable per-speaker armor signal can be found, a runtime integration
would be a separate project. No injection or process-memory work was attempted here. A manual
four-slot RC replacement can be built with known tooling, but it cannot preserve generic clone
voices for every other armor and therefore is not a completion of the requested feature.

## Saved research

`dist/rc-upgrade/all-assets-routing.txt` is the current complete filediver inventory.
`armor-audio-extracted/content/audio/` contains all eleven `project*.wwise_metadata` resources
and `custom.wwise_properties` from the installed game. `routing-source/` contains the public
schema/type-name files inspected, with the source index in `filediver-source-tree.json`.
These are research inputs; no replacement patch has been generated from them.

## Follow-up: parsed voice components and armor ownership

The user requires automatic armor matching; manual voice-slot selection is explicitly not an
acceptable replacement. The following staging-only inspection narrows the blocker.

Parsed the public filediver `dl_library.dl_typelib` using the maintainer's
[binary schema and hash function](https://github.com/xypwn/filediver/blob/master/datalibrary/datalib.go).
Validated the `LTLD` magic, version4, every member range, and total file length against the complete
header-described layout. This is the public tooling snapshot, **not a claimed extraction of the
installed process's current runtime data**. Its shared avatar resource does match the installed inventory.

| Type | Parsed 64-bit layout |
|---|---|
| `CharacterVoiceComponent` | 4 bytes: exactly one `CharacterVoicePrefix` enum at offset0 |
| `CharacterVoiceComponentData` | 112 bytes: six `ComponentIndexData` hash-table slots, then four 4-byte voice-component values |
| `HelldiverCustomizationKit` | 64 bytes: scalar identity/text/passive fields, archive hash, kit-type enum, and a body-pieces array |
| `HelldiverBodyTypePieces` | Body-type enum and a piece-info array |
| `HelldiverCustomizationPieceInfo` | Visual unit path, slot/type/weight, material/texture hashes, tone variations |
| `UnitCustomizationComponent` | Four inline arrays of material overrides; no voice component reference |

There is **no member typed as an entity delta, character-voice component, voice-prefix enum, or
owner-component override in the parsed kit/body/piece structures**. The fields shown as unknown
padding in the handwritten armor parser are not additional declared fields in this type library.
In particular, the body array is an array of visual pieces, not a pointer to an owning-avatar
component configuration. This rules out the proposed simple kit-property edit in the inspected
schema; it does not rule out a separately implemented gameplay callback.

Parsed the public `generated_entities.dl_bin` voice table. Its occupied entries include shared
avatar `4d1c334d294dfa97` with initial numeric voice-prefix0, and two other resources with prefix10.
The voice table is a hash map: empty slots must not be mistaken for additional characters.
Parsed the companion entity-delta table: eight records modify component index83
(`CharacterVoiceComponentData`), each replacing exactly4 bytes at component offset0 with one of
the prefixes1 through8. These are explicit per-entity voice-prefix overrides. None of their eight
resource hashes resolves to an armor asset in the installed filediver inventory. Their names and
runtime selection logic remain unidentified, so they are not proof of an armor routing mechanism.
See the maintainer's [entity delta implementation](https://github.com/xypwn/filediver/blob/master/datalibrary/entity_deltas.go).

Also read the actual local `Delta Squad AIO-552-1-1A-1777439542.zip` archive indices without
installing or changing it: 146 resource records comprising **62 units, 48 bones, 27 textures and
9 materials**. It contains no Lua, Wwise, entity or package resources, and it does not replace the
shared avatar or any of the eight voice-override resource hashes. Thus the current Delta armor
mod supplies no existing owner-voice override to reuse.

**Specific remaining blocker:** there is no verified asset field or callback that takes the
selected body kit and sets that kit's owning avatar's dialogue prefix/switch. Writing one global
avatar prefix would affect the shared character definition; attaching a voice component to a
visual armor piece has no demonstrated path to the owner's dialogue emitter. Neither is a valid
implementation of automatic per-speaker matching. The next feasible research step is identifying
the gameplay selection of those avatar voice variants or an owner-directed customization callback.
That requires further engine/component investigation beyond the decoded static kit and bank
schemas. No functional armor-conditioned voice patch can yet be built from the verified route.

Reproduction artifacts: `dist/rc-upgrade/inspect-routing-typelib.ps1`,
`routing-typelib-voice-types.json`, `routing-typelib-all-types.json`,
`routing-component-voice-entities.json`, `routing-voice-delta-entities.json`,
`routing-delta-armor-assets.json`, and source-file SHA256 hashes in `routing-source-validation.json`.

### Final installed-Lua boundary check

Extracted **all ten** resources of type `lua` from the installed game with
`filediver -i '*' -T lua --raw-format main`; the tool reports `Extracted 10/10 matching files`.
This includes unnamed resources if any match that type; the resulting ten all have known names:
`boot`, `debug`, two animation runtime helpers, vector-field and reflection-probe helpers, and
four core Wwise helpers. There is no separate equipment-selection, character-voice, armor-kit,
or customization gameplay Lua module in this inventory.

Inspected printable constants across every extracted LuaJIT resource. No constants match armor,
voice, avatar, equipment, customization, owner/owning, or prefix. `boot.lua` is334 bytes and names
the core Wwise callbacks plus lifecycle/build/debug functions. `debug.lua` names editor-test,
rendering, input, crash-display and reload helpers. The Wwise helpers expose generic sources,
switches and parameters, but do not expose an armor-to-owning-avatar callback.

This closes the proposed straightforward gameplay-Lua inspection route for the installed archive
set. It does **not** prove all gameplay is native code or that no encoded/compiled selector exists
elsewhere. It establishes that there is no named, inspectable gameplay Lua selector in the extracted
resources to patch. Further progress requires identifying the currently unexposed gameplay bridge;
editing these generic engine callbacks without that bridge would be speculative.

Evidence: `dist/rc-upgrade/routing-installed-lua-inventory.txt`, `routing-all-lua-extract.log`,
`routing-all-lua/`, and the complete printable-constant list `routing-all-lua-strings.json`.
