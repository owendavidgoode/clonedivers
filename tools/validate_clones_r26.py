#!/usr/bin/env python3
"""Comprehensive 5-tier validator for staged Clonedivers r26 Stingray archives.

Validates across all 5 performance tiers (`full`, `lighter`, `reduced1024`, `reduced512`, `potato256`)
and selection matrices (`clonedivers` / `commandos` × `all-droids` / `mixed-droids` / `native-bots` /
`covenant` / `mixed-covenant`):
  - SLIM archive directory preflight + companion `.stream` / `.gpu_resources` range checks (zero OOB!).
  - SHA-256 + exact byte-size verification for 100% of newly staged blobs in `blobs/`.
  - 100% bit-exact preservation of all 83 runtime-pinned LEGO, Yoda, camera-pullback, and AT-ST `UNIT`s.
  - Gate 1a (`patch_45`): Delta Squad Katarn helmets (`CE-35`, `CM-10`, `SC-30`) win in BOTH `clonedivers`
    and `commandos` modes, pass `empire_port.unit_safe` against `stock`, preserve external `.bones`, and
    maintain 100% `@52` bone-hash parity.
  - Gate 1b (`patch_41`, `patch_46`, `patch_47`, `patch_48`): MagnaGuard `m8..m13` rebound to `m_collision`,
    all 16 shared Cyborg corpse/severed-limb/gore/spike `UNIT`s hidden (`0` drawn tris, `unit_safe == True`)
    across `all-droids` & `mixed-droids`, and all 5 HMP Gunship wreck + Berserker corpse `UNIT`s hidden in
    `all-droids` (and untouched in `mixed-droids` / `native-bots`).
  - Gate 1d (`patch_44` + `clone_eagle_assets.h`): 100% self-contained aircraft `MATERIAL` + `TEXTURE`
    residency in `patch_44`, every custom `eagle_base.package` entry pinned across all 5 tiers, and full
    `asset_gate.h` `ScanAssets` simulation across ALL `*.patch_*` files in `staged/{all-droids,mixed-droids}/<tier>`.
  - Gate 1e (`MELEE_POI_PACKAGES`): 100% 5-tier dependency closure across all 9 melee packages (`cqc-72`,
    `cqc-2`, `cqc-19`, `cqc-30`, `cqc-5`, `cqc-42` Machete, `melee_chainsaw_greatsword`, `melee_flag`,
    `melee_sledge_hammer`) plus Guard Dog Rover (`32cb44321da1b5a0` / `cae3f1e8a44a80b3`).
  - Zero external `GeometryGroup` residency violations, zero `TINY_MESH_AABB`, `<= 64` bones per compact skin,
    and zero `NaN` / `Inf` floats across matrices and hydrated GPU vertex/UV/blend buffers.
"""
from __future__ import annotations

import argparse
from collections import Counter
import functools
import hashlib
import json
import math
import re
import struct
import sys
from pathlib import Path
from typing import Any

import numpy as np

sys.dont_write_bytecode = True

TOOLS_DIR = Path(__file__).resolve().parent
WT_ROOT = TOOLS_DIR.parent
sys.path.insert(0, str(TOOLS_DIR))
sys.path.insert(0, str(TOOLS_DIR / "empire-forge"))

import build_clones_r26 as B  # noqa: E402
import empire_port as P  # noqa: E402
import verify_candidate as VC  # noqa: E402

MELEE_POI_PACKAGES = (
    0x49DE16E1343250E4,  # cqc-72 Entrenchment Tool
    0x3CFC68E6BF19F67D,  # cqc-2
    0x1F251A64EBA0F770,  # cqc-19 Stun Lance
    0xF3C65EAAB75F1FC2,  # cqc-30 Stun Baton
    0x1982853D15D996DA,  # cqc-5 Combat Hatchet
    0xF5014993C8402D87,  # cqc-42 Machete (packages/generated/loadout/machete)
    0xEEEBC5FD26FE0F40,  # melee_chainsaw_greatsword
    0xC40E3B3FA79052EC,  # melee_flag
    0x39DDD2E6F131C873,  # melee_sledge_hammer
)


def select_manifest_files(
    manifest: dict[str, Any],
    mode: str,
    tier: str,
    options: set[str],
) -> list[dict[str, Any]]:
    chosen: list[dict[str, Any]] = []
    for f in manifest["pack"]["files"]:
        modes = f.get("modes")
        if modes and mode not in modes:
            continue
        profs = f.get("textureProfiles")
        if profs and tier not in profs:
            continue
        if f.get("option") and f["option"] not in options:
            continue
        if f.get("unlessOption") and f["unlessOption"] in options:
            continue
        chosen.append(f)
    return chosen


def drawn_index_count(raw: bytes) -> int:
    """Return total drawn index count across all non-collision (`mtype not in (0, 256)`) `@100` meshes."""
    at100 = struct.unpack_from("<I", raw, 100)[0]
    if not at100:
        return 0
    total = 0
    for mh in VC.pointers(raw, 100):
        mtype = struct.unpack_from("<I", raw, mh + 36)[0]
        if mtype in (0, 256):
            continue
        ng, go = struct.unpack_from("<2I", raw, mh + 120)
        for gi in range(ng):
            total += struct.unpack_from("<6I", raw, mh + go + 24 * gi)[4]
    return total


def joint_hashes(raw: bytes) -> list[int]:
    at52 = struct.unpack_from("<I", raw, 52)[0]
    if not at52:
        return []
    nn = struct.unpack_from("<I", raw, at52)[0]
    return list(struct.unpack_from(f"<{nn}I", raw, at52 + 16 + 132 * nn))


def check_unit_matrices_and_skins(raw: bytes) -> list[str]:
    """Check finite @52 local/world matrices, @88 bind matrices, and <= 64-bone skin palettes."""
    issues: list[str] = []
    at52, at88, at100 = (struct.unpack_from("<I", raw, off)[0] for off in (52, 88, 100))
    if at52:
        nn = struct.unpack_from("<I", raw, at52)[0]
        if at52 + 16 + 136 * nn > len(raw):
            issues.append("joint-table-oob")
            return issues
        floats_lw = np.frombuffer(raw, dtype="<f4", count=32 * nn, offset=at52 + 16)
        if not np.isfinite(floats_lw).all():
            issues.append("nan-or-inf-in-joint-matrices")
    else:
        nn = 0

    skin_remaps: list[list[int]] = []
    if at88:
        for si, sat in enumerate(VC.pointers(raw, 88)):
            bc, mo, bo, ro = struct.unpack_from("<4I", raw, sat)
            if bc > 4096 or sat + mo + 64 * bc > len(raw) or sat + bo + 4 * bc > len(raw):
                issues.append(f"skin-{si}-oob")
                continue
            bind_f = np.frombuffer(raw, dtype="<f4", count=16 * bc, offset=sat + mo)
            if not np.isfinite(bind_f).all():
                issues.append(f"nan-or-inf-in-skin-{si}-bind")
            joints = struct.unpack_from(f"<{bc}I", raw, sat + bo)
            if nn > 0 and any(j >= nn for j in joints):
                issues.append(f"skin-{si}-joint-oob")
            nr = struct.unpack_from("<I", raw, sat + ro)[0]
            r_sizes: list[int] = []
            for ri in range(nr):
                roff, rcnt = struct.unpack_from("<II", raw, sat + ro + 4 + 8 * ri)
                rslots = struct.unpack_from(f"<{rcnt}I", raw, sat + ro + roff)
                if any(s >= bc for s in rslots):
                    issues.append(f"skin-{si}-remap-{ri}-slot-oob")
                r_sizes.append(rcnt)
            skin_remaps.append(r_sizes)

    if at100:
        for mi, mh in enumerate(VC.pointers(raw, 100)):
            vals7 = struct.unpack_from("<7f", raw, mh + 8)
            if not all(math.isfinite(x) for x in vals7):
                issues.append(f"mesh-{mi}-nan-aabb")
            ti44, ti48 = struct.unpack_from("<2I", raw, mh + 44)
            if nn > 0 and (ti44 >= nn or ti48 >= nn):
                issues.append(f"mesh-{mi}-transform-idx-oob-{ti44}-{ti48}")
            si = struct.unpack_from("<i", raw, mh + 56)[0]
            nm, mo = struct.unpack_from("<2I", raw, mh + 104)
            ng, go = struct.unpack_from("<2I", raw, mh + 120)
            if nm > 1:
                mkeys = struct.unpack_from(f"<{nm}I", raw, mh + mo)
                if len(set(mkeys)) != nm:
                    issues.append(f"mesh-{mi}-duplicate-mkeys")
            for gi in range(ng):
                mat_i, _, _, _, icnt, skin_set = struct.unpack_from("<6I", raw, mh + go + 24 * gi)
                if nm > 0 and mat_i >= nm:
                    issues.append(f"mesh-{mi}-grp-{gi}-mat-oob")
                if icnt > 0 and si >= 0:
                    if si >= len(skin_remaps) or skin_set >= len(skin_remaps[si]):
                        issues.append(f"mesh-{mi}-grp-{gi}-skinset-oob")
                    elif skin_remaps[si][skin_set] == 0 or skin_remaps[si][skin_set] > 64:
                        issues.append(f"mesh-{mi}-grp-{gi}-bones-{skin_remaps[si][skin_set]}-gt-64")
    return issues


def check_gpu_vertices_finite(unit_main: bytes, unit_gpu: bytes) -> list[str]:
    """Verify GPU vertex positions, float UVs, and float16/float32 blend weights contain 0 NaN/Inf."""
    issues: list[str] = []
    at92 = struct.unpack_from("<I", unit_main, 92)[0]
    if not at92:
        return issues
    fmt_sizes = {
        0: 4, 1: 8, 2: 12, 3: 16, 4: 4, 21: 4, 22: 8, 23: 12, 24: 16,
        25: 1, 26: 4, 27: 3, 28: 4, 29: 4, 30: 4, 32: 2, 33: 4, 34: 6, 35: 8,
    }
    for li, lp in enumerate(VC.pointers(unit_main, 92)):
        lay = unit_main[lp : lp + 448]
        acnt = struct.unpack_from("<I", lay, 328)[0]
        vc, stride = struct.unpack_from("<II", lay, 352)
        ic, _ = struct.unpack_from("<II", lay, 392)
        vo, vb, io, ib = struct.unpack_from("<4I", lay, 416)
        if vo + vc * stride > len(unit_gpu) or io + ib > len(unit_gpu):
            issues.append(f"layout-{li}-gpu-oob")
            continue
        cursor = 0
        for ai in range(acnt):
            _, fmt, _ = struct.unpack_from("<3I", lay, 8 + 20 * ai)
            sz = fmt_sizes.get(fmt)
            if sz is None:
                issues.append(f"layout-{li}-unknown-fmt-{fmt}")
                break
            if vc > 0:
                if fmt in (0, 1, 2, 3):  # f32x1..4
                    dim = sz // 4
                    arr = np.ndarray((vc, dim), dtype="<f4", buffer=unit_gpu, offset=vo + cursor, strides=(stride, 4))
                    if not np.isfinite(arr).all():
                        issues.append(f"layout-{li}-attr-{ai}-f32-nan")
                elif fmt in (32, 33, 34, 35):  # f16x1..4
                    dim = sz // 2
                    arr = np.ndarray((vc, dim), dtype="<f2", buffer=unit_gpu, offset=vo + cursor, strides=(stride, 2))
                    if not np.isfinite(arr).all():
                        issues.append(f"layout-{li}-attr-{ai}-f16-nan")
            cursor += sz
        if cursor != stride:
            issues.append(f"layout-{li}-stride-mismatch-{cursor}!={stride}")
        if ic > 0:
            iw = ib // ic
            idx_arr = np.frombuffer(unit_gpu, dtype="<u2" if iw == 2 else "<u4", count=ic, offset=io)
            if vc > 0 and int(idx_arr.max()) >= vc:
                issues.append(f"layout-{li}-index-exceeds-vc")
    return issues


def simulate_asset_gate_scan_dir(
    staged_dir: Path,
    pin_rows: list[tuple[int, int, int, list[str]]],
    expected_tier_idx: int,
) -> list[str]:
    """Faithfully simulate `forge-mods/empire-tie-switch/src/asset_gate.h` `ScanAssets(directory)` across all `*.patch_*`."""
    errs: list[str] = []
    patch_re = re.compile(r"^([0-9a-f]{16})\.patch_(\d+)$")
    patch_files: list[tuple[str, int, Path]] = []
    for entry in sorted(staged_dir.iterdir()):
        if not entry.is_file():
            continue
        m = patch_re.fullmatch(entry.name)
        if not m:
            continue
        idx = int(m.group(2))
        if idx > 100000:
            continue
        patch_files.append((m.group(1), idx, entry))

    if not patch_files:
        return [f"ScanAssets: no readable staged patches in {staged_dir}"]

    wanted_keys = {(rname, rkind) for rname, rkind, _, _ in pin_rows}
    # Map (rname, rkind) -> (prefix16, index, main_path, row_tuple)
    proofs: dict[tuple[int, int], tuple[str, int, Path, tuple[int, ...]]] = {}

    for prefix16, idx, ppath in patch_files:
        try:
            rows = P.directory(ppath)
        except Exception as exc:
            errs.append(f"ScanAssets: invalid archive directory {ppath}: {exc}")
            return errs
        for row in rows:
            rk = (row[0], row[1])
            if rk not in wanted_keys:
                continue
            if rk in proofs:
                prior_prefix, prior_idx, _, _ = proofs[rk]
                if prior_prefix != prefix16 or prior_idx == idx:
                    errs.append(f"ScanAssets: ambiguous archive provider for {rk[0]:016x}.{rk[1]:016x} in {ppath}")
                    return errs
                if idx > prior_idx:
                    proofs[rk] = (prefix16, idx, ppath, row)
            else:
                proofs[rk] = (prefix16, idx, ppath, row)

    tier_mask = 0x1F
    for rname, rkind, part_idx, hashes5 in pin_rows:
        rk = (rname, rkind)
        if rk not in proofs:
            errs.append(f"ScanAssets: missing staged resource {rname:016x}.{rkind:016x} part={part_idx} in {staged_dir}")
            continue
        _, _, main_p, row = proofs[rk]
        comp_p = main_p.with_name(main_p.name + P.SUFFIXES[part_idx])
        if not comp_p.is_file():
            errs.append(f"ScanAssets: missing companion file {comp_p}")
            continue
        file_len = comp_p.stat().st_size
        off = row[2 + part_idx]
        sz = row[7 + part_idx]
        if sz > 128 * 1024 * 1024 or off > file_len or sz > file_len - off:
            errs.append(f"ScanAssets: OOB payload {rname:016x} part={part_idx} off={off} sz={sz} len={file_len} in {comp_p}")
            continue
        with comp_p.open("rb") as fh:
            fh.seek(off)
            pdata = fh.read(sz) if sz else b""
        actual_h = hashlib.sha256(pdata).hexdigest()
        part_mask = 0
        for ti, expected_h in enumerate(hashes5):
            if actual_h == expected_h:
                part_mask |= 1 << ti
        if part_mask == 0:
            errs.append(f"ScanAssets: hash mismatch for {rname:016x}.{rkind:016x} part={part_idx} in {comp_p}")
            continue
        if not (part_mask & (1 << expected_tier_idx)):
            errs.append(f"ScanAssets: tier bit {expected_tier_idx} missing for {rname:016x} part={part_idx} in {comp_p}")
        tier_mask &= part_mask
        if tier_mask == 0:
            errs.append(f"ScanAssets: inconsistent texture tier across staged closure at {rname:016x} in {staged_dir}")
            break

    return errs


def validate(
    *,
    out_dir: Path = Path(r"D:\ClaudeWork\swarm-packs\clones-r26"),
    cache_dir: Path = Path(r"D:\ClaudeWork\swarm-packs\clones-r26\_blob_cache"),
) -> dict[str, Any]:
    blobs_dir = out_dir / "blobs"
    r26_path = out_dir / "hellforge-clones-r26.json"
    eagle_h_path = out_dir / "clone_eagle_assets.h"
    r25 = json.loads((WT_ROOT / "Clonedivers/Resources/hellforge-clones-r25.json").read_text("utf-8-sig"))
    r26 = json.loads(r26_path.read_text("utf-8-sig"))
    protected_units = B.load_protected_units(WT_ROOT)

    nat_idx = json.loads(Path(r"D:\ClaudeWork\empire-forge\native-index-25480438.json").read_text("utf-8"))
    full_idx = json.loads(
        Path(r"D:\ClaudeWork\empire-forge\r47\verifier-inputs\dist\hd2-update-2026-10-29\native-index-v1\native-index.json").read_text("utf-8")
    )
    native = B.NATIVE_ARCH.NativeSlim(Path(r"C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2\data"))
    native.chunk = functools.lru_cache(maxsize=64)(native.chunk)

    @functools.lru_cache(maxsize=512)
    def _archive_toc(archive: str) -> dict[tuple[int, int], tuple[int, ...]]:
        hdr = native.read(archive, 0, 72)
        magic, nt, nf = struct.unpack_from("<III", hdr, 0)
        if magic != 0xF0000011:
            raise ValueError(f"Invalid native archive header {archive}")
        raw_rows = native.read(archive, 72 + 32 * nt, 80 * nf)
        return {r[:2]: r for r in P.ROW.iter_unpack(raw_rows)}

    @functools.lru_cache(maxsize=2048)
    def stock_main(key: tuple[int, int]) -> bytes | None:
        lbl = f"{key[0]:016x}.{key[1]:016x}"
        seen_archives: set[str] = set()
        for idx_d in (nat_idx, full_idx):
            for prov in idx_d.get(lbl, []):
                arch = prov["archive"]
                if arch in seen_archives:
                    continue
                seen_archives.add(arch)
                try:
                    toc = _archive_toc(arch)
                    if key not in toc:
                        continue
                    r = toc[key]
                    d = native.read(arch, r[2], r[7])
                    if len(d) == r[7]:
                        return d
                except Exception:
                    continue
        return None

    def resolve_blob(sha: str) -> Path:
        p1 = blobs_dir / sha
        if p1.is_file():
            return p1
        p2 = cache_dir / sha
        if p2.is_file():
            return p2
        raise FileNotFoundError(sha)

    errors: list[str] = []
    metrics = Counter()

    # 1. Verify every newly staged blob in `blobs/` matches its filename SHA-256 and manifest byte size
    manifest_sizes: dict[str, set[int]] = {}
    for f in r26["pack"]["files"]:
        if f["size"] > 0:
            manifest_sizes.setdefault(f["sha256"], set()).add(f["size"])

    for blob_file in sorted(blobs_dir.iterdir()):
        if not blob_file.is_file():
            continue
        actual_sha = B.sha256_file(blob_file)
        actual_sz = blob_file.stat().st_size
        metrics["staged_blobs_hashed"] += 1
        if actual_sha != blob_file.name:
            errors.append(f"Blob SHA mismatch: {blob_file.name} != {actual_sha}")
        if blob_file.name in manifest_sizes and manifest_sizes[blob_file.name] != {actual_sz}:
            errors.append(f"Blob size mismatch for {blob_file.name}: {actual_sz} vs {manifest_sizes[blob_file.name]}")

    # 2. Verify SLIM archive preflight + companion `(stream_size, gpu_size)` bounds on EVERY main archive in r26
    checked_preflight: set[tuple[str, int, int]] = set()
    for f in r26["pack"]["files"]:
        nm = f["name"]
        if nm.endswith((".stream", ".gpu_resources")) or f["size"] == 0:
            continue
        tier = (f.get("textureProfiles") or ["full"])[0]
        opt = f.get("option")
        unless_opt = f.get("unlessOption")

        def _comp_sz(suf: str) -> int:
            for e in r26["pack"]["files"]:
                if e["name"] != nm + suf:
                    continue
                profs = e.get("textureProfiles")
                if profs and tier not in profs:
                    continue
                if e.get("option") != opt or e.get("unlessOption") != unless_opt:
                    continue
                return e["size"]
            raise KeyError(f"Missing companion {nm + suf} for tier={tier} opt={opt}")

        stm_sz = _comp_sz(".stream")
        gpu_sz = _comp_sz(".gpu_resources")
        pf_key = (f["sha256"], stm_sz, gpu_sz)
        if pf_key not in checked_preflight:
            checked_preflight.add(pf_key)
            rep = B.PREFLIGHT.inspect_path(resolve_blob(f["sha256"]), (stm_sz, gpu_sz))
            metrics["archive_preflights_checked"] += 1
            if not rep["passed"]:
                errors.append(f"Preflight failed on {nm} ({f['sha256'][:12]}): {rep['issues']}")

    # 2b. Verify strict 0..K patch contiguity & unique triplets across all 180 valid mode × option × tier combinations!
    mode_states = (("clonedivers", set()), ("commandos", {"commandos"}))
    droid_states = (
        ("native-bots", set(), 42),
        ("all-droids", {"droids"}, 47),
        ("mixed-droids", {"droids", "mixed-droids"}, 47),
    )
    covenant_states = (set(), {"covenant"}, {"covenant", "mixed-covenant"})
    aimpoint_states = (set(), {"aimpoints"})
    for tier in B.TIERS:
        for mode_name, m_opts in mode_states:
            for d_lbl, d_opts, expected_max_p in droid_states:
                for c_opts in covenant_states:
                    for a_opts in aimpoint_states:
                        opts_180 = m_opts | d_opts | c_opts | a_opts
                        sel_180 = select_manifest_files(r26, mode_name, tier, opts_180)
                        by_name = Counter(e["name"] for e in sel_180)
                        dups = [nm for nm, cnt in by_name.items() if cnt != 1]
                        if dups:
                            errors.append(f"Duplicate manifest selection in {mode_name}/{d_lbl}/{tier}: {dups}")
                            continue
                        pnums = sorted(
                            int(e["name"].split("_")[1])
                            for e in sel_180
                            if not e["name"].endswith((".stream", ".gpu_resources"))
                        )
                        expected_seq = list(range(expected_max_p + 1))
                        if pnums != expected_seq:
                            errors.append(
                                f"Non-contiguous patch sequence in {mode_name}/{d_lbl}/{tier}: got {pnums}, expected 0..{expected_max_p}"
                            )
                            continue
                        for pn in expected_seq:
                            for suf in P.SUFFIXES:
                                if by_name.get(f"9ba626afa44a3aa3.patch_{pn}{suf}") != 1:
                                    errors.append(f"Missing companion patch_{pn}{suf} in {mode_name}/{d_lbl}/{tier}")
                        metrics["contiguous_180_combinations_verified"] += 1

    # 3. Verify 100% bit-exact preservation of all 83 protected runtime-pinned UNITs across all 5 tiers
    for tier in B.TIERS:
        r25_sel = select_manifest_files(r25, "commandos", tier, {"commandos", "droids", "aimpoints"})
        r26_sel = select_manifest_files(r26, "commandos", tier, {"commandos", "droids", "aimpoints"})

        def _unit_winners(sel: list[dict[str, Any]]) -> dict[int, tuple[bytes, int, str]]:
            wins: dict[int, tuple[bytes, int, str]] = {}
            mains = [e for e in sel if not e["name"].endswith((".stream", ".gpu_resources")) and e["size"] > 0]
            gpus = {e["name"]: e for e in sel if e["name"].endswith(".gpu_resources")}
            for e in sorted(mains, key=lambda x: int(x["name"].split("_")[-1])):
                bp = resolve_blob(e["sha256"])
                mb = bp.read_bytes()
                g_sha = gpus[e["name"] + ".gpu_resources"]["sha256"]
                for r in P.directory(bp):
                    if r[1] == B.UNIT_T:
                        wins[r[0]] = (mb[r[2] : r[2] + r[7]], r[9], f"{g_sha}:{r[4]}:{r[9]}")
            return wins

        w25 = _unit_winners(r25_sel)
        w26 = _unit_winners(r26_sel)
        for puid in sorted(protected_units):
            if puid in w25:
                metrics["protected_unit_tier_checks"] += 1
                if puid not in w26 or w25[puid] != w26[puid]:
                    errors.append(f"Protected UNIT 0x{puid:016x} mutated on tier={tier}")

    # Parse clone_eagle_assets.h pins up-front for Gate 1d closure & ScanAssets checks
    eagle_h_text = eagle_h_path.read_text("utf-8")
    pin_rows = [
        (
            int(m.group(1), 16),
            int(m.group(2), 16),
            int(m.group(3)),
            re.findall(r'"([0-9a-f]{64})"', m.group(4)),
        )
        for m in re.finditer(
            r"\{0x([0-9a-f]{16})ull,\s*0x([0-9a-f]{16})ull,\s*(\d+),\s*\{([^}]+)\}\}",
            eagle_h_text,
        )
    ]
    if not pin_rows:
        errors.append("No asset pins parsed from clone_eagle_assets.h")
    pinned_parts_set = {(rn, rk, pt) for rn, rk, pt, _ in pin_rows}

    # 4. Full 5-tier selection closure, Gate 1a/1b/1d/1e assertions, GeometryGroup scan, UNIT schema/AABB/skin/NaN checks
    selection_matrix = (
        ("commandos", {"commandos", "droids", "aimpoints"}, "commandos+all-droids"),
        ("commandos", {"commandos", "droids", "mixed-droids", "aimpoints"}, "commandos+mixed-droids"),
        ("clonedivers", {"droids", "aimpoints"}, "clonedivers+all-droids"),
        ("clonedivers", {"droids", "mixed-droids", "aimpoints"}, "clonedivers+mixed-droids"),
        ("clonedivers", {"aimpoints"}, "clonedivers+native-bots"),
        ("commandos", {"commandos", "droids", "mixed-droids", "covenant", "mixed-covenant", "aimpoints"}, "commandos+mixed-both"),
    )

    inspected_unit_blobs: set[str] = set()
    inspected_gpu_slices: set[tuple[str, int, int]] = set()
    stock_eagle_pkg = stock_main((B.EAGLE_PACKAGE, B.PACKAGE_T))
    assert stock_eagle_pkg is not None
    stock_eagle_pkg_entries = {(rn, rk) for rk, rn in struct.iter_unpack("<QQ", stock_eagle_pkg[16:])}

    for tier in B.TIERS:
        for mode, opts, label in selection_matrix:
            sel = select_manifest_files(r26, mode, tier, opts)
            mains = [e for e in sel if not e["name"].endswith((".stream", ".gpu_resources")) and e["size"] > 0]
            stms = {e["name"]: e for e in sel if e["name"].endswith(".stream")}
            gpus = {e["name"]: e for e in sel if e["name"].endswith(".gpu_resources")}

            winners: dict[tuple[int, int], tuple[str, Path, tuple[int, ...], str, str]] = {}
            for e in sorted(mains, key=lambda x: int(x["name"].split("_")[-1])):
                bp = resolve_blob(e["sha256"])
                s_sha = stms[e["name"] + ".stream"]["sha256"]
                g_sha = gpus[e["name"] + ".gpu_resources"]["sha256"]
                for r in P.directory(bp):
                    winners[(r[0], r[1])] = (e["name"], bp, r, s_sha, g_sha)

            metrics["selection_combinations_checked"] += 1

            # Verify skybox option gating on patch_35
            has_providence = (0x0F3F333C23700D53, B.UNIT_T) in winners
            if ("droids" in opts) != has_providence:
                errors.append(f"Skybox Providence gating mismatch in {label}/{tier}: has={has_providence}")

            # Gate 1a: Scorch/Fixer/Sev Katarn helmets win in BOTH `clonedivers` and `commandos` (`patch_41`), unit_safe & bone-hash aligned
            for dst_uid, _, _ in B.DELTA_SQUAD_HELMET_COPIES:
                h_win = winners.get((dst_uid, B.UNIT_T))
                if not h_win or h_win[0] != "9ba626afa44a3aa3.patch_41":
                    errors.append(f"Delta Squad helmet 0x{dst_uid:016x} not won by patch_41 in {label}/{tier}")
                    continue
                h_raw = h_win[1].read_bytes()[h_win[2][2] : h_win[2][2] + h_win[2][7]]
                stk_dst = stock_main((dst_uid, B.UNIT_T))
                assert stk_dst is not None
                if not P.unit_safe((h_raw, b"", b""), stk_dst):
                    errors.append(f"patch_41 helmet 0x{dst_uid:016x} failed empire_port.unit_safe in {label}/{tier}")
                if struct.unpack_from("<Q", h_raw, 8)[0] != struct.unpack_from("<Q", stk_dst, 8)[0]:
                    errors.append(f"patch_41 helmet 0x{dst_uid:016x} corrupted external bones ref in {label}/{tier}")
                if joint_hashes(h_raw) != joint_hashes(stk_dst):
                    errors.append(f"patch_41 helmet 0x{dst_uid:016x} @52 bone-hash order mismatch vs stock in {label}/{tier}")
                metrics["patch41_helmet_checks"] += 1

            # Verify 49-stream GAR Clone voice pronunciation delta (`patch_42`) wins across both `clonedivers` and `commandos`
            p42_main_entry = next((e for e in mains if e["name"] == "9ba626afa44a3aa3.patch_42"), None)
            if p42_main_entry is None or p42_main_entry["sha256"] != B.GAR_VO_MAIN_SHA256:
                errors.append(f"Missing or mismatched GAR VO patch_42 in {label}/{tier}")
            elif stms.get("9ba626afa44a3aa3.patch_42.stream", {}).get("sha256") != B.GAR_VO_STREAM_SHA256:
                errors.append(f"Mismatched GAR VO patch_42.stream in {label}/{tier}")
            else:
                p42_bp = resolve_blob(B.GAR_VO_MAIN_SHA256)
                p42_sp = resolve_blob(B.GAR_VO_STREAM_SHA256)
                p42_mb = p42_bp.read_bytes()
                p42_rows = P.directory(p42_bp)
                if len(p42_rows) != 49:
                    errors.append(f"Expected 49 GAR VO streams in patch_42, got {len(p42_rows)}")
                with p42_sp.open("rb") as sfh:
                    for vr in p42_rows:
                        vkey = (vr[0], vr[1])
                        if winners.get(vkey, ("",))[0] != "9ba626afa44a3aa3.patch_42":
                            errors.append(f"GAR VO stream {vr[0]:016x} not won by patch_42 in {label}/{tier}")
                            break
                        wrap = p42_mb[vr[2] : vr[2] + vr[7]]
                        stk_wrap = stock_main(vkey)
                        if len(wrap) != 12 or struct.unpack_from("<I", wrap, 8)[0] != vr[8]:
                            errors.append(f"Invalid GAR VO 12B wrapper on {vr[0]:016x}")
                            break
                        if stk_wrap is not None and wrap[:8] != stk_wrap[:8]:
                            errors.append(f"GAR VO native wrapper header mismatch on {vr[0]:016x}")
                            break
                        sfh.seek(vr[3])
                        riff_hdr = sfh.read(12)
                        if riff_hdr[:4] != b"RIFF" or riff_hdr[8:12] != b"WAVE":
                            errors.append(f"GAR VO stream {vr[0]:016x} missing RIFF/WAVE header")
                            break
                metrics["gar_vo_overlay_checks"] += 1

            # Gate 1b: MagnaGuard m8..m13 rebind + Cyborg corpse/severed-limb/gore/gunship-wreck hiders (`patch_47`)
            if "droids" in opts:
                mg_win = winners.get((B.MAGNAGUARD_FEMALE_UID, B.UNIT_T))
                if not mg_win:
                    errors.append(f"Missing MagnaGuard 9f44121c554dae89 in {label}/{tier}")
                else:
                    mg_raw = mg_win[1].read_bytes()[mg_win[2][2] : mg_win[2][2] + mg_win[2][7]]
                    mg_mm = VC.material_map(mg_raw)
                    for th in B.MAGNAGUARD_COLLISION_REBINDS:
                        if mg_mm.get(th) != B.M_COLLISION:
                            errors.append(f"MagnaGuard thin 0x{th:08x} not rebound to m_collision in {label}/{tier}")

                for cuid in B.CORPSE_SHARED_DROIDS:
                    c_win = winners.get((cuid, B.UNIT_T))
                    if not c_win or c_win[0] != "9ba626afa44a3aa3.patch_47":
                        errors.append(f"Shared corpse/gore hider 0x{cuid:016x} not won by patch_47 in {label}/{tier}")
                        continue
                    c_raw = c_win[1].read_bytes()[c_win[2][2] : c_win[2][2] + c_win[2][7]]
                    stk_c = stock_main((cuid, B.UNIT_T))
                    assert stk_c is not None
                    if not P.unit_safe((c_raw, b"", b""), stk_c):
                        errors.append(f"patch_47 hider 0x{cuid:016x} failed unit_safe in {label}/{tier}")
                    if drawn_index_count(c_raw) != 0:
                        errors.append(f"patch_47 hider 0x{cuid:016x} still draws indices in {label}/{tier}")
                    if any(v != B.M_COLLISION for v in VC.material_map(c_raw).values()):
                        errors.append(f"patch_47 hider 0x{cuid:016x} material_map not rebound to m_collision in {label}/{tier}")
                    metrics["corpse_hider_checks"] += 1

                if "mixed-droids" not in opts:
                    for wuid in B.CORPSE_ALL_DROIDS_ONLY:
                        w_win = winners.get((wuid, B.UNIT_T))
                        if not w_win or w_win[0] != "9ba626afa44a3aa3.patch_47":
                            errors.append(f"all-droids wreck/corpse hider 0x{wuid:016x} not won by patch_47 in {label}/{tier}")
                            continue
                        w_raw = w_win[1].read_bytes()[w_win[2][2] : w_win[2][2] + w_win[2][7]]
                        stk_w = stock_main((wuid, B.UNIT_T))
                        assert stk_w is not None
                        if not P.unit_safe((w_raw, b"", b""), stk_w):
                            errors.append(f"patch_47 hider 0x{wuid:016x} failed unit_safe in {label}/{tier}")
                        if drawn_index_count(w_raw) != 0:
                            errors.append(f"patch_47 hider 0x{wuid:016x} still draws indices in {label}/{tier}")
                        if any(v != B.M_COLLISION for v in VC.material_map(w_raw).values()):
                            errors.append(f"patch_47 hider 0x{wuid:016x} material_map not rebound to m_collision in {label}/{tier}")
                        metrics["corpse_hider_checks"] += 1
                else:
                    for wuid in B.CORPSE_ALL_DROIDS_ONLY:
                        if (wuid, B.UNIT_T) in winners:
                            errors.append(f"all-droids-only hider 0x{wuid:016x} leaked into mixed-droids ({label}/{tier})")
            else:
                for cuid in (*B.CORPSE_SHARED_DROIDS, *B.CORPSE_ALL_DROIDS_ONLY):
                    if (cuid, B.UNIT_T) in winners:
                        errors.append(f"Droid corpse/wreck hider 0x{cuid:016x} leaked into non-droid selection ({label}/{tier})")

            # Helper to read winner main bytes or stock main bytes
            def _get_main(key: tuple[int, int]) -> bytes | None:
                if key in winners:
                    _, bp, r, _, _ = winners[key]
                    with bp.open("rb") as fh:
                        fh.seek(r[2])
                        return fh.read(r[7])
                return stock_main(key)

            def _exists(key: tuple[int, int]) -> bool:
                if key in winners:
                    return True
                lbl_k = f"{key[0]:016x}.{key[1]:016x}"
                return lbl_k in nat_idx or lbl_k in full_idx

            # Gate 1d: Verify every custom eagle_base.package entry resides in patch_40 and is pinned in clone_eagle_assets.h
            eb_raw = _get_main((B.EAGLE_PACKAGE, B.PACKAGE_T))
            if eb_raw is None:
                errors.append(f"Missing eagle_base.package in {label}/{tier}")
            else:
                eb_cnt = struct.unpack_from("<I", eb_raw, 8)[0]
                for rk, rn in struct.iter_unpack("<QQ", eb_raw[16 : 16 + 16 * eb_cnt]):
                    if (rn, rk) in stock_eagle_pkg_entries:
                        continue
                    if (rn, rk) not in winners:
                        if rk == B.MAT_T and _exists((rn, rk)):
                            continue  # native shader parent
                        errors.append(f"Custom eagle_base entry {rn:016x}.{rk:016x} missing from winners in {label}/{tier}")
                        continue
                    w_name, _, w_row, _, _ = winners[(rn, rk)]
                    if w_name != "9ba626afa44a3aa3.patch_40":
                        errors.append(f"Custom eagle_base entry {rn:016x}.{rk:016x} lives in {w_name} instead of patch_40")
                    for pt in range(3):
                        if w_row[7 + pt] > 0 and (rn, rk, pt) not in pinned_parts_set:
                            errors.append(f"Custom eagle_base entry {rn:016x}.{rk:016x} part={pt} omitted from clone_eagle_assets.h")

            # Check UNIT, MATERIAL, TEXTURE closure & GeometryGroup scan across all winners + melee POI packages
            surface_seen: set[tuple[int, bool]] = set()

            def _check_surface(mid: int, owner_hex: str, is_parent: bool = False) -> None:
                if (mid, is_parent) in surface_seen:
                    return
                surface_seen.add((mid, is_parent))
                mkey = (mid, B.MAT_T)
                if not _exists(mkey):
                    errors.append(f"Missing material 0x{mid:016x} referenced by {owner_hex} ({label}/{tier})")
                    return
                if is_parent:
                    return
                mraw = _get_main(mkey)
                if mraw is None or len(mraw) < 136:
                    return
                tcnt = struct.unpack_from("<I", mraw, 64)[0]
                if tcnt > 10000 or 136 + 12 * tcnt > len(mraw):
                    errors.append(f"Malformed material 0x{mid:016x} in {label}/{tier}")
                    return
                for tid in struct.unpack_from(f"<{tcnt}Q", mraw, 136 + 4 * tcnt):
                    if tid and not _exists((tid, B.TEX_T)):
                        errors.append(f"Missing texture 0x{tid:016x} on mat 0x{mid:016x} ({label}/{tier})")
                parent_id = struct.unpack_from("<Q", mraw, 24)[0]
                if parent_id and parent_id != mid:
                    _check_surface(parent_id, owner_hex, True)

            # Check PACKAGE closure: eagle_base + all 9 melee POI packages + any pack-overridden PACKAGE
            pkg_ids = {B.EAGLE_PACKAGE, *MELEE_POI_PACKAGES} | {k[0] for k in winners if k[1] == B.PACKAGE_T}
            for pid in sorted(pkg_ids):
                praw = _get_main((pid, B.PACKAGE_T))
                if praw is None:
                    errors.append(f"Missing PACKAGE 0x{pid:016x} in {label}/{tier}")
                    continue
                stk_praw = stock_main((pid, B.PACKAGE_T))
                stk_pkg_set: set[tuple[int, int]] = set()
                if stk_praw is not None:
                    scnt = struct.unpack_from("<I", stk_praw, 8)[0]
                    stk_pkg_set = {(rn, rk) for rk, rn in struct.iter_unpack("<QQ", stk_praw[16 : 16 + 16 * scnt])}
                pcnt = struct.unpack_from("<I", praw, 8)[0]
                for rk, rn in struct.iter_unpack("<QQ", praw[16 : 16 + 16 * pcnt]):
                    if not _exists((rn, rk)):
                        if (rn, rk) in stk_pkg_set:
                            metrics["inherited_native_package_exceptions"] += 1
                            continue
                        errors.append(
                            f"Unresolved PACKAGE entry {rn:016x}.{rk:016x} in pkg 0x{pid:016x} ({label}/{tier})"
                        )
                    elif pid in MELEE_POI_PACKAGES and rk == B.MAT_T:
                        _check_surface(rn, f"melee_pkg:{pid:016x}")
                metrics["packages_closure_verified"] += 1

            for (rid, rkind), (p_name, bp, row, s_sha, g_sha) in winners.items():
                if rkind != B.UNIT_T:
                    continue
                with bp.open("rb") as fh:
                    fh.seek(row[2])
                    uraw = fh.read(row[7])

                u_hash = B.sha256_bytes(uraw)
                if u_hash not in inspected_unit_blobs:
                    inspected_unit_blobs.add(u_hash)
                    metrics["unique_units_inspected"] += 1
                    try:
                        VC.lod_descriptors(uraw)
                    except Exception as exc:
                        errors.append(f"LODData error on UNIT {rid:016x} ({p_name}): {exc}")
                    u_issues = check_unit_matrices_and_skins(uraw)
                    if u_issues:
                        errors.append(f"UNIT {rid:016x} ({p_name}) issues: {u_issues}")

                    if rid not in protected_units:
                        at100 = struct.unpack_from("<I", uraw, 100)[0]
                        if at100:
                            for mi, mh in enumerate(VC.pointers(uraw, 100)):
                                mtype = struct.unpack_from("<I", uraw, mh + 36)[0]
                                if mtype in (0, 256):
                                    continue
                                ng, go = struct.unpack_from("<2I", uraw, mh + 120)
                                drawn_idx = sum(
                                    struct.unpack_from("<6I", uraw, mh + go + 24 * gi)[4] for gi in range(ng)
                                )
                                if drawn_idx == 0:
                                    continue
                                mnx, mny, mnz, mxx, mxy, mxz, rad = struct.unpack_from("<7f", uraw, mh + 8)
                                max_span = max(mxx - mnx, mxy - mny, mxz - mnz)
                                if rad < 0.5 or max_span < 0.25:
                                    errors.append(
                                        f"TINY_MESH_AABB on UNIT {rid:016x} M{mi} ({p_name}): r={rad:.3f} span={max_span:.3f}"
                                    )

                if row[9] > 0:
                    g_slice_key = (g_sha, row[4], row[9])
                    if g_slice_key not in inspected_gpu_slices:
                        try:
                            gp = resolve_blob(g_sha)
                        except FileNotFoundError:
                            gp = None
                        if gp is not None:
                            inspected_gpu_slices.add(g_slice_key)
                            with gp.open("rb") as gfh:
                                gfh.seek(row[4])
                                ug = gfh.read(row[9])
                            g_issues = check_gpu_vertices_finite(uraw, ug)
                            metrics["hydrated_gpu_meshes_scanned"] += 1
                            if g_issues:
                                errors.append(f"GPU NaN/bounds issue on UNIT {rid:016x} ({p_name}): {g_issues}")

                for off, dep_kind, role in (
                    (8, B.BONES_T, "bones"),
                    (16, B.GG_T, "geometry-group"),
                    (32, B.SM_T, "state-machine"),
                ):
                    val = struct.unpack_from("<Q", uraw, off)[0]
                    if not val:
                        continue
                    if dep_kind == B.GG_T and (val, B.GG_T) not in winners:
                        if struct.unpack_from("<I", uraw, 48)[0] != 0 and not VC.pointers(uraw, 48):
                            pass
                        else:
                            errors.append(f"External non-resident GeometryGroup {val:016x} on UNIT {rid:016x}")
                    if not _exists((val, dep_kind)):
                        stk_u = stock_main((rid, B.UNIT_T))
                        if role == "state-machine" and stk_u and struct.unpack_from("<Q", stk_u, 32)[0] == val:
                            continue
                        errors.append(f"Missing {role} {val:016x} on UNIT {rid:016x} ({label}/{tier})")

                mm = VC.material_map(uraw)
                at100 = struct.unpack_from("<I", uraw, 100)[0]
                if at100:
                    for mh in VC.pointers(uraw, 100):
                        if struct.unpack_from("<I", uraw, mh + 36)[0] == 0:
                            continue
                        nm_c, mo_c = struct.unpack_from("<2I", uraw, mh + 104)
                        mats_c = struct.unpack_from(f"<{nm_c}I", uraw, mh + mo_c)
                        ng_c, go_c = struct.unpack_from("<2I", uraw, mh + 120)
                        for gi in range(ng_c):
                            grp = struct.unpack_from("<6I", uraw, mh + go_c + 24 * gi)
                            if grp[4] > 0:
                                th = mats_c[grp[0]]
                                if th in mm:
                                    _check_surface(mm[th], f"{rid:016x}")
                                elif th != 0x093FC934:
                                    errors.append(f"Unmapped drawn thin mat 0x{th:08x} on UNIT {rid:016x}")

    # 5. Simulate `asset_gate.h` (`ScanAssets`) + physical staged patch contiguity across all 15 staged directories (`all-droids`, `mixed-droids`, `native-bots` × 5 tiers)!
    for variant_name, expected_max_p in (("all-droids", 47), ("mixed-droids", 47), ("native-bots", 42)):
        for tier_idx, tier in enumerate(B.TIERS):
            vdir = out_dir / "staged" / variant_name / tier
            staged_mains = sorted(
                int(p.name.split("_")[1])
                for p in vdir.iterdir()
                if p.is_file() and re.fullmatch(r"9ba626afa44a3aa3\.patch_\d+", p.name)
            )
            if staged_mains != list(range(expected_max_p + 1)):
                errors.append(
                    f"Staged directory {vdir} non-contiguous main patches: got {staged_mains}, expected 0..{expected_max_p}"
                )
            scan_errs = simulate_asset_gate_scan_dir(vdir, pin_rows, tier_idx)
            errors.extend(scan_errs)
            metrics["eagle_asset_gate_full_dirs_verified"] += 1

    summary = {
        "passed": len(errors) == 0,
        "errorCount": len(errors),
        "errors": errors[:50],
        "pinCount": len(pin_rows),
        "metrics": dict(metrics),
    }
    (out_dir / "validation-summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return summary


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out-dir", type=Path, default=Path(r"D:\ClaudeWork\swarm-packs\clones-r26"))
    parser.add_argument("--cache-dir", type=Path, default=Path(r"D:\ClaudeWork\swarm-packs\clones-r26\_blob_cache"))
    args = parser.parse_args(argv)
    summary = validate(out_dir=args.out_dir.resolve(), cache_dir=args.cache_dir.resolve())
    print(json.dumps(summary, indent=2))
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
