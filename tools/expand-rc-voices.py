#!/usr/bin/env python3
"""Build a staged, subtitle-backed expansion without changing the shipped RC recipe."""

import argparse
import copy
import csv
import hashlib
import importlib.util
import json
import re
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


# Full phrase matches only. Optional vocatives do not remove named squad members.
VOCATIVE = r"(?: (?:sir|delta|deltas|squad|squad leader|commando|commandos))?"
PHRASES = {
    "affirmative": r"affirmative|yes|yeah|got it|you got it|roger that|roger that order|confirmed|confirm|understood|acknowledged|copy that|as you wish|aye|aye aye|i can do that|consider it done|got your order|good idea|i'm all over it|we'll get right on that",
    "negative": r"negative|negative on that|no|can't do that|unable to comply|i can't comply|order respectfully declined|that can't be done|that command makes no sense",
    "cancel": r"cancel that(?: order| commando)?|disregard that|never mind|belay that order|cancel maneuver|ignore that command|maneuver aborted|yes sir canceling maneuver|yes sir aborting maneuver|aye aye canceling|canceling order|understood aborting maneuver",
    "thanks": r"thanks|thank you|thanks for the help|thanks for the assist|i appreciate the assist|yes sir thank you sir",
    "apology": r"sorry|oh sorry|my mistake|my fault",
    "follow": r"form up|let's form up|delta form up|form up squad|form up deltas|follow me|on me",
    "hold_position": r"hold (?:your |that )?position|holding (?:this position|here)|defend this position|wait here|don't move|sit tight|hold up",
    "retreat": r"fall back(?: move it)?|retreat(?: quickly| blast it)?|abandon position delta|let's get out of here|time to depart team|time to back off|it's time to pull out|back it off team|we need to get out of here fast|get out of there|getting out of the firefight|i'm pulling out",
    "moving": r"on my way|moving on|moving into position|moving to (?:your mark|position|your position)|getting into position|moving off|moving in|i'm heading there now",
    "reload": r"reloading|need a reload here|i'm out reloading|cover me",
    "out_of_ammo": r"i'm out reloading|out of ammo|ammunition exhausted|ammunition exhausted and so am i",
    "throw_grenade": r"grenade thrown|grenade live|grenade|fire in the hole|throwing grenade|grenade out",
    "low_health": r"i need bacta|could use some bacta sir|need aid now|i need help commandos|need medical assistance|need meds|need meds now|i'm running low on life here|can't take any more|can't take anymore|i'm wounded",
    "heal_other": r"administering bacta|give him some bacta|assisting you|helping you|i'm here to help|let me help you",
    "heal_self": r"bacta applied|bacta taken|i'm healed|nothing like a little bacta|that was close|i'm healthy now|feeling much better now|all patched up here|feeling good|i'm good as new|i'm good let's go|ah much better|feeling all right now|commando healed|rule (?:39|thirty nine) never say no to bacta",
    "terminal": r"slicing (?:in )?now|slicing (?:in )?now this will take (?:a minute|a while)|just a quick slice|just have to override the access restrictions",
    "completed": r"done|all done|complete|it's done|that's done|that did the trick|all set",
    "spawn_ready": r"ready to engage|ready for action|ready for battle|weapon ready|i'm ready|i'm ready to go",
    "attack": r"engage the enemy|assault their position|eliminate (?:opposition|target)|take (?:them|'em|em) out|engaging target|die|die already|you're mine|eat that|let's show (?:them|'em|em) some real firepower|time to make a little mess of my own|i'll blast them to bits|let's hit them|enough of this|fire fire",
    "enemy_droid": r"droids|enemy droids|clankers|found enemy droids|found confederacy droids|confederacy droids located|visual lock droids|federation droids here",
    "enemy_generic": r"enemy spotted|contact|hostiles|enemy sighted|marks sighted|hostiles spotted|got some unfriendlies here|we got a non friendly here|enemies within visual range|enemy detected|enemy units|targets confirmed|targets in view|hostiles ahoy|marks nearby|located marks|got some marks ahead|visual lock enemy units|got a visual on some enemies|unknown target ahead",
    "enemy_bugs": r"bugs|bugs ahead|more bugs|got a visual on some bugs|getting a good look at some crawlies",
    "enemy_incoming": r"multiple enemies incoming|enemy reinforcements incoming|they're bringing in reinforcements|here they come(?: again)?",
    "danger": r"incoming|incoming fire|look out|take cover|heads up",
    "demolition_warning": r"stand back|take cover|fire in the hole|get clear|delta squad clear the area|it's live get some distance|watch my blast",
    "visibility_low": r"visibility low|view obstructed",
}
PHRASES["affirmative"] += r"|i'm on it|on it|no problem"
PHRASES["retreat"] += r"|team take off|take off|move it back|we've got to pull out damn|i gotta get out of here"
PHRASES["heal_other"] += r"|commando receiving assistance|giving some help to a squad mate|lending a hand|need a hand|we ain't got time to bleed"
PHRASES["heal_self"] += r"|condition nominal|feeling better now|feeling better|i'm good to go|that did the job|i feel better now|i needed that|much better|that helped|that's just what i needed"
PHRASES["spawn_ready"] += r"|good to go|i'm up for action|ready to go|set for battle|back for more|can't keep a good clone down|set for action|i'm good to go|i'm ready for battle|let's get going|let's go|ready to engage the enemy|returning for duty|set for combat"
PHRASES["enemy_droid"] += r"|confederate droids encountered|droid detected|droid|droids ahead|droids encountered|enemy droid targets ahead|got a visual on enemy droids|got a visual on some mechanicals|got some mechanicals ahead|located some enemy droids|look sir droids|these must be the droids we're looking for|we've got droids|bolt bags ahead|clankers ahead|fedbots|federation property spotted|got an enemy droid visual here|got some droids here"
PHRASES["enemy_generic"] += r"|enemies dead ahead|enemies encountered|enemies in sight|enemy units straight ahead|hostile detected|targets ahead|bad guys ahead|we've got bogeys|we got company"
PHRASES["throw_grenade"] += r"|airborne|clear|detonator|live one|blast out|charge out|look alive|thermal ahoy|thermal away|throwing charge|hot meal|got a hot one here|hot one|take that|tossing a charge"
PHRASES["terminal"] += r"|on it|i'm on it|no problem|watch the master at work|this'll be easy|working"
PHRASES["attack"] += r"|die screaming|rule (?:1|one) kill 'em before they kill you"
PHRASES["moving_order"] = r"move|move move move|go|go go|go go go|move out|let's move on|let's move|keep moving"
PHRASES["injury"] = r"i've got some damage here|took some fire|wounded delta here|i've been hit|they got me|took a blast here|wounded but still kicking|battle damage over here|i took some fire|i'm hit|i've taken fire"
PHRASES["battle_cry"] = PHRASES["attack"]
PHRASES["acknowledge_selection"] = PHRASES["affirmative"]
NAMED = re.compile(r"\b(?:fixer|scorch|sev|boss|captain|geonosis|kashyyyk|three eight|four oh|oh seven|six two|four two|delta (?:seven|forty)|38|07|40|62)\b")


def write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def build(root: Path, output: Path) -> dict[str, Any]:
    spec = importlib.util.spec_from_file_location("original_mapper", root / "tools/rc-voice-map.py")
    assert spec and spec.loader
    original = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(original)
    norm = original.normalized
    baseline_path = root / "build-inputs/rc/voice-mapping.json"
    baseline_bytes = baseline_path.read_bytes()
    baseline = json.loads(baseline_bytes)
    audits = [json.loads(line) for line in (root / "dist/rc-audio-review-2026-09-22/clips.jsonl").read_text(encoding="utf-8").splitlines()]
    catalog = json.loads((root / "dist/rc-upgrade/rc-delta-candidates.json").read_text(encoding="utf-8-sig"))
    clips = {clip["sha256"]: clip for clip in reversed(catalog)}
    memberships: dict[str, list[str]] = defaultdict(list)
    for path in sorted((root / "dist/rc-upgrade/rc-selectors").glob("D*.t3d")):
        for name in re.findall(r"Sounds\(\d+\)=Sound'([^']+)'", path.read_text(encoding="utf-8-sig")):
            memberships[name.lower()].append(path.stem)
    old_sources = {row["rc_sha256"] for slot in baseline["slots"] for row in slot["rows"]}
    pools: dict[tuple[str, str], set[str]] = defaultdict(set)
    # Keep the proven nonverbal Hurt-selector takes alongside new injury dialogue.
    for slot in baseline["slots"]:
        for row in slot["rows"]:
            if row["semantic"] == "injury":
                pools[slot["character"], "injury"].add(row["rc_sha256"])
    dispositions = []
    audit_by_hash = {audit["sha256"]: audit for audit in audits}
    for audit in audits:
        sha = audit["sha256"]
        texts = [norm(text) for text in audit["subtitle_references"]]
        reason = "no approved event phrase"
        accepted = []
        safe = not any(NAMED.search(text) for text in texts)
        quality = (audit["subtitle_word_similarity"] or 0) >= .8 and not set(audit["flags"]) & {"uncertain_recognition", "no_speech_recognized"}
        if not safe:
            reason = "named character or setting"
        elif audit["duration_seconds"] > 4.5 or any(len(text.split()) > 14 for text in texts):
            reason = "long callout"
        elif not quality and sha not in old_sources:
            reason = "requires recognition or listening review"
        else:
            for semantic, pattern in PHRASES.items():
                if any(re.fullmatch(f"(?:{pattern}){VOCATIVE}", text) for text in texts):
                    pools[audit["character"], semantic].add(sha)
                    accepted.append(semantic)
            if accepted:
                reason = "approved phrase pool"
        dispositions.append({"sha256": sha, "character": audit["character"], "text": audit["best_subtitle"], "decision": reason, "eligible_events": accepted})

    rules = [(name, pattern) for name, pattern, _, _ in original.RULES]
    rules = [
        ("visibility_low", r"^visibility decreasing$"),
        ("moving_order", r"^move(?: move move)?$"),
        ("enemy_bugs", r"^bugs$"),
        ("danger", r"^danger$|orbital (?:incoming|inbound)"),
        ("low_health", r"losing so much blood|could truly use a (?:stim|stem)"),
        ("battle_cry", r"^whatever it takes$|^i will protect democracy at all costs$"),
    ] + rules
    result = copy.deepcopy(baseline)
    result.update(status="Staged expansion; archive verified separately; in-game playback pending.", rules=[{"semantic": name, "hd2_pattern": pattern} for name, pattern in rules], source_pools=[])
    result["unmapped"] = []
    changes = []
    for slot in result["slots"]:
        character = slot["character"]
        old_rows = {row["hd2_media_id"]: row for row in slot["rows"]}
        transcript = next((root / "dist/rc-upgrade/hd2-voice-transcripts").glob(f"voice{slot['slot']}_*.csv"))
        with transcript.open(encoding="utf-8-sig", newline="") as handle:
            targets = list(csv.DictReader(handle))
        assignments = []
        rows = []
        usage: Counter[str] = Counter()
        for target in targets:
            media = int(target["wem_short_id"])
            old = old_rows.get(media)
            text = norm(target["text"])
            semantic = next((name for name, pattern in rules if re.search(pattern, text)), "")
            options = sorted(pools[character, semantic])
            # Bug calls may use a species-neutral enemy call if no bug phrase exists.
            if semantic == "enemy_bugs" and not options:
                options = sorted(pools[character, "enemy_generic"])
            if options:
                assignments.append((target, semantic, options))
            elif old and not any(NAMED.search(norm(t)) for t in old["rc_text"]) and semantic not in {"battle_cry", "attack", "terminal", "completed", "enemy_incoming", "danger"}:
                rows.append(copy.deepcopy(old))
                usage[old["rc_sha256"]] += 1
            else:
                result["unmapped"].append({"slot": slot["slot"], "character": character, "hd2_media_id": media, "hd2_text": target["text"], "semantic": semantic or None, "reason": "No approved context-matching recording", "fallback": "retain existing generic clone recording"})
        # Allocate constrained pools first, then use the least-used recording globally.
        # Existing assignments win ties, minimizing churn without starving new sources.
        for target, semantic, options in sorted(assignments, key=lambda item: (len(item[2]), int(item[0]["wem_short_id"]))):
            media = int(target["wem_short_id"])
            old = old_rows.get(media)
            chosen = min(options, key=lambda sha: (usage[sha], sha != (old or {}).get("rc_sha256"), sha))
            clip = clips[chosen]
            wav = Path(clip["file"])
            if hashlib.sha256(wav.read_bytes()).hexdigest() != chosen:
                raise ValueError(f"Source changed: {wav}")
            rows.append({"hd2_media_id": media, "hd2_archive_resource_hash": f"{int(target['archive_file_id']):016x}", "hd2_text": target["text"], "rc_wav": str(wav.relative_to(root)), "rc_object": clip["object"], "rc_sha256": chosen, "rc_text": [sub["text"] for sub in clip["subtitles"]], "match_method": "reviewed_phrase_balanced_allocation", "semantic": semantic, "rc_selector_provenance": memberships[clip["object"].lower()], "source_pool": [clips[sha]["object"] for sha in options], "review_note": "Original subtitle plus automated recognition gate; full phrase match to curated event; runtime timing untested.", **original.metadata(wav)})
            usage[chosen] += 1
        rows.sort(key=lambda row: row["hd2_media_id"])
        slot.update(rows=rows, mapped=len(rows), unmapped=len(targets)-len(rows), semantic_counts=dict(Counter(row["semantic"] for row in rows)))
        new_rows = {row["hd2_media_id"]: row for row in rows}
        for media in sorted(set(old_rows) | set(new_rows)):
            old, new = old_rows.get(media), new_rows.get(media)
            if (old or {}).get("rc_sha256") != (new or {}).get("rc_sha256"):
                changes.append({"character": character, "media_id": media, "hd2_text": (new or old)["hd2_text"], "change": "added coverage" if not old else "restored clone fallback" if not new else "different recording", "old_sha256": (old or {}).get("rc_sha256"), "new_sha256": (new or {}).get("rc_sha256"), "new_text": (new or {}).get("rc_text")})
    used = {row["rc_sha256"] for slot in result["slots"] for row in slot["rows"]}
    result["unique_rc_recordings"] = len(used)
    for disposition in dispositions:
        disposition["used_in_patch"] = disposition["sha256"] in used
        disposition["previously_shipped"] = disposition["sha256"] in old_sources
        if disposition["used_in_patch"] and not disposition["eligible_events"]:
            disposition["decision"] = "retained baseline recording; not newly approved"
    for (character, semantic), hashes in sorted(pools.items()):
        if hashes:
            result["source_pools"].append({"character": character, "semantic": semantic, "sha256": sorted(hashes)})
    summary = {"baseline_mapping_sha256": hashlib.sha256(baseline_bytes).hexdigest(), "audited_unique_recordings": len(audits), "before_unique": len(old_sources), "after_unique": len(used), "new_recordings": len(used-old_sources), "retired_recordings": len(old_sources-used), "before_mapped": sum(s["mapped"] for s in baseline["slots"]), "after_mapped": sum(s["mapped"] for s in result["slots"]), "changes": dict(Counter(change["change"] for change in changes)), "slots": [{"character": slot["character"], "mapped": slot["mapped"], "fallback": slot["unmapped"], "unique": len({row["rc_sha256"] for row in slot["rows"]})} for slot in result["slots"]], "runtime_tested": False, "loudness_changed": False}
    output.mkdir(parents=True, exist_ok=True)
    write_json(output / "mapping.json", result)
    write_json(output / "summary.json", summary)
    write_json(output / "changes.json", changes)
    write_json(output / "catalog-disposition.json", dispositions)
    write_json(output / "new-recordings.json", [{"character": audit_by_hash[sha]["character"], "sha256": sha, "text": audit_by_hash[sha]["best_subtitle"], "events": sorted({row["semantic"] for slot in result["slots"] for row in slot["rows"] if row["rc_sha256"] == sha})} for sha in sorted(used-old_sources)])
    if baseline_path.read_bytes() != baseline_bytes:
        raise ValueError("Shipped mapping changed during expansion")
    return summary


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.workspace.resolve(), args.out), indent=2))


if __name__ == "__main__":
    main()
