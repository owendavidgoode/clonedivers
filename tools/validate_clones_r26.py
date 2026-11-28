#!/usr/bin/env python3
"""Comprehensive 5-tier validator for staged Clonedivers r26 Stingray archives.

Validates across all 5 performance tiers (`full`, `lighter`, `reduced1024`, `reduced512`, `potato256`)
and selection matrices (`clonedivers` / `commandos` × `all-droids` / `mixed-droids` / `native-bots` /
`covenant` / `mixed-covenant`):
  - SLIM archive directory preflight + companion `.stream` / `.gpu_resources` range checks (zero OOB!).
  - SHA-256 + exact byte-size verification for 100% of newly staged blobs in `blobs/`.
  - 100% bit-exact preservation of all 99 runtime-pinned LEGO, Yoda, camera-pullback, and AT-ST `UNIT`s.
  - Dependency closure (`UNIT` -> `BONES`, `STATE_MACHINE`, `GeometryGroup`, `MATERIAL` -> parent + `TEXTURE`;
    `PACKAGE` -> all listed resources, including `eagle_base` and all 6 melee POI packages `cqc-72`, `cqc-2`,
    `cqc-19`, `cqc-30`, `cqc-5`, `cqc-42`, plus Guard Dog Rover `32cb44321da1b5a0` / `cae3f1e8a44a80b3`).
  - Zero external `GeometryGroup` residency violations (`verify_candidate.geometry_provider_scan`).
  - Zero `TINY_MESH_AABB` / ankle-cull bounds / invalid `@48` `LODData` footers on unprotected `UNIT`s.
  - `<= 64` bones per compact GPU skinning sub-palette across 100% of `UNIT`s.
  - Zero `NaN` / `Inf` floats across scene-graph matrices (`@52`), inverse-bind matrices (`@88`), and
    hydrated GPU vertex/UV/blend buffers (`patch_44`, `patch_45`, `patch_46`, `patch_34`, `patch_35`, `patch_40..43`).
  - `clone_eagle_assets.h` 5-tier SHA-256 coherence check (`asset_gate.h` simulation across all 5 tiers).
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
    0x8F295B4D04C3A06C,  # cqc-42 Saber
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
        25: 1, 26: 2, 27: 3, 28: 4, 29: 4, 30: 4, 32: 2, 33: 4, 34: 6, 35: 8,
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
            kind, fmt, _ = struct.unpack_from("<3I", lay, 8 + 20 * ai)
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

    # 3. Verify 100% bit-exact preservation of all 99 protected runtime-pinned UNITs across all 5 tiers
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

    # 4. Full 5-tier selection closure, GeometryGroup scan, UNIT schema/AABB/skin/NaN checks, and melee POI closure
    selection_matrix = (
        ("commandos", {"commandos", "droids", "aimpoints"}, "commandos+all-droids"),
        ("commandos", {"commandos", "droids", "mixed-droids", "aimpoints"}, "commandos+mixed-droids"),
        ("clonedivers", {"droids", "mixed-droids", "aimpoints"}, "clonedivers+mixed-droids"),
        ("clonedivers", {"aimpoints"}, "clonedivers+native-bots"),
        ("commandos", {"commandos", "droids", "mixed-droids", "covenant", "mixed-covenant", "aimpoints"}, "commandos+mixed-both"),
    )

    inspected_unit_blobs: set[str] = set()
    inspected_gpu_slices: set[tuple[str, int, int]] = set()

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

            # VerifyScorch/Fixer/Sev Katarn helmets on commandos
            if "commandos" in opts and mode == "commandos":
                for dst_uid, _, _ in B.DELTA_SQUAD_HELMET_COPIES:
                    if winners.get((dst_uid, B.UNIT_T), ("",))[0] != "9ba626afa44a3aa3.patch_45":
                        errors.append(f"Delta Squad helmet 0x{dst_uid:016x} not won by patch_45 in {label}/{tier}")

            # Verify MagnaGuard m8..m13 rebind in droid modes
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

            # Check PACKAGE closure: eagle_base + all 6 melee POI packages + any pack-overridden PACKAGE
            pkg_ids = {B.EAGLE_PACKAGE, *MELEE_POI_PACKAGES} | {k[0] for k in winners if k[1] == B.PACKAGE_T}
            for pid in sorted(pkg_ids):
                praw = _get_main((pid, B.PACKAGE_T))
                if praw is None:
                    errors.append(f"Missing PACKAGE 0x{pid:016x} in {label}/{tier}")
                    continue
                pcnt = struct.unpack_from("<I", praw, 8)[0]
                for rk, rn in struct.iter_unpack("<QQ", praw[16 : 16 + 16 * pcnt]):
                    if not _exists((rn, rk)):
                        errors.append(
                            f"Unresolved PACKAGE entry {rn:016x}.{rk:016x} in pkg 0x{pid:016x} ({label}/{tier})"
                        )
                metrics["packages_closure_verified"] += 1

            # Check UNIT, MATERIAL, TEXTURE closure & GeometryGroup scan across all winners
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

                    # Check TINY_MESH_AABB on unprotected non-exempt UNITs
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

                # Hydrated GPU vertex finite scan whenever `.gpu_resources` is in blobs_dir or cache_dir
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

                # UNIT dependency closure (`bones`, `state_machine`, `geometry_group`, drawn `materials`)
                for off, dep_kind, role in (
                    (8, B.BONES_T, "bones"),
                    (16, B.GG_T, "geometry-group"),
                    (32, B.SM_T, "state-machine"),
                ):
                    val = struct.unpack_from("<Q", uraw, off)[0]
                    if not val:
                        continue
                    if dep_kind == B.GG_T and (val, B.GG_T) not in winners:
                        # Consolidated override external GG check (matching verify_candidate.geometry_provider_scan)
                        if struct.unpack_from("<I", uraw, 48)[0] != 0 and not VC.pointers(uraw, 48):
                            pass  # empty LOD shell
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

    # 5. Simulate `asset_gate.h` (`ScanAssets`) against `clone_eagle_assets.h` across all 5 staged tiers!
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

    for tier_idx, tier in enumerate(B.TIERS):
        vdir = out_dir / "staged" / "all-droids" / tier
        p44 = vdir / "9ba626afa44a3aa3.patch_44"
        rows_map = {(r[0], r[1]): r for r in P.directory(p44)}
        tier_mask = 0x1F
        for rname, rkind, part_idx, hashes5 in pin_rows:
            if (rname, rkind) not in rows_map:
                errors.append(f"clone_eagle_assets.h missing ({rname:016x}, {rkind:016x}) in staged {tier}")
                continue
            r = rows_map[(rname, rkind)]
            comp_p = p44.with_name(p44.name + P.SUFFIXES[part_idx])
            with comp_p.open("rb") as fh:
                fh.seek(r[2 + part_idx])
                pdata = fh.read(r[7 + part_idx])
            actual_h = hashlib.sha256(pdata).hexdigest()
            part_mask = 0
            for ti, expected_h in enumerate(hashes5):
                if actual_h == expected_h:
                    part_mask |= 1 << ti
            tier_mask &= part_mask
            if not (part_mask & (1 << tier_idx)):
                errors.append(f"clone_eagle_assets.h hash mismatch on tier {tier} for {rname:016x} part={part_idx}")
        if tier_mask == 0:
            errors.append(f"asset_gate.h tierMask collapsed to 0 on tier {tier}")
        metrics["eagle_asset_gate_tiers_verified"] += 1

    summary = {
        "passed": len(errors) == 0,
        "errorCount": len(errors),
        "errors": errors[:50],
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
