#!/usr/bin/env python3
"""Seal the LEGO body identity and independent offline Yoda feature reviews."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AREA = ROOT / "dist/empire-yoda-death-2026-10-08/armor"
EXPECTED = {
    "inventory-v1": "22967e062cd86601ef9ad54afa9534518699b4bac89140ddfe2498231369e7b5",
    "native-kits-v1": "59abe1cf3b093dc5f3c4a8ad4cb93ebb67965be2d9db9fe416a473701b7e3c74",
    "identity-peer-v1": "49298e4e4e197edb144457dc0facd09252e41241ed72e12b43e1be703729839d",
    "packages-v1": "7d210eb612e6fc22c26ca330267f296c04d0274bb6c251779e4ade24948524e6",
    "current-category-v1": "d08fc132c3eb4590e32de9a1fea6f07ce31563ac7774fcd523745ad2294ebb11",
    "runtime-peer-v1b": "eb56d220ca1951cbfb3b643691c7a0f6a92d943ba2feeb06c988303b78e68acf",
    "runtime-peer-v2": "cd2f30ae8c1606a2bf3effc45822edc327f698f9ae824809ff44c09edcd1c5d6",
    "audio-peer-v1": "de8d0cba9b710e7421a1e1df6c1dab1f04e578e4200adaac93362210f739338e",
}


def pin(path: Path) -> dict[str, str | int]:
    with path.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def main() -> None:
    output = AREA / "handoff-v1"
    if output.exists():
        raise ValueError("Frozen handoff already exists")
    reports = []
    for name, expected in EXPECTED.items():
        item = pin(AREA / name / "report.json")
        if item["sha256"] != expected:
            raise ValueError("Changed sealed report: " + name)
        reports.append(item)
    styles = [
        {"name": "LEGO Stormtrooper", "originalArmor": "DP-40 Hero of the Federation",
         "appliedArmorId": "B513FD54", "helmetId": "5C3087D2", "bodyCategory": 0, "helmetCategory": 1,
         "armorPackage": "1beb230363aaf427", "helmetPackage": "9d538582c1cd96b8",
         "brawnyTorsoUnit": "bb11c8041c14c747", "leanTorsoUnit": "003cc8a527711da5", "helmetUnit": "2bb094edbc756963"},
        {"name": "LEGO Bikini Stormtrooper", "originalArmor": "AF-02 Haz-Master",
         "appliedArmorId": "E9ADD047", "helmetId": "2F748B84", "bodyCategory": 0, "helmetCategory": 1,
         "armorPackage": "c67938d0bbe8dcce", "helmetPackage": "be1fbdaa4b77e86b",
         "brawnyTorsoUnit": "c1141b792c1b621a", "leanTorsoUnit": "09ad5784b873e58f", "helmetUnit": "43840f40d7999c80"},
    ]
    report = {"status": "offline-qualified", "styles": styles, "reports": reports,
              "identity": {"bodyTypes": {"0": "Brawny", "1": "Lean"},
                           "bodyAloneDeterminesFeature": True, "ordinaryBodyWithLegoHelmetAccepted": False,
                           "appliedBodyOffset": 12, "appliedHelmetOffset": 4, "recordStride": 68,
                           "kitCategoryOffset": 40, "currentPrimaryKitRecords": 411,
                           "historicalNativeKitRecordsIndependentlyRead": 402,
                           "actualVisualUnitMainsSourceMatched": 26, "textureProfiles": ["Full", "Lighter"]},
              "excludedRoutes": {"genericSkeleton": "3c33cf10a26cbb3e", "skeletonConsumers": 101,
                                 "genericAvatar": "4d1c334d294dfa97", "unresolvedAvatarOnDeathHash": "35b4f97f",
                                 "assetOnlyPerKitDeathAudioFieldProved": False,
                                 "equipmentPackagesAltered": False, "equipmentAudioDependencies": 0},
              "review": {"candidateV1Fixtures": 78, "preparedV2Fixtures": 81,
                         "allOriginalHircRecordsPreserved": 2640, "originalEventsPreserved": 604,
                         "originalSoundContractsPreserved": 644, "originalResidentMediaPreserved": 335,
                         "newDedicatedEvent": "clonedivers_lego_yoda_death", "newDedicatedEventId": 4055635130,
                         "classicSourcePcmUnchangedFrames": 14705, "audioHz": 11025,
                         "ordinaryDeathAudioPreserved": True, "remotePlayerRoutingAuthored": False},
              "remaining": ["Verify exact modules embedded in final addon against the reviewed prepared reader.",
                            "Loaded native instruction/event proofs and audible local LEGO death remain runtime acceptance checks."],
              "runtimeAccepted": False, "publicOrLiveChanges": False, "tool": pin(Path(__file__))}
    output.mkdir(parents=True)
    destination = output / "report.json"
    destination.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(pin(destination)))


if __name__ == "__main__":
    main()
