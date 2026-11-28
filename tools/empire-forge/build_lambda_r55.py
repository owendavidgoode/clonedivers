#!/usr/bin/env python3
"""Build EmpireDivers r55 Lambda shuttle fixes across all 5 tiers (patch_1036 & patch_1038).

Fixes "extraction shuttle placement weird, no lamda movement" with 0 B .stream churn:
1. Pad Placement (ΔY = +2.68 m on Extraction Lambda ef3a4136b21592cb):
   - Shifts XWA Lambda hull/cockpit/folding-wing geometry (grp[0..13] in layout[1] & layout[2])
     forward by +2.68 m across all 5 tiers so nose (+5.41 m) & cabin ([-4.78, +1.25]) align with
     Stock Pelican-1 (+5.34 m nose, Havok walk-in cabin, and Pelican-aligned animated boarding
     ramp & hydraulic landing gear in grp[14..15] at Y = [-4.30, -1.99], Z = 0.00 m).
   - Shifts scene-graph wing hinge nodes (223, 224), skin[0] wing inverse-bind matrices, and all
     non-additive patch_1036 animation local_pivots by ΔY = +2.68 m so wing rotation axes stay exact.
2. Animated Movement & NaN-Free Rig (patch_1036 & patch_1038):
   - Eliminates all NaN float32 scales on bones 134, 135 in Layer[14]'s 12 additive clips using
     native compressed additive identity bytes (ff7fff7fff7f03082080ff7fff7fff7f).
   - Fixes Layer[17] extraction departure clips (st[2..7]: bbe524ccd3d67c52, f34f6aa2f2105e08,
     b9330538e1cfefd2, f09d4b0f0008ab88, 0444a4355abe8cc6): holds 135.53 deg folded on the pad
     during t = 0..1.90s while the boarding ramp closes and shuttle clears the ground, then smoothly
     unfolds 135.53 -> 0.0 deg via cosine S-curve over t = 1.90..6.20s in full view of the camera.
   - Fixes Layer[17] fly-in approach clip (st[8]: 2914ce19dbe5b5ea, 16.67s): holds 0.0 deg in flight
     (0..11.0s), smoothly folds 0.0 -> 135.53 deg over 11.0..16.0s for landing.
   - Fixes Layer[17] tutorial extraction clip (st[22]: 3fd0be71dd7e3cc1, 55.0s): holds 135.53 deg on
     pad (0..34.3s), smoothly unfolds 135.53 -> 0.0 deg over 34.3..38.8s after liftoff.
   - Smooths Layer[1] landing_gear_down (f13ce1c7b113d63f) & landing_gear_up (54ee79bffaf7cc1e)
     wing fold/unfold curves with cosine easing.
   - Compacts skin[0] in-place to <= 64 bones on ef3a4136b21592cb (148 -> 19) and 18f11a4e18192cfe
     (86 -> 5), fixes MeshObject (@100) and LOD footer (@48) AABBs on ef3a4136b21592cb,
     18f11a4e18192cfe, and 75be82ed8592a6b3, and preserves all 4 AT-ST units 100% bit-exact.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import shutil
import struct
import sys

import numpy as np

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import empire_port as P
import verify_candidate as VC

ANIMATION = 0x931E336D7646CC26
UNIT = 0xE0A48D0BE9A7453F
DELTA_Y = 2.68

DEFAULT_R54_EXP = Path(r"D:\Clonedivers-HellForge-cache\exports\empire-017fd5e5140344768bd55de41dcd6789")
DEFAULT_BLOB_CACHE = Path(r"D:\ClaudeWork\cache\r54-blobs")
DEFAULT_STEAM_DATA = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2\data")
DEFAULT_NAT_IDX = Path(r"D:\ClaudeWork\empire-forge\native-index-25480438.json")
DEFAULT_BK_TOOLS = Path(r"D:\ClaudeWork\backups\c-checkout-20261109-172437\files\tools")
DEFAULT_OUT_ROOT = Path(r"D:\ClaudeWork\swarm-packs\empire-r55\patches")

ADDITIVE_IDENTITY_HEX = "ff7fff7fff7f03082080ff7fff7fff7f"

EXTRACT_LIFTOFF_CLIPS = {
    0xBBE524CCD3D67C52,  # mission_extract_1
    0xF34F6AA2F2105E08,  # mission_extract_2
    0xB9330538E1CFEFD2,  # mission_extract_3
    0xF09D4B0F0008AB88,  # mission_extract_4
    0x0444A4355ABE8CC6,  # mission_extract_city
}
APPROACH_FLYIN_CLIP = 0x2914CE19DBE5B5EA
TUTORIAL_EXTRACT_CLIP = 0x3FD0BE71DD7E3CC1
GEAR_DOWN_FOLD_CLIP = 0xF13CE1C7B113D63F   # 0.0 -> 1.0 over 2.33s
GEAR_UP_UNFOLD_CLIP = 0x54EE79BFFAF7CC1E   # 1.0 -> 0.0 over 2.33s

ATST_UNITS = (
    0x79E4B3D2DA5E45E3,
    0xC2D449ECF7FACAB1,
    0x7B2326F6FD9C8069,
    0x35DBF54F016F3624,
)

# Expected r54 input SHA-256s
P1036_INPUTS = {
    "full_lighter": ("exp", "9ba626afa44a3aa3.patch_1036", "30b8586132d459669e2ed9f811524dbf1e4a5931286afe0a3f3195049012e7ff"),
    "reduced1024_512": ("blob", "4aa586fa6bdbf848a5941d0b9b9aedbfe41d897e23235e144c0a18588c449425", "4aa586fa6bdbf848a5941d0b9b9aedbfe41d897e23235e144c0a18588c449425"),
    "potato256": ("blob", "7afe5bdcd6f58fd59b6dbeeac0687003b7fda67d59536bb149f6d035fcb8dfad", "7afe5bdcd6f58fd59b6dbeeac0687003b7fda67d59536bb149f6d035fcb8dfad"),
}

P1038_MAIN_INPUTS = {
    "full": ("exp", "9ba626afa44a3aa3.patch_1038", "e5cc07179c53b851d3ffee5feaf79fe122c600b086532cfcc2c882d81941b194"),
    "lighter": ("blob", "a50820638acad497f020be1065b00e49dbe8165457f36f5bab765f15f7c46179", "a50820638acad497f020be1065b00e49dbe8165457f36f5bab765f15f7c46179"),
    "reduced1024": ("blob", "028eb1aeb4e2f0cbaccdc611a115952fec5b6fdc3118d90ed81112b246df6fbd", "028eb1aeb4e2f0cbaccdc611a115952fec5b6fdc3118d90ed81112b246df6fbd"),
    "reduced512": ("blob", "85850f03e72c286e9ba7167805d5dde42ea02acf12b9b2545d7b603331706941", "85850f03e72c286e9ba7167805d5dde42ea02acf12b9b2545d7b603331706941"),
    "potato256": ("blob", "2638159c39db51272a22ec12462b599a2315d7a47b7774014986d3f8e79f708a", "2638159c39db51272a22ec12462b599a2315d7a47b7774014986d3f8e79f708a"),
}

P1038_GPU_INPUTS = {
    "full": ("exp", "9ba626afa44a3aa3.patch_1038.gpu_resources", "a389075d25514d793cee9b2820548ca4ff40a3dab5b3d977e501d9c9f5a7e75d"),
    "lighter": ("blob", "1cd01a111145a6a1300d193c98a1d37d86f1c26122dc5af4ea028074a9a555b2", "1cd01a111145a6a1300d193c98a1d37d86f1c26122dc5af4ea028074a9a555b2"),
    "reduced1024_512": ("blob", "12094937f75bc52f36d065c9694de58a2f44cefa71894c8c064d4306d02b2a17", "12094937f75bc52f36d065c9694de58a2f44cefa71894c8c064d4306d02b2a17"),
    "potato256": ("blob", "671c757030868e0388127e6b4cc4125cb6627d433a2a859e269bcce22c208d3c", "671c757030868e0388127e6b4cc4125cb6627d433a2a859e269bcce22c208d3c"),
}


def load_mod(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    if spec is None or spec.loader is None:
        raise ImportError(f"Cannot load {path}")
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def file_sha256(path: Path) -> str:
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def resolve_input(spec_tuple: tuple[str, str, str], r54_exp: Path, blob_cache: Path) -> Path:
    src_kind, rel, expected_sha = spec_tuple
    p = (r54_exp / rel) if src_kind == "exp" else (blob_cache / rel)
    if not p.is_file():
        raise FileNotFoundError(f"Missing cached input {p}")
    actual = file_sha256(p)
    if actual != expected_sha:
        raise ValueError(f"SHA-256 mismatch for {p}: expected {expected_sha}, got {actual}")
    return p


def quat_for_factor(axis: np.ndarray, full_angle: float, factor: float) -> tuple[float, float, float, float]:
    half = 0.5 * full_angle * factor
    s = math.sin(half)
    c = math.cos(half)
    return (float(axis[0] * s), float(axis[1] * s), float(axis[2] * s), float(c))


def build_cosine_wing_entries(
    LC,
    length: float,
    axes: list[np.ndarray],
    angles: list[float],
    f_start: float,
    f_end: float,
    t0: float,
    t1: float,
) -> list[dict]:
    """Generate smooth 30 Hz cosine S-curve rotation keyframes on bones 134 and 135."""
    times: list[float] = []
    if t0 > 0.001:
        times.append(0.0)
    steps = max(2, math.ceil((t1 - t0) * 30.0))
    for k in range(steps + 1):
        times.append(t0 + (t1 - t0) * k / steps)
    if length - t1 > 0.001:
        times.append(length)

    entries = []
    for i in range(2):
        bone_idx = 134 + i
        for tm in times:
            if tm <= t0:
                fac = f_start
            elif tm >= t1:
                fac = f_end
            else:
                u = (tm - t0) / (t1 - t0)
                ease = 0.5 * (1.0 - math.cos(math.pi * u))
                fac = f_start + (f_end - f_start) * ease
            q = quat_for_factor(axes[i], angles[i], fac)
            entries.append({"bone": bone_idx, "time": tm, "bytes": LC.extended_rotation(bone_idx, tm, q)})
    return entries


def pack_animation(LC, parsed: dict, flags_bytes: bytes, initial_list: list[bytes], entries_list: list[dict]) -> bytes:
    count = parsed["count"]
    result = bytearray(parsed["prefix"])
    struct.pack_into("<I", result, 4, count)
    result.extend(flags_bytes)
    result.extend(b"".join(initial_list))
    result.extend(parsed["hashFloats"])
    result.extend(b"".join(e["bytes"] for e in entries_list))
    section_size = len(result) + 6
    result.extend(struct.pack("<HI", 3, section_size - 4))
    struct.pack_into("<I", result, 12, 0)
    result.extend(bytes(result))
    struct.pack_into("<I", result, 12, len(result))
    out_bytes = bytes(result)

    # Full roundtrip verification
    chk = LC.animation_parts(out_bytes)
    if chk["initial"][:134] != parsed["initial"][:134]:
        raise RuntimeError("Native initial transforms [0..133] changed")
    native_orig = [e for e in parsed["entries"] if e["bone"] < 134 or e.get("subtype") == 2]
    native_chk = [e for e in chk["entries"] if e["bone"] < 134 or e.get("subtype") == 2]
    if native_chk != native_orig or chk["hashFloats"] != parsed["hashFloats"]:
        raise RuntimeError("Native animation channels or hashFloats changed")
    return out_bytes


def transform_patch_1036_animations(LC, orig_p1036: Path) -> tuple[dict[int, bytes], dict]:
    """Transform all 66 ANIMATION resources in patch_1036."""
    raw_all = orig_p1036.read_bytes()
    rows = P.directory(orig_p1036)
    anim_rows = [r for r in rows if r[1] == ANIMATION]
    if len(anim_rows) != 66:
        raise RuntimeError(f"Expected 66 ANIMATIONs in patch_1036, got {len(anim_rows)}")

    # Extract exact wing fold axes & full fold angles from idle_landing (0c9c8283c1d854d3)
    idle_row = next(r for r in anim_rows if r[0] == 0x0C9C8283C1D854D3)
    idle_parsed = LC.animation_parts(raw_all[idle_row[2] : idle_row[2] + idle_row[7]])
    pivots: list[tuple[float, float, float]] = []
    axes: list[np.ndarray] = []
    angles: list[float] = []
    for bi in (134, 135):
        px, py, pz, qx, qy, qz, qw, sx, sy, sz = struct.unpack_from("<10f", idle_parsed["initial"][bi], 0)
        pivots.append((px, py + DELTA_Y, pz))
        q_vec = np.array([qx, qy, qz], dtype=float)
        nrm = float(np.linalg.norm(q_vec))
        ang = 2.0 * math.atan2(nrm, qw)
        axes.append(q_vec / nrm)
        angles.append(ang)

    additive_init = bytes.fromhex(ADDITIVE_IDENTITY_HEX)
    rebuilt: dict[int, bytes] = {}
    counts = {
        "additive_nan_fixed": 0,
        "extract_liftoff_fixed": 0,
        "approach_flyin_fixed": 0,
        "tutorial_extract_fixed": 0,
        "gear_transition_smoothed": 0,
        "pivot_only_updated": 0,
    }

    for r in anim_rows:
        aid = r[0]
        araw = raw_all[r[2] : r[2] + r[7]]
        parsed = LC.animation_parts(araw)
        if parsed["count"] != 136:
            raise RuntimeError(f"Unexpected bone count {parsed['count']} in {aid:016x}")

        comp134 = [bool(parsed["flags"][(3 * 134 + k) // 8] & (1 << ((3 * 134 + k) % 8))) for k in range(3)]
        init134_floats = struct.unpack_from("<10f", parsed["initial"][134], 0) if not any(comp134) else ()
        is_additive = bool(init134_floats and any(math.isnan(x) for x in init134_floats))

        flags = bytearray(parsed["flags"])
        initial = list(parsed["initial"])
        base_entries = [e for e in parsed["entries"] if e["bone"] not in (134, 135) or e.get("subtype") == 2]

        if is_additive:
            # Set compression bits for bones 134 & 135 to [True, True, True] and write native compressed additive identity
            for bit_idx in range(3 * 134, 3 * 136):
                flags[bit_idx // 8] |= 1 << (bit_idx % 8)
            initial[134] = additive_init
            initial[135] = additive_init
            rebuilt[aid] = pack_animation(LC, parsed, bytes(flags), initial, base_entries)
            counts["additive_nan_fixed"] += 1
            continue

        length = float(parsed["length"])
        if aid in EXTRACT_LIFTOFF_CLIPS:
            for i in range(2):
                q0 = quat_for_factor(axes[i], angles[i], 1.0)
                initial[134 + i] = struct.pack("<3f4f3f", *pivots[i], *q0, 1.0, 1.0, 1.0)
            wing_ents = build_cosine_wing_entries(LC, length, axes, angles, 1.0, 0.0, 1.90, 6.20)
            rebuilt[aid] = pack_animation(LC, parsed, bytes(flags), initial, base_entries + wing_ents)
            counts["extract_liftoff_fixed"] += 1
        elif aid == APPROACH_FLYIN_CLIP:
            for i in range(2):
                q0 = quat_for_factor(axes[i], angles[i], 0.0)
                initial[134 + i] = struct.pack("<3f4f3f", *pivots[i], *q0, 1.0, 1.0, 1.0)
            wing_ents = build_cosine_wing_entries(LC, length, axes, angles, 0.0, 1.0, 11.00, 16.00)
            rebuilt[aid] = pack_animation(LC, parsed, bytes(flags), initial, base_entries + wing_ents)
            counts["approach_flyin_fixed"] += 1
        elif aid == TUTORIAL_EXTRACT_CLIP:
            for i in range(2):
                q0 = quat_for_factor(axes[i], angles[i], 1.0)
                initial[134 + i] = struct.pack("<3f4f3f", *pivots[i], *q0, 1.0, 1.0, 1.0)
            wing_ents = build_cosine_wing_entries(LC, length, axes, angles, 1.0, 0.0, 34.30, 38.80)
            rebuilt[aid] = pack_animation(LC, parsed, bytes(flags), initial, base_entries + wing_ents)
            counts["tutorial_extract_fixed"] += 1
        elif aid in (GEAR_DOWN_FOLD_CLIP, GEAR_UP_UNFOLD_CLIP):
            f_s, f_e = (0.0, 1.0) if aid == GEAR_DOWN_FOLD_CLIP else (1.0, 0.0)
            for i in range(2):
                q0 = quat_for_factor(axes[i], angles[i], f_s)
                initial[134 + i] = struct.pack("<3f4f3f", *pivots[i], *q0, 1.0, 1.0, 1.0)
            wing_ents = build_cosine_wing_entries(LC, length, axes, angles, f_s, f_e, 0.0, length)
            rebuilt[aid] = pack_animation(LC, parsed, bytes(flags), initial, base_entries + wing_ents)
            counts["gear_transition_smoothed"] += 1
        else:
            # Update only local_pivots Y += DELTA_Y, keeping existing initial rotation & existing wing entries
            for i in range(2):
                old_vals = list(struct.unpack_from("<10f", initial[134 + i], 0))
                old_vals[0], old_vals[1], old_vals[2] = pivots[i]
                if any(not math.isfinite(x) for x in old_vals):
                    raise RuntimeError(f"Non-finite float in {aid:016x} bone {134 + i}")
                initial[134 + i] = struct.pack("<10f", *old_vals)
            rebuilt[aid] = pack_animation(LC, parsed, bytes(flags), initial, parsed["entries"])
            counts["pivot_only_updated"] += 1

        # Verify 0 NaN floats in initial uncompressed transforms & extended entries
        chk = LC.animation_parts(rebuilt[aid])
        for bi in (134, 135):
            c_flags = [bool(chk["flags"][(3 * bi + k) // 8] & (1 << ((3 * bi + k) % 8))) for k in range(3)]
            if not any(c_flags):
                vals = struct.unpack_from("<10f", chk["initial"][bi], 0)
                if not all(math.isfinite(x) for x in vals):
                    raise RuntimeError(f"NaN/Inf in rebuilt initial[{bi}] of {aid:016x}")
        for ent in chk["entries"]:
            if ent["bone"] in (134, 135) and ent.get("subtype") == 5:
                vals = struct.unpack_from("<f4f", ent["bytes"], 6)
                if not all(math.isfinite(x) for x in vals):
                    raise RuntimeError(f"NaN/Inf in rebuilt entry of {aid:016x}")

    return rebuilt, {
        "shifted_pivots": [list(round(x, 5) for x in p) for p in pivots],
        "fold_angles_deg": [round(math.degrees(a), 3) for a in angles],
        "clip_counts": counts,
    }


def repack_main_preserving_companions(orig_main: Path, replacements: dict[tuple[int, int], bytes], out_main: Path) -> dict:
    """Rewrite a Stingray .patch_N main file while keeping stream_off, gpu_off, stream_sz, gpu_sz 100% identical."""
    orig_bytes = orig_main.read_bytes()
    magic, nt, nf = struct.unpack_from("<III", orig_bytes, 0)
    if magic != 0xF0000011:
        raise ValueError(f"Invalid archive header {orig_main}")
    head_and_types = orig_bytes[: 72 + 32 * nt]
    orig_rows = P.directory(orig_main)
    toc_end = 72 + 32 * nt + 80 * nf
    start = (toc_end + 8 + 255) // 256 * 256

    out_main.parent.mkdir(parents=True, exist_ok=True)
    buf = bytearray(head_and_types + bytes(start - len(head_and_types)))
    new_rows = []
    for row in orig_rows:
        key = (row[0], row[1])
        m_payload = replacements[key] if key in replacements else orig_bytes[row[2] : row[2] + row[7]]
        buf.extend(bytes((-len(buf)) % 16))
        m_off = len(buf)
        buf.extend(m_payload)
        if key[1] == P.STREAM and len(m_payload) < 16:
            buf.extend(bytes(16 - len(m_payload)))
        new_rows.append(
            P.ROW.pack(
                row[0],
                row[1],
                m_off,
                row[3],  # exact stream_off preserved
                row[4],  # exact gpu_off preserved
                row[5],
                row[6],
                len(m_payload),
                row[8],  # exact stream_sz preserved
                row[9],  # exact gpu_sz preserved
                row[10],
                row[11],
                row[12],
            )
        )
    buf[72 + 32 * nt : 72 + 32 * nt + 80 * nf] = b"".join(new_rows)
    out_main.write_bytes(bytes(buf))
    # Verify directory roundtrip
    chk_rows = P.directory(out_main)
    if len(chk_rows) != nf:
        raise RuntimeError(f"Directory count mismatch on {out_main}")
    return {"path": str(out_main), "size": len(buf), "sha256": hashlib.sha256(buf).hexdigest()}


def compact_skin0_in_place(um: bytearray, updated_binds: dict[int, bytes] | None = None) -> tuple[int, int]:
    """Compact skin[0] (@88) in-place to only the unique bones referenced by remaps (<= 64)."""
    at88 = struct.unpack_from("<I", um, 88)[0]
    hdr_offs = [struct.unpack_from("<I", um, f)[0] for f in (48, 52, 88, 92, 96, 100, 104, 108, 112)]
    sec_end = min([o for o in hdr_offs if o > at88] + [len(um)])
    skins = VC.pointers(bytes(um), 88)
    if len(skins) != 1:
        raise RuntimeError(f"Expected 1 skin, got {len(skins)}")
    sp = skins[0]
    old_cnt, mo, bo, ro = struct.unpack_from("<4I", um, sp)
    old_binds = [bytes(um[sp + mo + 64 * i : sp + mo + 64 * (i + 1)]) for i in range(old_cnt)]
    old_bones = list(struct.unpack_from(f"<{old_cnt}I", um, sp + bo))
    nr = struct.unpack_from("<I", um, sp + ro)[0]
    old_remaps = []
    for ri in range(nr):
        r_off, r_n = struct.unpack_from("<2I", um, sp + ro + 4 + 8 * ri)
        old_remaps.append(list(struct.unpack_from(f"<{r_n}I", um, sp + ro + r_off)))

    # Preserve order of first appearance across remaps
    used_old_indices: list[int] = []
    old_to_new: dict[int, int] = {}
    for rem in old_remaps:
        for b_idx in rem:
            if b_idx not in old_to_new:
                old_to_new[b_idx] = len(used_old_indices)
                used_old_indices.append(b_idx)

    new_cnt = len(used_old_indices)
    if new_cnt == 0 or new_cnt > 64:
        raise RuntimeError(f"Compacted skin bone count {new_cnt} outside 1..64")

    new_mo = 16
    new_bo = new_mo + 64 * new_cnt
    new_ro = new_bo + 4 * new_cnt
    rem_tbl_hdr = 4 + 8 * nr

    skin_blob = bytearray()
    skin_blob.extend(struct.pack("<4I", new_cnt, new_mo, new_bo, new_ro))
    for old_i in used_old_indices:
        node_id = old_bones[old_i]
        b_bytes = updated_binds[node_id] if (updated_binds and node_id in updated_binds) else old_binds[old_i]
        if len(b_bytes) != 64:
            raise RuntimeError("Invalid 64-byte bind matrix")
        skin_blob.extend(b_bytes)
    for old_i in used_old_indices:
        skin_blob.extend(struct.pack("<I", old_bones[old_i]))

    # Pack remaps table
    rem_data = bytearray()
    rem_hdr = bytearray(struct.pack("<I", nr))
    for rem in old_remaps:
        mapped = [old_to_new[b_idx] for b_idx in rem]
        r_rel = rem_tbl_hdr + len(rem_data)
        rem_hdr.extend(struct.pack("<2I", r_rel, len(mapped)))
        rem_data.extend(struct.pack(f"<{len(mapped)}I", *mapped))
    skin_blob.extend(rem_hdr)
    skin_blob.extend(rem_data)

    avail = sec_end - sp
    if len(skin_blob) > avail:
        raise RuntimeError(f"Compacted skin ({len(skin_blob)} B) exceeds in-place capacity ({avail} B)")
    um[sp : sec_end] = skin_blob + bytes(avail - len(skin_blob))
    return old_cnt, new_cnt


def set_unit_aabbs_in_place(um: bytearray, aabb7: tuple[float, float, float, float, float, float, float]) -> None:
    """Update all @100 MeshObject AABBs (mp+8) and all @48 LODData footer AABBs (at+frel) in-place."""
    for mp in VC.pointers(bytes(um), 100):
        struct.pack_into("<7f", um, mp + 8, *aabb7)
    for at in VC.pointers(bytes(um), 48):
        footers, levels = struct.unpack_from("<2I", um, at + 12)
        for fi in range(footers):
            frel = struct.unpack_from("<I", um, at + 20 + 4 * levels + 4 * fi)[0]
            struct.pack_into("<7f", um, at + frel, *aabb7)


def patch_lambda_unit_and_gpu(GEO, um_in: bytes, ug_in: bytes | None) -> tuple[bytes, bytes | None, dict]:
    """Shift Extraction Lambda (ef3a4136b21592cb) grp[0..13] + wing nodes 223/224 by ΔY = +2.68 m,
    compact skin[0] 148 -> 19 bones, and set conservative AABBs.
    """
    um = bytearray(um_in)
    at52 = struct.unpack_from("<I", um, 52)[0]
    nn = struct.unpack_from("<I", um, at52)[0]
    if nn != 225:
        raise RuntimeError(f"Expected 225 nodes on ef3a4136b21592cb, got {nn}")

    # 1. Shift scene-graph nodes 223 & 224 local Y and world Y by +2.68 m, and compute new inverse-bind matrices
    updated_binds: dict[int, bytes] = {}
    for ni in (223, 224):
        loc_off = at52 + 16 + 64 * ni
        wld_off = at52 + 16 + 64 * nn + 64 * ni
        loc_y = struct.unpack_from("<f", um, loc_off + 40)[0] + DELTA_Y
        wld_y = struct.unpack_from("<f", um, wld_off + 52)[0] + DELTA_Y
        struct.pack_into("<f", um, loc_off + 40, loc_y)
        struct.pack_into("<f", um, wld_off + 52, wld_y)
        w_mat = np.frombuffer(bytes(um[wld_off : wld_off + 64]), dtype="<f4").reshape(4, 4).T.astype(np.float64)
        inv_w = np.linalg.inv(w_mat)
        updated_binds[ni] = inv_w.T.astype("<f4").tobytes()

    # 2. Compact skin[0] 148 -> 19 bones in-place
    old_bones_n, new_bones_n = compact_skin0_in_place(um, updated_binds)

    # 3. Update @100 MeshObject AABBs and @48 LOD footers
    set_unit_aabbs_in_place(um, (-25.0, -26.0, -22.6, 25.0, 25.0, 27.5, 47.5))

    # 4. Shift GPU vertex positions Y += +2.68 m for grp[0..13] in layout[1] & layout[2]
    ug_out = None
    gpu_info = {}
    if ug_in is not None:
        ug = bytearray(ug_in)
        layouts = VC.pointers(bytes(um), 92)
        meshes = VC.pointers(bytes(um), 100)
        # Find grp[14] v_off in mesh[10] (layout[1]) so grp[14..15] stay at ΔY = 0!
        mp10 = meshes[10]
        ng10, go10 = struct.unpack_from("<2I", um, mp10 + 120)
        if ng10 != 16:
            raise RuntimeError(f"Expected 16 groups on mesh[10], got {ng10}")
        grp14_v_off = struct.unpack_from("<6I", um, mp10 + go10 + 24 * 14)[1]

        # Shift layout[1] vertices [0 .. grp14_v_off)
        lo1 = layouts[1]
        cnt1, stride1 = struct.unpack_from("<2I", um, lo1 + 352)
        vo1, vb1, _io1, _ib1 = struct.unpack_from("<4I", um, lo1 + 416)
        if grp14_v_off > cnt1 or vo1 + vb1 > len(ug):
            raise RuntimeError("Invalid layout[1] bounds")
        arr1 = np.ndarray((grp14_v_off, 3), dtype="<f4", buffer=ug, offset=vo1, strides=(stride1, 4))
        arr1[:, 1] = (arr1[:, 1].astype(np.float64) + DELTA_Y).astype("<f4")

        # Shift layout[2] vertices [0 .. cnt2) (all 14 groups in mesh[0..3] are hull/wings/cockpit)
        lo2 = layouts[2]
        cnt2, stride2 = struct.unpack_from("<2I", um, lo2 + 352)
        vo2, vb2, _io2, _ib2 = struct.unpack_from("<4I", um, lo2 + 416)
        if vo2 + vb2 > len(ug):
            raise RuntimeError("Invalid layout[2] bounds")
        arr2 = np.ndarray((cnt2, 3), dtype="<f4", buffer=ug, offset=vo2, strides=(stride2, 4))
        arr2[:, 1] = (arr2[:, 1].astype(np.float64) + DELTA_Y).astype("<f4")

        if not (np.isfinite(arr1).all() and np.isfinite(arr2).all()):
            raise RuntimeError("Non-finite GPU vertex positions after shift")
        gpu_info = {
            "layout1_shifted_verts": int(grp14_v_off),
            "layout1_y_range": [round(float(arr1[:, 1].min()), 3), round(float(arr1[:, 1].max()), 3)],
            "layout2_shifted_verts": int(cnt2),
            "layout2_y_range": [round(float(arr2[:, 1].min()), 3), round(float(arr2[:, 1].max()), 3)],
        }
        ug_out = bytes(ug)

    # Validate scene graph, skin maps, and LOD descriptors
    GEO.worlds(bytes(um))
    smaps = GEO.skin_maps(bytes(um))
    if len(smaps[0]["bones"]) > 64:
        raise RuntimeError("skin[0] > 64 bones after compaction")
    VC.lod_descriptors(bytes(um))

    return bytes(um), ug_out, {"skin_bones_before": old_bones_n, "skin_bones_after": new_bones_n, **gpu_info}


def build_all(
    r54_exp: Path,
    blob_cache: Path,
    steam_data: Path,
    nat_idx_path: Path,
    bk_tools: Path,
    out_root: Path,
) -> dict:
    LC = load_mod("lambda_core", bk_tools / "remaining-vehicles-lambda-core.py")
    GEO = load_mod("cargo_geo", bk_tools / "r25-cargo-geometry-core.py")
    NS = load_mod("walker_ns", bk_tools / "walker-next-native.py")
    ns = NS.NativeSlim(steam_data)
    nat_idx = json.loads(nat_idx_path.read_text(encoding="utf-8"))

    def read_stock_unit(uid: int) -> bytes:
        prov = nat_idx[f"{uid:016x}.{UNIT:016x}"][0]
        hdr = ns.read(prov["archive"], 0, 72)
        _, t, c = struct.unpack_from("<III", hdr)
        arows = {(r[0], r[1]): r for r in NS.ROW.iter_unpack(ns.read(prov["archive"], 72 + 32 * t, 80 * c))}
        sr = arows[(uid, UNIT)]
        return ns.read(prov["archive"], sr[2], sr[7])

    stk_ef = read_stock_unit(0xEF3A4136B21592CB)
    stk_18 = read_stock_unit(0x18F11A4E18192CFE)

    # --- PART A: patch_1036 across all 3 tier groups (0 B GPU/stream churn!) ---
    p1036_paths = {k: resolve_input(v, r54_exp, blob_cache) for k, v in P1036_INPUTS.items()}
    rebuilt_anims, anim_meta = transform_patch_1036_animations(LC, p1036_paths["full_lighter"])
    anim_replacements = {(aid, ANIMATION): payload for aid, payload in rebuilt_anims.items()}

    p1036_outputs = {}
    for tgrp, in_path in p1036_paths.items():
        out_path = out_root / f"p1036_{tgrp}" / "9ba626afa44a3aa3.patch_1036"
        p1036_outputs[tgrp] = repack_main_preserving_companions(in_path, anim_replacements, out_path)

    # --- PART B: patch_1038 across all 5 tiers (0 B .stream churn, 100% AT-ST preservation!) ---
    p1038_mains = {k: resolve_input(v, r54_exp, blob_cache) for k, v in P1038_MAIN_INPUTS.items()}
    p1038_gpus = {k: resolve_input(v, r54_exp, blob_cache) for k, v in P1038_GPU_INPUTS.items()}

    tier_to_gpu_key = {
        "full": "full",
        "lighter": "lighter",
        "reduced1024": "reduced1024_512",
        "reduced512": "reduced1024_512",
        "potato256": "potato256",
    }

    p1038_main_outputs = {}
    p1038_gpu_outputs = {}
    written_gpus: dict[str, dict] = {}
    tier_diagnostics = {}

    for tier, m_in in p1038_mains.items():
        g_key = tier_to_gpu_key[tier]
        g_in = p1038_gpus[g_key]

        m_bytes = bytearray(m_in.read_bytes())
        rows = {r[:2]: r for r in P.directory(m_in)}

        # Record AT-ST hashes BEFORE editing
        atst_before = {}
        for a_uid in ATST_UNITS:
            ar = rows[(a_uid, UNIT)]
            with g_in.open("rb") as gf:
                gf.seek(ar[4])
                ag_sha = hashlib.sha256(gf.read(ar[9])).hexdigest()
            atst_before[a_uid] = (hashlib.sha256(m_bytes[ar[2] : ar[2] + ar[7]]).hexdigest(), ag_sha)

        # 1. Extraction Lambda (ef3a4136b21592cb)
        r_ef = rows[(0xEF3A4136B21592CB, UNIT)]
        um_ef_orig = bytes(m_bytes[r_ef[2] : r_ef[2] + r_ef[7]])
        need_gpu = g_key not in written_gpus
        ug_ef_orig = None
        if need_gpu:
            with g_in.open("rb") as gf:
                gf.seek(r_ef[4])
                ug_ef_orig = gf.read(r_ef[9])

        um_ef_new, ug_ef_new, ef_diag = patch_lambda_unit_and_gpu(GEO, um_ef_orig, ug_ef_orig)
        if len(um_ef_new) != r_ef[7]:
            raise RuntimeError("In-place main size changed on ef3a4136b21592cb")
        m_bytes[r_ef[2] : r_ef[2] + r_ef[7]] = um_ef_new

        # Verify unit_safe on ef3a4136b21592cb
        if not P.unit_safe((um_ef_new, b"", b""), stk_ef):
            raise RuntimeError(f"unit_safe failed on ef3a4136b21592cb ({tier})")

        # Write patched GPU resource in-place once per unique GPU blob
        if need_gpu and ug_ef_new is not None:
            out_gpu = out_root / f"p1038_gpu_{g_key}" / "9ba626afa44a3aa3.patch_1038.gpu_resources"
            out_gpu.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(g_in, out_gpu)
            with out_gpu.open("r+b") as gf:
                gf.seek(r_ef[4])
                gf.write(ug_ef_new)
            written_gpus[g_key] = {
                "path": str(out_gpu),
                "size": out_gpu.stat().st_size,
                "sha256": file_sha256(out_gpu),
            }
            p1038_gpu_outputs[g_key] = written_gpus[g_key]

        # 2. Personnel Lambda (18f11a4e18192cfe): compact skin[0] 86 -> 5 bones & expand AABBs in-place
        r_18 = rows[(0x18F11A4E18192CFE, UNIT)]
        um_18 = bytearray(m_bytes[r_18[2] : r_18[2] + r_18[7]])
        old18_b, new18_b = compact_skin0_in_place(um_18, None)
        set_unit_aabbs_in_place(um_18, (-16.0, -17.0, -11.0, 16.0, 11.0, 15.0, 27.5))
        if not P.unit_safe((bytes(um_18), b"", b""), stk_18):
            raise RuntimeError(f"unit_safe failed on 18f11a4e18192cfe ({tier})")
        VC.lod_descriptors(bytes(um_18))
        m_bytes[r_18[2] : r_18[2] + r_18[7]] = um_18

        # 3. Cargo/Vehicle Lambda (75be82ed8592a6b3): fix broken Z=[8.84..13.88] AABBs & LOD footer in-place
        r_75 = rows[(0x75BE82ED8592A6B3, UNIT)]
        um_75 = bytearray(m_bytes[r_75[2] : r_75[2] + r_75[7]])
        set_unit_aabbs_in_place(um_75, (-16.0, -17.0, -11.0, 16.0, 11.0, 15.0, 27.5))
        VC.lod_descriptors(bytes(um_75))
        m_bytes[r_75[2] : r_75[2] + r_75[7]] = um_75

        out_main = out_root / f"p1038_{tier}" / "9ba626afa44a3aa3.patch_1038"
        out_main.parent.mkdir(parents=True, exist_ok=True)
        out_main.write_bytes(bytes(m_bytes))

        # Verify AT-ST units are 100% bit-exact in both main and GPU!
        out_gpu_path = Path(written_gpus[g_key]["path"])
        rows_after = {r[:2]: r for r in P.directory(out_main)}
        for a_uid in ATST_UNITS:
            ar = rows_after[(a_uid, UNIT)]
            m_sha = hashlib.sha256(m_bytes[ar[2] : ar[2] + ar[7]]).hexdigest()
            with out_gpu_path.open("rb") as gf:
                gf.seek(ar[4])
                g_sha = hashlib.sha256(gf.read(ar[9])).hexdigest()
            if (m_sha, g_sha) != atst_before[a_uid]:
                raise RuntimeError(f"AT-ST {a_uid:016x} changed in {tier}!")

        p1038_main_outputs[tier] = {
            "path": str(out_main),
            "size": len(m_bytes),
            "sha256": hashlib.sha256(m_bytes).hexdigest(),
        }
        tier_diagnostics[tier] = {
            **ef_diag,
            "u18_skin_before": old18_b,
            "u18_skin_after": new18_b,
            "atst_units_bit_exact": len(ATST_UNITS),
        }

    summary = {
        "delta_y_meters": DELTA_Y,
        "patch_1036_animation_summary": anim_meta,
        "patch_1036_mains": p1036_outputs,
        "patch_1038_mains": p1038_main_outputs,
        "patch_1038_gpus": p1038_gpu_outputs,
        "tier_diagnostics": tier_diagnostics,
    }
    (out_root.parent / "lambda_r55_summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return summary


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--r54-exp", type=Path, default=DEFAULT_R54_EXP)
    ap.add_argument("--blob-cache", type=Path, default=DEFAULT_BLOB_CACHE)
    ap.add_argument("--steam-data", type=Path, default=DEFAULT_STEAM_DATA)
    ap.add_argument("--nat-idx", type=Path, default=DEFAULT_NAT_IDX)
    ap.add_argument("--bk-tools", type=Path, default=DEFAULT_BK_TOOLS)
    ap.add_argument("--out-root", type=Path, default=DEFAULT_OUT_ROOT)
    args = ap.parse_args()

    summary = build_all(
        args.r54_exp,
        args.blob_cache,
        args.steam_data,
        args.nat_idx,
        args.bk_tools,
        args.out_root,
    )
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
