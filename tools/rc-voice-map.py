#!/usr/bin/env python3
"""Map current HD2 voice media to original RC recordings using explicit semantic rules.

Unmatched or ambiguous transcriptions retain the existing clone recording. No game files are written.
"""

import argparse
import csv
import hashlib
import json
import logging
import re
import sys
import unicodedata
import wave
from collections import Counter, defaultdict
from collections.abc import Sequence
from pathlib import Path
from typing import Any

LOGGER = logging.getLogger(__name__)
SLOTS = [
    (1, "Sev", "D07", "helldiver_soldier_female1_VO", "0a39096a51ae9e86"),
    (2, "Fixer", "D40", "helldiver_soldier_male1_VO", "017f8b366af141f8"),
    (3, "Scorch", "D62", "helldiver_soldier_female2_VO", "498646c6a1ffba95"),
    (4, "Boss", "D38", "helldiver_purist_VO", "6c91a47b88d26638"),
]

# Patterns are anchored or scoped phrases. Order resolves overlaps such as team reload vs self reload.
# Each rule explicitly specifies original RC selector names and/or short transcript equivalents.
RULES = [
    ("affirmative", r"^(affirmative|yes|yeah|i'?m on it|i'?ll take it)[.! ]*$", [], r"^(affirmative|yes|yes sir|got it|you got it)[.! ]*$"),
    ("negative", r"^(negative|no|don'?t need it)[.! ]*$", [], r"^(negative|negative sir|negative on that|no sir)[.! ]*$"),
    ("cancel", r"^(cancel that|never mind|nevermind)[.! ]*$", [], r"^(cancel that order|cancel that commando|cancel that|disregard that|never mind)[.! ]*$"),
    ("thanks", r"^(thank you|thanks)[.! ]*$", [], r"^(thanks|thanks for the help(?: delta)?|thanks for the assist delta|thank you sir)[.! ]*$"),
    ("apology", r"^(i'?m sorry|sorry)[.! ]*$", [], r"^(sorry sir|sorry|my mistake|my fault)[.! ]*$"),
    ("follow", r"^follow me[.! ]*$", ["FormUp"], r"^(form up|form up squad|form up deltas|follow me|on me)[.! ]*$"),
    ("hold_position", r"^hold position[.! ]*$", ["StanceHold", "SecurePosition"], r"^(hold (?:your |that )?position|holding this position|defend this position)[.! ]*$"),
    ("retreat", r"^(fall back|run|steering clear|not engaging)[.! ]*$", ["RetreatHealthy", "RetreatInjured"], r"^(fall back(?: move it)?|retreat(?: quickly| blast it)?|abandon position delta)[.! ]*$"),
    ("moving", r"^(on my way|heading there now|rolling out|roll(?:ing)? it (?:out|up)|move(?: move move)?|i'?m on my way)[.! ]*$", ["MoveOut"], r"^(on my way|moving on|moving into position|moving to your mark|getting into position|moving off|moving in)[.! ]*$"),
    ("team_reload", r"(?:team|tim) reload", [], r"^(cover me|i'?ll cover you)[.! ]*$"),
    ("reload", r"reload|new (?:mag|mac|canister)|changing (?:ice|guys|eyes)|swap(?:ping)? (?:ice|internal cooling)|fresh ice|canister.*empty|(?:mag|mac|tech ?pack|tac ?pack)s? (?:is )?empty|nothing in the chamber", ["Reloading"], r"^(cover me)[.! ]*$"),
    ("out_of_ammo", r"(?:out of|need) ammo|^i'?m out[.! ]*$", [], r"^(need a reload here|i'?m out reloading|out of ammo)[.! ]*$"),
    ("throw_grenade", r"throw(?:ing)? grenade|growing grenade|^fire in the hole[.! ]*$|^grenade[.! ]*$", ["ThrowGrenade"], r"^(grenade thrown|grenade live|grenade|fire in the hole)[.! ]*$"),
    ("low_health", r"bleed(?:ing)? out|can'?t stop the blood|can'?t survive these wounds|need (?:a )?(?:stim|stem|stam|help)|out of (?:stim|stem)s|liberty save me|^sos[.! ]*$", ["DropToOrange", "ReviveInitiateSelf"], r"^(i need bacta|could use some bacta sir|need aid now|i need help commandos)[.! ]*$"),
    ("injury", r"my (?:arm|leg|lag)|broken arm|patch up this leg|fix this.*arm|leg.*slowing|i'?m hit|^ouch[.! ]*$|^no pain no freedom[.! ]*$", ["HurtSmallArms", "HurtLargeArms", "HurtMelee"], None),
    ("heal_other", r"stimming you|administering meds|^i(?:'?ve)? (?:have )?got you[.! ]*$|democracy isn'?t done with you", ["RevivePlayerInitiate", "AssistingAlly"], r"^(administering bacta|give him some bacta|assisting you|helping you)[.! ]*$"),
    ("heal_self", r"little sh(?:ot|uttle) of liberty|injury.*what injury|^feels good[.! ]*$|^not today[.! ]*$", ["BactaJackIn", "Healed"], r"^(bacta applied|bacta taken|i'?m healed|nothing like a little bacta|that was close)[.! ]*$"),
    ("terminal", r"(?:engag|handl|got|on|input).*terminal|^i'?ll handle the terminal|^i'?m on the terminal", ["HackTerminalProgress", "HackTerminalConfirm", "HackTerminalInitiate"], r"^(slicing (?:in )?now|slice that terminal delta|commence slicing protocols delta)[.! ]*$"),
    ("completed", r"^done[.! ]*$", [], r"^(done|done sir|done boss|done squad leader|all done|complete|area secured)[.! ]*$"),
    ("spawn_ready", r"reporting (?:for duty|to the front)|joining the fray|democracy has landed|another diver for the cause|ready to liberate|^weapons ready[.! ]*$|^loadout confirmed[.! ]*$|^let'?s do this[.! ]*$|^point me to the enemy[.! ]*$", ["Revived"], r"^(ready to engage|ready for action(?: sir| boss)?|ready for battle|weapon ready|i'?m ready|i'?m ready to go(?: sir)?)[.! ]*$"),
    ("attack", r"^engaging[.! ]*$|kill (?:em|them) all|^eat this[.! ]*$|^burn in the fires of democracy|^freedom fire|^get some", ["KilledExcited", "EngageTarget"], r"^(engage the enemy|assault their position|eliminate opposition|take them out|engaging target)[.! ]*$"),
    ("battle_cry", r"democracy conquers|democracy for all|for prosperity|freedom forever|freedom never sleeps|my life for super earth|fight for (?:freedom|super earth)|soldier of liberty|liberty guides|have a taste|taste of freedom|say hello to democracy|helldivers? never die|hell divers never die|that'?s called democracy|nice cup of|^liberty[.! ]*$|liberty prosperity democracy|let the light of liberty|you will never destroy our way of life|tinder.*liberty.*match|no diver left behind|liberty for every|freedom requires firepower", ["KilledExcited", "KilledCalm"], r"^(another kill for the cause|bang and you'?re dead|let'?s get to it squad|ready to engage)[.! ]*$"),
    ("enemy_droid", r"^bots?[.! ]*$|^bot out ?post|^bot fabricator", ["SpottedDroid", "SpottedSBD"], r"^(droids|droids incoming|found enemy droids|found confederacy droids)[.! ]*$"),
    ("enemy_generic", r"enemy (?:spotted|patrol|out ?post|elite|emplacement)|^contact[.! ]*$|^bugs[.! ]*$|^squids[.! ]*$|^illuminate[.! ]*$|(?:bug|squid) (?:hole|out ?post|warp gate)|^aerial enemy[.! ]*$|^dangerous wildlife[.! ]*$|^heavy[.! ]*$", ["SpottedEnemy"], r"^(enemy spotted|contact|hostiles|enemy sighted|marks sighted|hostiles spotted|got some unfriendlies here|we got a non friendly here)[.! ]*$"),
    ("enemy_incoming", r"drop ?ships|tunnel breach|illuminate teleporting|orbital (?:incoming|inbound)|^danger[.! ]*$", [], r"^(incoming|incoming fire|multiple enemies incoming|enemy reinforcements incoming)[.! ]*$"),
    ("turret_enter", r"manning.*(?:emplacement|placement|replacement)|man(?:ning)? in combat walker", ["TurretConfirm", "TurretReady", "TurretInitiate"], r"^(gunner ready|turret ready sir|acquire turret)[.! ]*$"),
    ("demolition_warning", r"(?:hell|help) ?bomb armed.*clear the area", ["DemolitionExplode"], r"^(stand back|take cover|fire in the hole)[.! ]*$"),
    ("take_item", r"(?:sample|artifact|package|pack is|art effect|uranium|iridescence|d710|d7 10|e7 10).*(?:collected|acquired|secured)|^got a sample[.! ]*$|^sample collected[.! ]*$|^i'?ll take it[.! ]*$", [], r"^(got it|i got it|got it sir)[.! ]*$"),
    ("acknowledge_selection", r"(?:stratagem|strategum|strategy them) selected", [], r"^(affirmative|got it|acknowledged)[.! ]*$"),
    ("request_backup", r"reinforc|sending (?:out|an).*sos|deploying sos", [], r"^(i need backup|need some help here|we could use some help here|can i get some assistance here)[.! ]*$"),
    ("jump_pack", r"activating jump pack|^to the skies[.! ]*$", [], r"^airborne[.! ]*$"),
    ("harvest_resource", r"(?:saffron|zephron|zefron).*harvested|(?:legendary|rare).*acquired", [], r"^(got it|i got it|got it sir)[.! ]*$"),
    ("extraction_request", r"call(?:ing)? (?:in|it|an|it an) extraction|marking extraction point|extraction point (?:located|spotted)", [], r"^(let'?s get out of here|there'?s our ride|we should get out of here|deltas let'?s get out of here)[.! ]*$"),
]


def normalized(text: str) -> str:
    text = re.sub(r"^Delta\s+\d+:\s*", "", text, flags=re.I)
    text = unicodedata.normalize("NFKC", text).lower().replace("’", "'").replace("…", " ")
    text = re.sub(r"[^\w'\s]", " ", text)
    return " ".join(text.split())


def metadata(path: Path) -> dict[str, Any]:
    with wave.open(str(path)) as handle:
        return {"rc_duration_seconds": round(handle.getnframes() / handle.getframerate(), 6),
                "rc_sample_rate": handle.getframerate(), "rc_channels": handle.getnchannels(),
                "rc_sample_width": handle.getsampwidth()}


def build(args: argparse.Namespace) -> dict[str, Any]:
    root = args.workspace.resolve() / "dist/rc-upgrade"
    catalog = json.loads((root / "rc-delta-candidates.json").read_text(encoding="utf-8-sig"))
    objects = {clip["object"].lower(): clip for clip in catalog}
    source_info: dict[str, dict[str, Any]] = {}
    selectors: dict[str, list[str]] = {}
    memberships: dict[str, list[str]] = defaultdict(list)
    for selector in (root / "rc-selectors").glob("D*.t3d"):
        names = re.findall(r"Sounds\(\d+\)=Sound'([^']+)'", selector.read_text(encoding="utf-8-sig"))
        selectors[selector.stem] = [name.lower() for name in names if name.lower() in objects]
        for name in selectors[selector.stem]:
            memberships[name].append(selector.stem)
    for key, clip in objects.items():
        info = metadata(Path(clip["file"]))
        if info["rc_duration_seconds"] <= args.max_seconds:
            source_info[key] = info
    exact: dict[tuple[str, str], list[str]] = defaultdict(list)
    for key, clip in objects.items():
        if key not in source_info:
            continue
        for subtitle in clip.get("subtitles", []):
            phrase = normalized(subtitle["text"])
            if phrase and len(phrase.split()) <= 12:
                exact[(clip["character"], phrase)].append(key)
    slot_reports, unmapped, pool_report = [], [], []
    unique_sources: set[str] = set()
    for slot, character, prefix, bank, bank_hash in SLOTS:
        pools: dict[str, list[str]] = {}
        for semantic, _hd2, category_selectors, rc_pattern in RULES:
            pool = set()
            # Explicit matching short phrases are the least context-dependent choices.
            if rc_pattern:
                for (speaker, phrase), keys in exact.items():
                    if speaker == character and re.search(rc_pattern, phrase):
                        pool.update(keys)
            # Only use generic matching categories when phrase choices are unavailable;
            # categories also recover untranscribed non-verbal and combat takes.
            if not pool:
                for selector in category_selectors:
                    pool.update(selectors.get(prefix + "_" + selector, []))
            if semantic == "reload":
                reload_pool = set(selectors.get(prefix + "_Reloading", []))
                if reload_pool:
                    pool = reload_pool
            pool = {key for key in pool if key in source_info}
            # A recording referring to a specific squad member/planet is unsuitable for arbitrary HD2 events.
            filtered = set()
            for key in pool:
                texts = [normalized(s["text"]) for s in objects[key].get("subtitles", [])]
                if any(re.search(r"\b(?:fixer|scorch|sev|geonosis|kashyyyk|four oh|oh seven|six two|delta seven|delta forty|delta three eight)\b", t) for t in texts):
                    continue
                if any(len(t.split()) > 14 for t in texts):
                    continue
                filtered.add(key)
            pools[semantic] = sorted(filtered)
            pool_report.append({"slot": slot, "character": character, "semantic": semantic,
                                "objects": [objects[key]["object"] for key in pools[semantic]]})
        transcript = next((root / "hd2-voice-transcripts").glob(f"voice{slot}_*.csv"))
        rows = []
        with transcript.open(encoding="utf-8-sig", newline="") as handle:
            targets = list(csv.DictReader(handle))
        for target in targets:
            text = normalized(target["text"])
            options = exact.get((character, text), []) if text else []
            method, semantic = "exact_transcript", "exact"
            if not options:
                method = "explicit_semantic_rule"
                semantic = next((name for name, pattern, _s, _r in RULES if re.search(pattern, text, re.I)), "")
                options = pools.get(semantic, [])
            media_id = int(target["wem_short_id"])
            if not options:
                unmapped.append({"slot": slot, "character": character, "hd2_media_id": media_id,
                                 "hd2_text": target["text"], "semantic": semantic or None,
                                 "reason": "No suitable original character recording" if semantic else "No explicit semantic match",
                                 "fallback": "retain existing generic clone recording"})
                continue
            options = sorted(set(options))
            chosen = options[media_id % len(options)]
            clip = objects[chosen]
            wav_path = Path(clip["file"])
            digest = hashlib.sha256(wav_path.read_bytes()).hexdigest()
            if digest != clip["sha256"]:
                raise ValueError(f"Changed original RC WAV: {wav_path}")
            unique_sources.add(chosen)
            rows.append({"hd2_media_id": media_id,
                         "hd2_archive_resource_hash": f"{int(target['archive_file_id']):016x}",
                         "hd2_text": target["text"], "rc_wav": str(wav_path),
                         "rc_object": clip["object"], "rc_sha256": digest,
                         "rc_text": list(dict.fromkeys(s["text"] for s in clip.get("subtitles", []))),
                         "match_method": method, "semantic": semantic,
                         "rc_selector_provenance": memberships.get(chosen, []),
                         "source_pool": [objects[key]["object"] for key in options],
                         "review_note": "Explicit source semantics; HD2 community transcription and in-game timing need playback review.",
                         **source_info[chosen]})
        slot_reports.append({"slot": slot, "character": character, "bank_name": bank,
                             "bank_resource_hash": bank_hash, "language": "us",
                             "targets_in_transcript": len(targets), "mapped": len(rows),
                             "unmapped": len(targets) - len(rows), "rows": rows,
                             "semantic_counts": dict(Counter(row["semantic"] for row in rows))})
    return {"status": "Authoring map; current bank/layout and in-game playback validation required before deployment.",
            "scope": "Selectable voices: Sev1 Fixer2 Scorch3 Boss4; Republic Commando mode gating handled by app.",
            "semantic_source": "https://gist.github.com/trashguy/a25afb2612dd56c05ac61d6d8f40d579",
            "rules": [{"semantic": n, "hd2_pattern": p, "rc_selectors": s, "rc_pattern": r} for n, p, s, r in RULES],
            "slots": slot_reports, "unmapped": unmapped, "source_pools": pool_report,
            "unique_rc_recordings": len(unique_sources),
            "shared_standard_exertions": "Unmodified: per-slot attribution not established. Actor-bank injury calls may use explicit RC Hurt selector recordings."}


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--out-dir", type=Path)
    parser.add_argument("--max-seconds", type=float, default=4.5)
    parser.add_argument("-v", "--verbose", action="store_true")
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO)
    try:
        output = args.out_dir or args.workspace / "dist/rc-upgrade/voice-slots-map"
        output.mkdir(parents=True, exist_ok=True)
        result = build(args)
        (output / "mapping.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        for slot in result["slots"]:
            LOGGER.info("%s: %s/%s mapped", slot["character"], slot["mapped"], slot["targets_in_transcript"])
        LOGGER.info("%s unique original recordings; %s unmapped targets", result["unique_rc_recordings"], len(result["unmapped"]))
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, wave.Error):
        LOGGER.exception("RC semantic mapping failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
