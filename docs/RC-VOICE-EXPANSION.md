# RC voice expansion — September 23, 2026

Built and verified locally; not installed or published. The shipped r9 mapping
and its reproduction lock remain unchanged. This is the broader follow-up to
the six examples in the [audio review](RC-AUDIO-REVIEW.md).

| Character | Shipped recordings | Staged recordings | Shipped mapped entries | Staged mapped entries |
| --- | ---: | ---: | ---: | ---: |
| Sev | 92 | 153 | 405 | 423 |
| Fixer | 89 | 145 | 418 | 423 |
| Scorch | 89 | 124 | 372 | 378 |
| Boss | 52 | 74 | 293 | 301 |
| **Total** | **322** | **496** | **1,488** | **1,525** |

The expansion uses 228 previously unused recordings and retires 54 earlier choices,
for a net gain of 174 recordings (54%). Distinct recordings are measured by source
SHA-256; different performances of the same words still count separately. These
are not 496 different sentences. The number of game entries is also separate from
the number of source recordings: one recording can serve several game entries.

There are 37 newly covered entries, 765 entries assigned a different recording,
and no previously mapped entries returned to fallback. All 3,140 transcribed game
entries remain accounted for across the four slots. The remaining 1,615 entries
retain the underlying clone audio.

## Selection and corrections

`tools/expand-rc-voices.py` considers all 3,988 audited unique sources. Newly used
recordings require original subtitle evidence, at least 0.8 subtitle/recognition
agreement, no uncertain/no-speech recognition flag, a short duration, and a full
match to an explicitly curated event phrase. The unreliable second caption pass
is not used. Existing nonverbal injury recordings retain their Hurt-selector
provenance. Automated recognition is evidence, not a claim of manual listening.

Acknowledgments, movement, healing, injury, spawning, terminal interaction and
combat warnings gain alternatives. Allocation favors less-used eligible sources
across related events rather than choosing by media ID modulo pool size. This is
a static reassignment of existing event variants, not new runtime randomization;
actual variety still depends on which variants the game plays.

The staged map also removes named-Boss responses from generic interactions,
replaces kill confirmations used as attack cries, separates orbital danger from
enemy reinforcements, and uses first-person terminal activity in place of orders
to another commando. Not every retired recording was incorrect; some lose their
place to a more suitable pool or to the limited number of event variants.

Specific coordinates, distances, equipment and campaign dialogue are not filled
with arbitrary chatter. “Tank destroyed” remains unused without a proven tank-kill
event. “Last one down” remains fallback because its event context is not established.

## Artifacts and validation

Staging directory: `dist/rc-voice-expansion-2026-09-23/`.

- `mapping.json`, `summary.json`, `changes.json`: exact assignments and comparison.
- `new-recordings.json`: the 228 new sources with character, words and assigned events.
- `catalog-disposition.json`: decisions and usage for all 3,988 audited sources.
- `patch/Republic Commando Voices.zip`: built patch, 36,611,956 bytes.
- `patch/build-report.json`: source mapping, template and output hashes.
- `validation.json`: successful independent staged checks.

Validation checks every source hash and actor, complete target/fallback accounting,
new-source recognition gates, 108 protected fallback entries, output hashes, and
all four banks. Restoring just the changed codec fields reproduces each original
bank byte-for-byte. The builder also verifies each serialized media resource.
Repeating mapping generation reproduced the hash used by the already-built patch.

The template index was extracted from the existing recovered Full Clone Voice
Conversion ZIP and verified against SHA-256
`f321f972fcaa187921c5281c208b05e4bef2501b13fb87831f49cc5c3fc84de0`.

Reproduction uses the existing local Python 3.13 runtime and staged audio tools:

```powershell
python tools/expand-rc-voices.py --out dist/rc-voice-expansion-2026-09-23
python tools/build-rc-voices.py --template dist/rc-voice-expansion-2026-09-23/template/9ba626afa44a3aa3.patch_159 --mapping dist/rc-voice-expansion-2026-09-23/mapping.json --out dist/rc-voice-expansion-2026-09-23/patch
python tools/check-rc-voice-expansion.py --stage dist/rc-voice-expansion-2026-09-23
```

No API charges, new downloads, live-game writes, release changes, or volume
normalization were made. Boss loudness remains a separate mix task. Game timing,
playback and multiplayer acceptance remain untested. Keep this staged until those
checks and the rest of the player customization update are ready.
