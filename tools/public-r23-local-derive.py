#!/usr/bin/env python3
"""Derive a pinned r23 additive transaction and actual-desktop-binary checker."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re


def sha(path: Path) -> str:
    with path.open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def replace(text: str, old: str, new: str, count: int = 1) -> str:
    if text.count(old) != count:
        raise ValueError(f"Template anchor differs ({text.count(old)} vs {count}): {old[:90]}")
    return text.replace(old, new)


def run(root: Path) -> None:
    release = root / "dist/production-r23-2026-10-08"
    manifest_path = release / "release-ready-v1/manifest.json"
    manifest_sha = sha(manifest_path)
    baseline_sha = sha(root / "dist/production-r22-2026-10-08/release-ready-v1/manifest.json")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    verify_path = release / "public-readback-v1/verification.json"
    verification = json.loads(verify_path.read_text(encoding="utf-8-sig"))
    if not (manifest["pack"]["version"] == "2026.10.08-r23" and len(manifest["pack"]["files"]) == 1809 and verification.get("passed") and verification.get("version") == "2026.10.08-r23" and len(verification.get("feeds", [])) == 3 and all(row.get("apiEqualsPrepared") and row.get("rawEqualsPrepared") for row in verification["feeds"])):
        raise ValueError("Published r23 verification required")
    added = manifest["pack"]["files"][1800:]
    old = root / "tools/PublicR22.Local/Apply.cs"
    text = old.read_text(encoding="utf-8-sig")
    text = replace(text, "static class PublicR22Local", "static class PublicR23Local")
    text = re.sub(r'const string BaselineSha = "[a-f0-9]{64}";', f'const string BaselineSha = "{baseline_sha}";', text, count=1)
    text = re.sub(r'const string TargetSha = "[a-f0-9]{64}";', f'const string TargetSha = "{manifest_sha}";', text, count=1)
    start, end = text.index("    static void Compare("), text.index("    static async Task<bool> Cache(")
    identities = ",\n            ".join(json.dumps(row["sha256"]) for row in added)
    sizes = ", ".join(str(row["size"]) for row in added)
    compare = '''    static void Compare(PackManifest baseline, PackManifest target)
    {
        Require(target.Version == "2026.10.08-r23" && baseline.Version == "2026.10.08-r22" &&
            baseline.Files.Count == 1800 && target.Files.Count == 1809 &&
            baseline.GameBuild == "25480438" && baseline.GameBuild == target.GameBuild &&
            baseline.CombinedRoster == target.CombinedRoster && target.CombinedRoster && baseline.Name == target.Name &&
            baseline.Status == target.Status && Serialize(baseline.GameDepots) == Serialize(target.GameDepots) &&
            Serialize(baseline.Options) == Serialize(target.Options) &&
            Serialize(baseline.TextureProfiles) == Serialize(target.TextureProfiles), "Manifest definitions changed.");
        for (var index = 0; index < baseline.Files.Count; index++)
            Require(Serialize(baseline.Files[index]) == Serialize(target.Files[index]), "Prior row changed at " + index);
        var identities = new[] { IDENTITIES };
        var sizes = new long[] { SIZES };
        for (var index = 0; index < 9; index++)
        {
            var row = target.Files[1800 + index];
            Require(row.Modes?.SequenceEqual(new[] { "empire" }) == true && row.TextureProfiles is null &&
                row.Option is null && row.UnlessOption is null &&
                row.Name == "9ba626afa44a3aa3.patch_" + (461 + index / 3) + new[] { "", ".stream", ".gpu_resources" }[index % 3] &&
                row.Sha256 == identities[index] && row.Size == sizes[index] &&
                (row.Size == 0 ? row.Url == "" : row.Url ==
                "https://github.com/owendavidgoode/clonedivers/releases/download/pack-2026.10.08-r23-files/" + row.Sha256), "Unexpected overlay row.");
        }
    }
'''.replace("IDENTITIES", identities).replace("SIZES", sizes)
    text = text[:start] + compare + text[end:]
    text = replace(text, 'var targetPath = Path.Combine(root, "dist/production-r22-2026-10-08/release-ready-v1/manifest.json");', 'var targetPath = Path.Combine(root, "dist/production-r23-2026-10-08/release-ready-v1/manifest.json");')
    text = replace(text, 'var baselinePath = Path.Combine(root, "dist/production-r21-2026-10-08/release-ready-v1/manifest.json");', 'var baselinePath = Path.Combine(root, "dist/production-r22-2026-10-08/release-ready-v1/manifest.json");')
    text, count = re.subn(r'await Pin\(Path.Combine\(root, "dist/production-r22-2026-10-08/public-readback-v1/verification.json"\), "[a-f0-9]{64}"\);', f'await Pin(Path.Combine(root, "dist/production-r23-2026-10-08/public-readback-v1/verification.json"), "{sha(verify_path)}");', text)
    if count != 1:
        raise ValueError("Public verification anchor")
    text = replace(text, "wanted.Count == 1387 && oldWanted.Count == 1381", "wanted.Count == 1396 && oldWanted.Count == 1387")
    text = text.replace("r21 baseline", "r22 baseline").replace("public r21", "public r22").replace("Public r21", "Public r22")
    text = text.replace("six appended r22 files", "nine appended r23 files").replace("six appended files", "nine appended files")
    text = replace(text, "pack.Files.Skip(1794)", "pack.Files.Skip(1800)")
    text = replace(text, "addedFiles.Count == 6", "addedFiles.Count == 9")
    text = replace(text, "x.Op == PlanOp.Finalize) == 6", "x.Op == PlanOp.Finalize) == 9")
    text = text.replace("index <= 460", "index <= 463").replace('== 387, "Supplemental triads changed."', '== 396, "Supplemental triads changed."')
    anchor = '                    Path.Combine(root, "dist/production-r22-2026-10-08/release-ready-v1/assets", file.Sha256),'
    text = replace(text, anchor, '                    Path.Combine(root, "dist/production-r23-2026-10-08/release-ready-v1/assets", file.Sha256),\n' + anchor)
    text = text.replace(".public-r22-", ".public-r23-").replace("exact public r22 installed", "exact public r23 installed")
    text = text.replace("// Compare already pins the twelve logical profile-gate changes.\n                old.TextureProfiles = next.TextureProfiles;", "// All prior logical and physical rows remain byte exact.")
    folder = root / "tools/PublicR23.Local"
    folder.mkdir()
    (folder / "Apply.cs").write_text(text, encoding="utf-8")
    (folder / "PublicR23.Local.csproj").write_text((root / "tools/PublicR22.Local/PublicR22.Local.csproj").read_text(encoding="utf-8-sig").replace("PublicR22Local", "PublicR23Local"), encoding="utf-8")
    (folder / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>\n', encoding="utf-8")
    peer = (root / "tools/PublicR22.LocalPeer/Check.cs").read_text(encoding="utf-8-sig")
    peer = re.sub(r'const string ManifestSha = "[a-f0-9]{64}";', f'const string ManifestSha = "{manifest_sha}";', peer, count=1)
    peer = peer.replace("dist/production-r22-2026-10-08", "dist/production-r23-2026-10-08").replace("2026.10.08-r22", "2026.10.08-r23")
    peer = peer.replace("Count == 1387", "Count == 1396").replace("1387 selected", "1396 selected").replace("r22OverlaysInstalled", "r23OverlaysInstalled")
    start = peer.index('                    Require(installedHashes.Contains(')
    end = peer.index('                }\n                Console.WriteLine', start)
    conditions = " &&\n                        ".join(f'installedHashes.Contains("{row["sha256"]}")' for row in added if row["size"])
    peer = peer[:start] + f'                    Require({conditions}, "r23 overlays absent.");\n' + peer[end:]
    peer_folder = root / "tools/PublicR23.LocalPeer"
    peer_folder.mkdir()
    (peer_folder / "Check.cs").write_text(peer, encoding="utf-8")
    (peer_folder / "PublicR23.LocalPeer.csproj").write_text((root / "tools/PublicR22.LocalPeer/PublicR22.LocalPeer.csproj").read_text(encoding="utf-8-sig"), encoding="utf-8")
    (peer_folder / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>\n', encoding="utf-8")
    print("Generated r23 transaction and actual desktop assembly peer.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, required=True)
    run(parser.parse_args().workspace.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
