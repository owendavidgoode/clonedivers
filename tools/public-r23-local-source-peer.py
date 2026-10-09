#!/usr/bin/env python3
"""Independently check generated r23 local helper source against guarded r22 source."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
LANE = ROOT / "dist/production-r23-2026-10-08"


def require(value: Any, message: str) -> None:
    if not value:
        raise ValueError(message)


def pin(path: Path) -> dict[str, Any]:
    with path.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest}


def text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def once(source: str, before: str, after: str, count: int = 1) -> str:
    require(source.count(before) == count, f"Exact source anchor required ({count}): {before[:100]}")
    return source.replace(before, after)


def section(source: str, start: str, end: str) -> str:
    return source[source.index(start):source.index(end)]


def run(out: Path) -> dict[str, Any]:
    require(not out.exists() and out.resolve().is_relative_to(LANE / "qa"), "Fresh scoped source peer output")
    old_path, new_path = ROOT / "tools/PublicR22.Local/Apply.cs", ROOT / "tools/PublicR23.Local/Apply.cs"
    old, new = text(old_path), text(new_path)
    baseline_path = ROOT / "dist/production-r22-2026-10-08/release-ready-v1/manifest.json"
    manifest_path = LANE / "release-ready-v1/manifest.json"
    baseline, manifest = json.loads(text(baseline_path)), json.loads(text(manifest_path))
    verification_path = LANE / "public-readback-v1/verification.json"
    verification = json.loads(text(verification_path))
    require(verification["passed"] and verification["version"] == "2026.10.08-r23" and len(verification["feeds"]) == 3 and all(r["apiEqualsPrepared"] and r["rawEqualsPrepared"] for r in verification["feeds"]), "Three public feeds verified before derivation")
    require(len(manifest["pack"]["files"]) == 1809 and manifest["pack"]["files"][:1800] == baseline["pack"]["files"], "Exact sealed additive manifests")
    old_compare = section(old, "    static void Compare(", "    static async Task<bool> Cache(")
    new_compare = section(new, "    static void Compare(", "    static async Task<bool> Cache(")
    for anchor in ("baseline.Files.Count == 1800 && target.Files.Count == 1809", "for (var index = 0; index < baseline.Files.Count; index++)", "Serialize(baseline.Files[index]) == Serialize(target.Files[index])", "row.Modes?.SequenceEqual(new[] { \"empire\" }) == true", "row.TextureProfiles is null", "row.Option is null && row.UnlessOption is null", "(461 + index / 3)"):
        require(anchor in new_compare, "Strict r23 Compare guard retained: " + anchor)
    additions = manifest["pack"]["files"][1800:]
    require(all(json.dumps(r["sha256"]) in new_compare for r in additions), "All nine exact new identities pinned")
    require(", ".join(str(r["size"]) for r in additions) in new_compare, "All nine new sizes pinned")
    restored = once(new, new_compare, old_compare)
    restored = once(restored, "static class PublicR23Local", "static class PublicR22Local")
    for name in ("BaselineSha", "TargetSha"):
        old_decl = re.search(rf'const string {name} = "[a-f0-9]{{64}}";', old)
        new_decl = re.search(rf'const string {name} = "[a-f0-9]{{64}}";', restored)
        require(old_decl and new_decl, "Exact hash constant declarations")
        expected = pin(baseline_path if name == "BaselineSha" else manifest_path)["sha256"]
        require(f'"{expected}"' in new_decl.group(), "Current sealed manifest source hash")
        restored = once(restored, new_decl.group(), old_decl.group())
    replacements = [
        ('var targetPath = Path.Combine(root, "dist/production-r23-2026-10-08/release-ready-v1/manifest.json");', 'var targetPath = Path.Combine(root, "dist/production-r22-2026-10-08/release-ready-v1/manifest.json");', 1),
        ('var baselinePath = Path.Combine(root, "dist/production-r22-2026-10-08/release-ready-v1/manifest.json");', 'var baselinePath = Path.Combine(root, "dist/production-r21-2026-10-08/release-ready-v1/manifest.json");', 1),
        ("wanted.Count == 1396 && oldWanted.Count == 1387", "wanted.Count == 1387 && oldWanted.Count == 1381", 1),
        ("nine appended r23 files", "six appended r22 files", 1),
        ("nine appended files", "six appended files", 2),
        ("pack.Files.Skip(1800)", "pack.Files.Skip(1794)", 1),
        ("addedFiles.Count == 9", "addedFiles.Count == 6", 1),
        ("x.Op == PlanOp.Finalize) == 9", "x.Op == PlanOp.Finalize) == 6", 1),
        ("index <= 463", "index <= 460", 2),
        ('== 396, "Supplemental triads changed."', '== 387, "Supplemental triads changed."', 1),
        ('                    Path.Combine(root, "dist/production-r23-2026-10-08/release-ready-v1/assets", file.Sha256),\n', "", 1),
        (".public-r23-", ".public-r22-", 1),
        ("exact public r23 installed", "exact public r22 installed", 1),
        ("// All prior logical and physical rows remain byte exact.", "// Compare already pins the twelve logical profile-gate changes.\n                old.TextureProfiles = next.TextureProfiles;", 1),
    ]
    for before, after, count in replacements:
        restored = once(restored, before, after, count)
    old_verify = re.search(r'await Pin\(Path.Combine\(root, "dist/production-r22-2026-10-08/public-readback-v1/verification.json"\), "[a-f0-9]{64}"\);', old)
    new_verify = re.search(r'await Pin\(Path.Combine\(root, "dist/production-r23-2026-10-08/public-readback-v1/verification.json"\), "[a-f0-9]{64}"\);', restored)
    require(old_verify and new_verify and pin(verification_path)["sha256"] in new_verify.group(), "Exact public readback report hash")
    restored = once(restored, new_verify.group(), old_verify.group())
    restored = once(restored, "exact public r22 installed", "EXACT_PUBLIC_R22_INSTALLED_SENTINEL")
    for before, after in (("r22 baseline", "r21 baseline"), ("public r22", "public r21"), ("Public r22", "Public r21")):
        restored = restored.replace(before, after)
    restored = once(restored, "EXACT_PUBLIC_R22_INSTALLED_SENTINEL", "exact public r22 installed")
    require(restored == old, "All helper bytes outside reviewed version/count/Compare/source-path changes exact")
    source_catalog = ROOT / "dist/production-r21-2026-10-08/local-promotion-v1/source/source-pins.json"
    require(pin(source_catalog)["sha256"] == "fdc43a394977b8f07a85e9a2d193706560f6cb2dc8cbcb3ab86d3bfc156a2a17", "Original public 1.7.3 source catalog exact")
    source = json.loads(text(source_catalog))
    require(len(source["files"]) == 15, "Original 15 public source files")
    source_dir = source_catalog.parent / "Clonedivers"
    require(len(list(source_dir.glob("*.cs"))) == 15, "No additional compiled public source")
    for row in source["files"]:
        require(pin(source_dir / row["name"])["sha256"] == row["sha256"], "Actual compiled source pin")
    old_project, new_project = ROOT / "tools/PublicR22.Local/PublicR22.Local.csproj", ROOT / "tools/PublicR23.Local/PublicR23.Local.csproj"
    require(text(old_project).replace("PublicR22Local", "PublicR23Local") == text(new_project), "Same 15 public source compilation inputs")
    old_peer_path, new_peer_path = ROOT / "tools/PublicR22.LocalPeer/Check.cs", ROOT / "tools/PublicR23.LocalPeer/Check.cs"
    peer_old, peer_new = text(old_peer_path), text(new_peer_path)
    require(section(peer_old, "    static void Closed()", "    static async Task<int> Main(") == section(peer_new, "    static void Closed()", "    static async Task<int> Main("), "Actual binary extraction/read-only process guards exact")
    for anchor in ("var managed = ExtractManaged(File.ReadAllBytes(app));", "wanted).Count == 1396", 'new[] { "EmpireDivers", "Clonedivers", "CommandoDivers" }', 'mode == "EmpireDivers", null, CancellationToken.None', 'Property(plan, "Downloads")!).Count == 0', 'Property(plan, "IsNoOp")!', 'Require(watched.All(path => Sha(path) == before[path])', 'foreach (var key in new[] { "GamePath", "TextureProfile", "Telemetry", "Options" })', "externalWrites = 0"):
        require(anchor in peer_new, "Actual binary peer guard retained: " + anchor)
    require(pin(manifest_path)["sha256"] in peer_new and all(r["sha256"] in peer_new for r in additions if r["size"]), "Actual binary peer manifest and new asset pins")
    require(text(ROOT / "tools/PublicR22.LocalPeer/PublicR22.LocalPeer.csproj") == text(ROOT / "tools/PublicR23.LocalPeer/PublicR23.LocalPeer.csproj"), "Same independent peer project")
    out.mkdir(parents=True)
    report = {"passed": True, "tool": pin(Path(__file__)), "deriver": pin(ROOT / "tools/public-r23-local-derive.py"), "baselineHelper": pin(old_path), "generatedHelper": pin(new_path), "generatedPeer": pin(new_peer_path), "manifest": pin(manifest_path), "publicVerification": pin(verification_path),
              "helperExactOutsideReviewedScopedChanges": True, "allProcessRecoveryPathLinkNativeBuildAppCacheCameraPreferencesAndTransactionGuardsPreserved": True,
              "strict1800PriorRowsAndNineAdditionsCompared": True, "fullSelection1396From1387": True, "all15Public173SourceFilesUnchanged": True,
              "actualDesktopExtractionAndFreshInventoryPeerGuardsPreserved": True, "allThreeModesZeroDownloadPeerRetained": True, "externalWrites": 0, "installedBytesNotYetCheckedByThisSourceReview": True}
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return {"passed": True, "report": pin(out / "report.json")}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=LANE / "qa/local-source-peer-v1")
    print(json.dumps(run(parser.parse_args().out.resolve())))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
