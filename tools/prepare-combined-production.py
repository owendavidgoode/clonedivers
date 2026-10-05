#!/usr/bin/env python3
"""Prepare the pinned combined r20 release and native-selector profile sources.

Offline only: no downloads, publication, game/settings writes, or roster split.
All output is bounded to dist/production-r20-2026-10-05 and uses fresh folders.
The prepare step preserves every source row except its loopback URL. The
profiles step consumes an actual production selector export, never logical
patch-name guesses, and verifies each local source against the selected bytes.
"""

from __future__ import annotations

import argparse
from collections import defaultdict
from collections.abc import Sequence
from concurrent.futures import ThreadPoolExecutor
import copy
import hashlib
import json
import logging
import os
from pathlib import Path
import re
import shutil
import sys
from typing import Any
from urllib.parse import urlsplit
from xml.sax.saxutils import escape

LOGGER = logging.getLogger(__name__)
VERSION = "2026.10.05-r20"
TAG = f"pack-{VERSION}-files"
RELEASE_PREFIX = "https://github.com/owendavidgoode/clonedivers/releases/download/"
SOURCE_REL = "dist/empire-qa-integration-2026-10-05/candidate-v1/manifest.json"
SOURCE_SHA = "56f6b1f3e91c0b432f702a00cb16efedfec460def5608934ce366d1886d61716"
SOURCE_SELECTOR_SHA = "3ed917cc1b8cf2ef7cfa06caae23da69bdf26ca3b1c8b703c7ccef31c966e8b9"
PUBLIC_PINS = {
    "manifest.json": "cc8c0628d0f01ba981889183e9ce0eaf1fa5a264d2ce07e6cebb6b2f9bf7d211",
    "manifest-v3.json": "35c38e748db8ecc5006e0a74c8618e78f2654f9f915812def46934235e83e29f",
    "manifest-v3-current.json": "da6e2c7ce0a21de4b39f1bd237c18f52c1cbd596349a2000c1aa8f93a109e2c2",
}
EMPTY = hashlib.sha256(b"").hexdigest()
PATCH = re.compile(r"[a-f0-9]{16}\.patch_\d+(?:\.stream|\.gpu_resources)?", re.I)
SHA = re.compile(r"[a-f0-9]{64}")
NOTES = "Phase I Shiny — DP-8 helmet visibility confirmed. Combined Clonedivers/Commando/Empire roster retained; camera and new Empire gameplay remain experimental."
STATUS_NOTES = "Use launcher 1.7.3. First Person starts disabled on fresh installs; saved user choices are retained. Follow the session camera-bridge procedure in release notes. Mission drop, AT-TE camera/aim/restoration, and the included Empire gameplay checks remain open."
PUBLIC_NOTES = """# Clonedivers 1.7.3 / pack 2026.10.05-r20

Phase I Shiny — DP-8 adds the requested Phase I clone helmet choice. The owner
confirmed helmet/head visibility after the material dependency repair. Body fit,
animation, mission drop and AT-TE camera acceptance remain open.

This release retains the complete combined Clonedivers, Commando and Empire
roster, Full and Lighter profiles, existing options, equipment labels and audio.
It includes the reviewed Empire armor/weapon/ship updates, Lambda format/winding
corrections, five positive-weight damage-piece corrections and shared shield
relay routing. These structural checks do not establish gameplay acceptance.

## Experimental first person and the AT-TE camera

Use launcher **1.7.3** for the runtime bridge procedure. First Person starts
**disabled on a fresh installation**; existing saved user choices are retained.
The included First Person/menu/loader resources remain experimental.

1. Bind **ToggleFirstPerson** under **Options > Mouse & Keyboard > MODS > FIRSTPERSON**.
2. In each new session, on the ship, keep **Enabled Off**. Set **Engine camera
   bridge On > Apply > Off > Apply**. Leave **EXPERIMENT sync camera to game Off**.
3. Enable first person only after landing. Check solo on-foot view and aiming,
   then the EXO-45/AT-TE replacement. Check camera and head restoration after
   toggling off, exiting the vehicle, death, extraction and the next session.

Startup and ship entry passed on build **25480438**, with three addons loaded
and no addon failures. First person stayed disabled in that test. No mission
drop, corrected AT-TE camera, vehicle aim or restoration result is claimed.

## Gameplay limits still open

Empire checks remain open: exterior Star Destroyer hull, both Lambda contexts,
armor damage pieces, shield triggering/playback, HMG/Maxigun, voice slots,
mission drop/extraction and several Eagle attacks. The Death Trooper zero-weight
gib face, interceptor trigger, partial Lean coverage and Clone PCM cutscene
compatibility boundary remain unresolved. Helmet acceptance does not accept
these Empire changes or the experimental camera.

The pack preserves all **1,518** candidate rows and their mode/profile/option
conditions. Full and Lighter are the published texture choices. This promotion
changes release metadata and private asset URLs; game payload bytes are unchanged.

Source credits include [Clone Armory](https://www.nexusmods.com/helldivers2/mods/13956),
[First Person](https://www.nexusmods.com/helldivers2/mods/16813), the Bingus loader
and menu dependencies, and the existing combined roster authors recorded in
[MODS.md](https://github.com/owendavidgoode/clonedivers/blob/main/docs/MODS.md).
"""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def pin(path: Path) -> dict[str, Any]:
    before = path.stat()
    require(path.is_file() and not path.is_symlink(), "Regular local file required: " + str(path))
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    after = path.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns),
            "File changed during verification: " + str(path))
    return {"path": str(path.resolve()), "bytes": after.st_size, "sha256": digest}


def canonical_sha(value: Any) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"),
                                    ensure_ascii=False).encode()).hexdigest()


def bound_output(workspace: Path, path: Path, *, fresh: bool) -> Path:
    bound = (workspace / "dist/production-r20-2026-10-05").resolve()
    path = path.resolve()
    require(path.is_relative_to(bound) and path != bound, "Output outside the assigned production area")
    require(not path.exists() if fresh else path.is_dir(), "Fresh/existing output requirement differs: " + str(path))
    return path


def link_verified(source: Path, destination: Path, expected: dict[str, Any]) -> dict[str, Any]:
    proof = pin(source)
    require(proof["sha256"] == expected["sha256"] and proof["bytes"] == expected["size"],
            "Source payload differs: " + str(source))
    require(not destination.exists(), "Destination already exists: " + str(destination))
    try:
        os.link(source, destination)
        method = "same-volume-hardlink"
    except OSError:
        shutil.copyfile(source, destination)
        method = "verified-byte-copy"
    output_pin = pin(destination)
    require(output_pin["sha256"] == expected["sha256"] and output_pin["bytes"] == expected["size"],
            "Staged payload differs: " + str(destination))
    return {"source": proof, "destination": output_pin, "method": method}


def public_assets(workspace: Path) -> tuple[dict[str, set[str]], list[dict[str, Any]]]:
    known: dict[str, set[str]] = defaultdict(set)
    sizes: dict[str, int] = {}
    witnesses = []
    for relative, expected_sha in PUBLIC_PINS.items():
        path = workspace / relative
        witness = pin(path)
        require(witness["sha256"] == expected_sha, "Public feed baseline changed: " + relative)
        witnesses.append(witness)
        for row in read_json(path)["pack"]["files"]:
            if not row["size"]:
                continue
            require(SHA.fullmatch(row["sha256"]) is not None and row["url"].startswith(RELEASE_PREFIX),
                    "Invalid previously hosted pack identity")
            parsed = urlsplit(row["url"])
            require(not parsed.query and not parsed.fragment and parsed.path.endswith("/" + row["sha256"]),
                    "Previously hosted asset is not content addressed")
            require(row["sha256"] not in sizes or sizes[row["sha256"]] == row["size"],
                    "Public hash has conflicting sizes")
            sizes[row["sha256"]] = row["size"]
            known[row["sha256"]].add(row["url"])
    return known, witnesses


def source_manifest(workspace: Path) -> tuple[dict[str, Any], dict[str, Any]]:
    path = workspace / SOURCE_REL
    proof = pin(path)
    require(proof["sha256"] == SOURCE_SHA, "Combined candidate SHA256 differs")
    value = read_json(path)
    require(value["format"] == 3 and value["pack"]["combinedRoster"]
            and value["pack"]["gameBuild"] == "25480438"
            and len(value["pack"]["files"]) == 1518, "Combined candidate scope differs")
    require(sum(row["size"] for row in value["pack"]["files"]) == value["pack"]["totalSize"],
            "Combined total size differs")
    return value, proof


def make_native_project(workspace: Path, out: Path, checkout: Path) -> None:
    require((checkout / "Clonedivers/Program.cs").is_file(), "Native checkout missing")
    native = out / "native-selector"
    native.mkdir()
    check = workspace / "tools/EmpireHolistic.Check/Check.cs"
    shutil.copyfile(check, native / "Check.cs")
    profile_check = workspace / "tools/Profile.Release.Check/Check.cs"
    shutil.copyfile(profile_check, native / "ProfileCheck.cs")
    includes = escape(str(checkout.resolve() / "Clonedivers/*.cs"), {'"': "&quot;"})
    for name, startup in (("NativeSelector", "EmpireHolisticCheck"), ("ProfileCheck", "ProfileReleaseCheck")):
        local_source = "Check.cs" if name == "NativeSelector" else "ProfileCheck.cs"
        (native / (name + ".csproj")).write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms>'
            '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
            '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
            f'<StartupObject>{startup}</StartupObject></PropertyGroup>'
            f'<ItemGroup><Compile Include="{local_source}" />'
            f'<Compile Include="{includes}" Link="%(Filename)%(Extension)" /></ItemGroup></Project>\n',
            encoding="utf-8")
    (native / "Directory.Build.props").write_text(
        '<Project><PropertyGroup><BaseIntermediateOutputPath>obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>'
        '<MSBuildProjectExtensionsPath>obj/$(MSBuildProjectName)/</MSBuildProjectExtensionsPath>'
        '<BaseOutputPath>bin/$(MSBuildProjectName)/</BaseOutputPath></PropertyGroup></Project>\n', encoding="utf-8")
    (native / "NuGet.Config").write_text(
        '<configuration><packageSources><clear /></packageSources></configuration>\n', encoding="utf-8")
    write_json(native / "source-pins.json", {
        "nativeCheckout": str(checkout.resolve()), "exporter": pin(check), "profileChecker": pin(profile_check),
        "launcherSources": [pin(path) for path in sorted((checkout / "Clonedivers").glob("*.cs"))],
        "gameOrSettingsChanged": False,
    })


def prepare(args: argparse.Namespace) -> None:
    workspace = args.workspace.resolve()
    out = bound_output(workspace, args.out, fresh=True)
    source, source_pin = source_manifest(workspace)
    known, public_pins = public_assets(workspace)
    after = copy.deepcopy(source)
    assets: dict[str, dict[str, Any]] = {}
    changed_rows = []
    reused = set()
    for index, row in enumerate(after["pack"]["files"]):
        require(PATCH.fullmatch(row["name"]) is not None and SHA.fullmatch(row["sha256"]) is not None,
                "Invalid source row")
        if not row["size"]:
            require(row["sha256"] == EMPTY and row["url"] == "", "Empty source row differs")
            continue
        parsed = urlsplit(row["url"])
        require(not parsed.query and not parsed.fragment, "Source URL has query/fragment")
        if parsed.hostname == "127.0.0.1" and parsed.scheme == "http":
            require(parsed.path == "/assets/" + row["sha256"], "Unexpected private URL path")
            require(row["sha256"] not in known, "Private source unexpectedly duplicates a hosted public hash")
            old_url = row["url"]
            row["url"] = RELEASE_PREFIX + TAG + "/" + row["sha256"]
            assets.setdefault(row["sha256"], {"sha256": row["sha256"], "size": row["size"]})
            require(assets[row["sha256"]]["size"] == row["size"], "Conflicting private hash sizes")
            changed_rows.append({"rowIndex": index, "name": row["name"], "size": row["size"],
                                 "sha256": row["sha256"], "oldUrl": old_url, "newUrl": row["url"],
                                 "selectionGates": {key: copy.deepcopy(row[key]) for key in
                                                    ("option", "unlessOption", "modes", "textureProfiles") if key in row}})
        else:
            require(row["sha256"] in known and row["url"] in known[row["sha256"]],
                    "Source public URL is not present in the pinned published feeds")
            reused.add(row["sha256"])
    require(len(assets) == 60 and len(changed_rows) == 61 and len(reused) == 805,
            "Reviewed new/reused asset counts differ")
    require(sum(row["size"] for row in assets.values()) == 1028484966, "Upload byte count differs")
    after["pack"].update(version=VERSION, notes=NOTES, statusNotes=STATUS_NOTES, isPublished=True)
    restored = copy.deepcopy(after)
    for key in ("version", "notes", "statusNotes", "isPublished"):
        restored["pack"][key] = source["pack"][key]
    for row, original in zip(restored["pack"]["files"], source["pack"]["files"], strict=True):
        row["url"] = original["url"]
    require(restored == source, "Unauthorized metadata/row/gate/payload change")
    require(all(not row["size"] or row["url"].startswith(RELEASE_PREFIX)
                for row in after["pack"]["files"]), "Private URL remains")
    out.mkdir(parents=True)
    asset_dir = out / "assets"
    asset_dir.mkdir()
    copies = []
    for index, row in enumerate(sorted(assets.values(), key=lambda item: item["sha256"]), 1):
        digest = row["sha256"]
        copies.append(link_verified((workspace / SOURCE_REL).parent / "assets" / digest,
                                    asset_dir / digest, row))
        if index % 15 == 0:
            LOGGER.info("Verified %d/60 new release assets", index)
    write_json(out / "manifest.json", after)
    (out / "notes.md").write_text(PUBLIC_NOTES, encoding="utf-8")
    plan = [{"sha": row["sha256"], "size": row["size"],
             "localPath": str((asset_dir / row["sha256"]).resolve()), "tag": TAG}
            for row in sorted(assets.values(), key=lambda item: item["sha256"])]
    write_json(out / "upload-plan.json", plan)
    source_selector = (workspace / SOURCE_REL).parent / "production96.json"
    require(pin(source_selector)["sha256"] == SOURCE_SELECTOR_SHA, "Source native selector receipt changed")
    if args.native_checkout:
        make_native_project(workspace, out, args.native_checkout)
    write_json(out / "source-promotion-report.json", {
        "passed": True, "version": VERSION, "sourceManifest": source_pin,
        "publicFeedPins": public_pins, "publicUniqueHashCatalog": len(known),
        "manifest": pin(out / "manifest.json"), "notes": pin(out / "notes.md"),
        "uploadPlan": pin(out / "upload-plan.json"), "compiler": pin(Path(__file__)),
        "sourceProduction96": pin(source_selector),
        "rows": 1518, "rowsPreservedInOriginalOrder": True, "payloadHashesSizesAndGatesExact": True,
        "totalSizeIsPerFileCombinedRosterAndAppExact": True, "empireIncluded": True,
        "metadataChanges": ["pack.version", "pack.notes", "pack.statusNotes", "pack.isPublished"],
        "rowChanges": changed_rows, "loopbackRowCount": 61, "newUniqueHashes": 60,
        "newAssetBytes": sum(row["size"] for row in assets.values()), "reusedHostedUniqueHashes": len(reused),
        "everyExistingGitHubUrlExact": True, "privateOrSignedUrlsInCandidate": False,
        "allSixtySourcesAndDestinationsFreshlyVerified": True, "assets": copies,
        "profileSourcesPrepared": False, "gameOrSettingsChanged": False,
        "uploaded": False, "published": False,
    })
    LOGGER.info("Prepared %s: 1,518 rows, 60 new hashes, 1,028,484,966 bytes", out)


def normalize(value: Any) -> Any:
    if isinstance(value, dict):
        return {key[:1].lower() + key[1:]: normalize(item) for key, item in value.items()}
    if isinstance(value, list):
        return [normalize(item) for item in value]
    return value


def payload_identity(row: dict[str, Any]) -> dict[str, Any]:
    return {key: row.get(key) for key in ("name", "size", "sha256", "option", "unlessOption", "modes", "textureProfiles")}


def profiles(args: argparse.Namespace) -> None:
    workspace = args.workspace.resolve()
    out = bound_output(workspace, args.out, fresh=False)
    profile_out = bound_output(workspace, out / "profiles", fresh=True)
    report = read_json(out / "source-promotion-report.json")
    candidate_pin = pin(out / "manifest.json")
    require(candidate_pin == report["manifest"] and report["passed"], "Promotion inputs changed")
    candidate = read_json(out / "manifest.json")
    selector_pin = pin(args.selector)
    selector = normalize(read_json(args.selector))
    require(selector["passed"] and selector["manifestSha256"] == candidate_pin["sha256"]
            and selector["requestedStates"] == 96 and selector["uniqueFileSets"] == 16
            and selector["productionSelector"] == "LauncherModes.OptionsFor + Pack.EffectiveFiles",
            "Fresh r20 native selector export required")
    source_selector_path = (workspace / SOURCE_REL).parent / "production96.json"
    require(pin(source_selector_path)["sha256"] == SOURCE_SELECTOR_SHA, "Source selector pin changed")
    source_selector = normalize(read_json(source_selector_path))
    def state_key(row: dict[str, Any]) -> str:
        return canonical_sha({key: row[key] for key in ("profile", "mode", "requestedOptions")})
    old_states = {state_key(row): row for row in source_selector["selections"]}
    require(len(old_states) == 96, "Source native states are not unique")
    for state in selector["selections"]:
        previous = old_states[state_key(state)]
        require(state["effectiveOptions"] == previous["effectiveOptions"], "Production flags changed")
        require([payload_identity(row) for row in selector["fileSets"][state["fileSet"]]] ==
                [payload_identity(row) for row in source_selector["fileSets"][previous["fileSet"]]],
                "Native physical selection/payload/gates changed")
    wanted: dict[str, list[dict[str, Any]]] = {}
    chosen_states = []
    for profile in ("full", "lighter"):
        matches = [state for state in selector["selections"] if state["profile"] == profile
                   and state["mode"] == "CommandoDivers" and state["requestedOptions"] ==
                   {"droids": True, "covenant": False, "commandos": True, "aimpoints": True}]
        require(len(matches) == 1, "Native default profile source selection is not unique")
        wanted[profile] = selector["fileSets"][matches[0]["fileSet"]]
        require(len({row["name"] for row in wanted[profile]}) == len(wanted[profile]), "Duplicate physical name")
        require(all(PATCH.fullmatch(row["name"]) is not None for row in wanted[profile]), "Bad native physical name")
        chosen_states.append(matches[0])
    required: dict[str, int] = {}
    for rows in wanted.values():
        for row in rows:
            require(row["sha256"] not in required or required[row["sha256"]] == row["size"], "Selected hash size conflict")
            required[row["sha256"]] = row["size"]
    sources: dict[str, dict[str, Any]] = {}
    checked = []
    # These are bounded, named local release/profile locations. No disk-wide search.
    game = args.game.resolve()
    folders = [out / "assets", game / "mods_download", game / "data",
               workspace / "dist/pack-optimized-current"]
    for release in ("release-r14-inputs", "release-r13-inputs", "release-r12-inputs", "release-r11-inputs"):
        folders.extend(workspace / "dist" / release / "profiles" / profile for profile in ("full", "lighter"))
        folders.append(workspace / "dist" / release / "assets")
    folders.extend(workspace / "dist" / release / "assets" for release in
                   ("release-ready-r19", "release-ready-r18", "release-ready-r17", "release-ready-r16"))
    scanned = []
    for folder in folders:
        if not folder.is_dir():
            scanned.append({"directory": str(folder), "exists": False, "hashedFiles": 0})
            continue
        require(not folder.is_symlink(), "Symlinked profile/cache folder is outside this inventory contract")
        missing_sizes = {size for digest, size in required.items() if digest not in sources and size > 0}
        if not missing_sizes:
            break
        paths = sorted(path for path in folder.iterdir() if path.is_file() and not path.is_symlink()
                       and (PATCH.fullmatch(path.name) or SHA.fullmatch(path.name))
                       and path.stat().st_size in missing_sizes)
        with ThreadPoolExecutor(max_workers=4) as pool:
            proofs = list(pool.map(pin, paths))
        matched = 0
        for proof in proofs:
            checked.append(proof)
            digest = proof["sha256"]
            if digest in required and required[digest] == proof["bytes"] and digest not in sources:
                sources[digest] = proof
                matched += 1
        scanned.append({"directory": str(folder.resolve()), "exists": True,
                        "hashedFiles": len(proofs), "newRequiredHashes": matched})
        LOGGER.info("Local inventory %s: %d files hashed, %d required hashes found; %d remain",
                    folder.name, len(proofs), matched, sum(size > 0 and digest not in sources for digest, size in required.items()))
    missing = [{"sha256": digest, "size": size,
                "physicalTargets": [{"profile": profile, "name": row["name"]}
                                    for profile, rows in wanted.items() for row in rows if row["sha256"] == digest]}
               for digest, size in required.items() if size > 0 and digest not in sources]
    write_json(out / "profile-source-inventory.json", {
        "passed": not missing, "manifest": candidate_pin, "selector": selector_pin,
        "all96PayloadSelectionsAndFlagsMatchPinnedCombinedSource": True,
        "chosenNativeStates": chosen_states, "boundedFolders": scanned,
        "verifiedLocalFiles": checked, "requiredUniqueNonemptyHashes": sum(size > 0 for size in required.values()),
        "missing": missing, "networkUsed": False, "gameOrSettingsChanged": False,
    })
    require(not missing, f"Local profile sources missing {len(missing)} hashes; see profile-source-inventory.json")
    profile_out.mkdir()
    profile_records = []
    directories = []
    for profile, rows in wanted.items():
        folder = profile_out / profile
        folder.mkdir()
        files = []
        for row in rows:
            target = folder / row["name"]
            if row["size"]:
                proof = link_verified(Path(sources[row["sha256"]]["path"]), target, row)
            else:
                require(row["sha256"] == EMPTY, "Selected empty identity differs")
                target.write_bytes(b"")
                proof = {"source": None, "destination": pin(target), "method": "verified-empty"}
            files.append({"physicalName": row["name"], "expected": payload_identity(row), **proof})
        require({path.name for path in folder.iterdir()} == {row["name"] for row in rows}, "Profile membership differs")
        directories.append({"id": profile, "directory": str(folder.resolve())})
        profile_records.append({"id": profile, "fileCount": len(rows), "bytes": sum(row["size"] for row in rows),
                                "everyPhysicalTargetFreshlyVerified": True, "files": files})
        LOGGER.info("Verified profile %s: %d physical targets", profile, len(rows))
    write_json(out / "profile-directories.json", directories)
    write_json(out / "profile-sources-report.json", {
        "passed": True, "manifest": candidate_pin, "sourcePromotionReport": pin(out / "source-promotion-report.json"),
        "nativeSelector": selector_pin, "sourceSelector": pin(source_selector_path),
        "all96PayloadSelectionsAndFlagsExact": True, "directorySpec": pin(out / "profile-directories.json"),
        "inventory": pin(out / "profile-source-inventory.json"), "profiles": profile_records,
        "physicalNamesComeOnlyFromProductionSelector": True, "allSourceAndDestinationHashesVerified": True,
        "networkUsed": False, "gameOrSettingsChanged": False,
    })


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("-v", "--verbose", action="count", default=0)
    sub = parser.add_subparsers(dest="command", required=True)
    prepare_parser = sub.add_parser("prepare")
    prepare_parser.add_argument("--out", type=Path, required=True)
    prepare_parser.add_argument("--native-checkout", type=Path)
    profiles_parser = sub.add_parser("profiles")
    profiles_parser.add_argument("--out", type=Path, required=True)
    profiles_parser.add_argument("--selector", type=Path, required=True)
    profiles_parser.add_argument("--game", type=Path, default=Path("C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2"))
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    logging.basicConfig(level=logging.INFO if args.verbose else logging.WARNING, format="%(levelname)s: %(message)s")
    try:
        if args.command == "prepare":
            prepare(args)
        else:
            profiles(args)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError, TypeError) as error:
        LOGGER.error("%s", error)
        return 1


if __name__ == "__main__":
    sys.exit(main())
