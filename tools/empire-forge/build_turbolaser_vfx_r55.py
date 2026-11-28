#!/usr/bin/env python3
"""Build EmpireDivers r55 turbolaser & bug-cloud VFX fix (9ba626afa44a3aa3.patch_1035).

Fixes "some turbolasers turn blue once they hit bug clouds" without touching patch_1000..1031:
1. Cleans residual Blue-Overhaul RGB keys inside r54 patch_1035's 20 PARTICLES and 32 MATERIALs,
   and adds & recolors the 2 unpatched child PARTICLES (ccfd98974277f784, 58b9b4a05fba232e).
2. Overrides in patch_1035 the 14 native MATERIALs from patch_1000..1031 whose volumetric smoke/cloud
   scatter and laser impact vec3/vec4 shader uniforms were dyed pure blue (restoring stock neutral
   scatter on shared smoke/bug-cloud materials, Imperial green on turbolaser/orbital/cannon materials,
   and Imperial crimson red on handheld blaster materials, strictly touching vtype in (2, 3) only).
3. Overrides in patch_1035 all blue-contaminated PARTICLES from patch_1000..1031:
   - Orbital, Star Destroyer vista, Extraction/Pelican shuttle, LAS-98 Laser Cannon, Laser Sentry,
     Rover, Quasar, and heavy cannon laser PFXs -> Imperial Turbolaser Green.
   - Handheld energy/laser/plasma blaster PFXs -> Imperial Blaster Red.
   - Ballistic, grenade, explosion, shield, hellpod, mortar, and spore/smoke hit PFXs dyed neon blue
     over stock warm/neutral -> restored to clean build-25480438 stock PARTICLES bytes.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import functools
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import struct
import sys

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import empire_port as P

PARTICLES = 0xA8193123526FAD64
MATERIAL = 0xEAC0B497876ADEDF
TEXTURE = 0xCD4238C6A0C69E32

DEFAULT_R54_EXP = Path(r"D:\Clonedivers-HellForge-cache\exports\empire-017fd5e5140344768bd55de41dcd6789")
DEFAULT_STEAM_DATA = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2\data")
DEFAULT_NAT_IDX = Path(r"D:\ClaudeWork\empire-forge\native-index-25480438.json")
DEFAULT_RNAMES = Path(r"D:\ClaudeWork\empire-forge\triage-r45\resource-names.json")
DEFAULT_BK_ROOT = Path(r"D:\ClaudeWork\backups\c-checkout-20261109-172437\files")
DEFAULT_OUT_DIR = Path(r"D:\ClaudeWork\swarm-packs\empire-r55\patches\shared")


def load_mod(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    if spec is None or spec.loader is None:
        raise ImportError(f"Cannot load {path}")
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def murmur64(text: str) -> int:
    data = text.encode("utf-8")
    m = 0xC6A4A7935BD1E995
    h = (len(data) * m) & 0xFFFFFFFFFFFFFFFF
    for i in range(len(data) // 8):
        k = struct.unpack_from("<Q", data, i * 8)[0]
        k = (k * m) & 0xFFFFFFFFFFFFFFFF
        k ^= k >> 47
        k = (k * m) & 0xFFFFFFFFFFFFFFFF
        h = (h ^ k) * m & 0xFFFFFFFFFFFFFFFF
    tail = data[(len(data) // 8) * 8 :]
    for i in range(len(tail) - 1, -1, -1):
        h ^= tail[i] << (8 * i)
    if tail:
        h = (h * m) & 0xFFFFFFFFFFFFFFFF
    h ^= h >> 47
    h = (h * m) & 0xFFFFFFFFFFFFFFFF
    h ^= h >> 47
    return h


def load_reverse_names(rnames_path: Path, bk_root: Path) -> dict[int, str]:
    rev64: dict[int, str] = {}
    if rnames_path.is_file():
        doc = json.loads(rnames_path.read_text(encoding="utf-8"))
        for k, v in doc.items():
            if isinstance(v, str):
                try:
                    rev64[int(k.split(".")[0], 16)] = v
                except ValueError:
                    pass
    if bk_root.is_dir():
        for root, dirs, files in os.walk(bk_root, onerror=lambda e: None):
            dirs[:] = [d for d in dirs if d not in (".git", "node_modules", "uv")]
            for fn in files:
                if fn in ("all-native.txt", "hashes.txt", "strings.txt"):
                    fp = Path(root) / fn
                    for line in fp.read_text(encoding="utf-8", errors="replace").splitlines():
                        line = line.strip()
                        if not line:
                            continue
                        if fn == "all-native.txt":
                            m = re.match(r"(.*), 0x([0-9a-f]{16})\.0x([0-9a-f]{16}) <-", line)
                            if m and not m.group(1).startswith("0x"):
                                rev64[int(m.group(2), 16)] = m.group(1)
                        else:
                            rev64[murmur64(line)] = line
    return rev64


# Explicit 34 stock-backed MATERIALs dyed blue in patch_1000..1031 (14 len==stock + 20 len!=stock):
MAT14_ROLES = {
    # Orbital / Laser Cannon / Sentry / Ship Turbolaser / Quasar materials -> Imperial Green
    0x4D66532712E63799: "GREEN",
    0xBE9AFFBE62259A60: "GREEN",
    0xDA5F9F5B3370080A: "GREEN",
    0x0391253BA6A0AE8A: "GREEN",
    0x45831B095ED84A0A: "GREEN",
    0x7F6740A9DDDEC2CB: "GREEN",
    0xFFAEE482184249FD: "GREEN",
    # Handheld Blaster / Plasma materials -> Imperial Crimson Red
    0x9E6E861E2E6539D1: "RED",
    0x7A1C3F81D8871516: "RED",
    0x9213C4FCE50E0F9B: "RED",
    0xA57C18EF2C462584: "RED",
    0x45E0B1D5266A1FED: "RED",
    # Shared volumetric smoke / spore-cloud / explosion / multi-weapon scatter materials -> stock neutral uniforms
    0x0B5568BD29017072: "STOCK_SCATTER",
    0x288933897138A1F0: "STOCK_SCATTER",
    0x7AA7D0DDCF913278: "STOCK_SCATTER",
    0x9952274912730C5C: "STOCK_SCATTER",
    0xBF6C75E150316E2A: "STOCK_SCATTER",
    0x88FA986529A8EB9D: "STOCK_SCATTER",
    0x20E6A80A52599F43: "STOCK_SCATTER",
    0x5335E7A1FB0587BB: "STOCK_SCATTER",
    0x2186E5B4FC0374BE: "STOCK_SCATTER",
    0x354C68A7928C5E89: "STOCK_SCATTER",
    0x39AC8AAD5AD42F33: "STOCK_SCATTER",
    0x3D5E7E65ADFBDD88: "STOCK_SCATTER",
    0x56C36AB93297805E: "STOCK_SCATTER",
    0x6FC8BEE4791D36D0: "STOCK_SCATTER",
    0x75A0F11ECC2BAA94: "STOCK_SCATTER",
    0x84964A833211B927: "STOCK_SCATTER",
    0x8D273AB3CA71AA4C: "STOCK_SCATTER",
    0xBC5BB03378E5BE75: "STOCK_SCATTER",
    0xBE7A5690C9A17B35: "STOCK_SCATTER",
    0xC27B6095174FC770: "STOCK_SCATTER",
    0xCAAF72E428B53EFF: "STOCK_SCATTER",
    0xF564E60E3776BE4F: "STOCK_SCATTER",
}

SKIP_CUSTOM_UNIT_MATS = {
    0x80C9A26670D38A4D,
    0x92BAA8D52A333885,
    0xC2A04DCA6982B9E9,
    0x09125D48E65AE866,
    0x0EFD2249BA57E908,
    0xF710010057A162AA,
}


def parse_material_vars(mraw: bytes) -> tuple[int, int, int, list[tuple[int, int, int, int]]]:
    """Return (nt, nv, vd_off, [(vi, vtype, nhash, abs_off), ...]) for Stingray MATERIAL."""
    if len(mraw) < 136:
        return 0, 0, 0, []
    nt = struct.unpack_from("<I", mraw, 64)[0]
    nv = struct.unpack_from("<I", mraw, 104)[0]
    vd_sz = struct.unpack_from("<I", mraw, 120)[0]
    u_start = 136 + 12 * nt
    vd_off = u_start + 20 * nv
    if nt > 256 or nv > 512 or vd_off + vd_sz > len(mraw):
        return 0, 0, 0, []
    recs = []
    for vi in range(nv):
        vtype, _z0, nhash, rel_off, _z1 = struct.unpack_from("<5I", mraw, u_start + 20 * vi)
        abs_off = vd_off + rel_off
        if abs_off <= vd_off + vd_sz:
            recs.append((vi, vtype, nhash, abs_off))
    return nt, nv, vd_off, recs


def blue_to_role_rgb(r: float, g: float, b: float, role: str) -> tuple[float, float, float]:
    """Map a blue-dominant RGB triple to Imperial Turbolaser Green or Imperial Blaster Red."""
    lo = min(r, g)
    hi = max(r, g)
    if role == "GREEN":
        # Match patch_1035 Imperial emerald-lime turbolaser (~(15.3, 255.0, 20.4) at 255 scale)
        return (max(0.0, lo * 0.16 + b * 0.06), b, max(0.0, hi * 0.16 + b * 0.08))
    if role == "RED":
        return (b, max(0.0, lo * 0.14 + b * 0.04), max(0.0, lo * 0.12 + b * 0.03))
    raise ValueError(f"Unexpected role {role}")


def warm_to_role_rgb(r: float, g: float, b: float, role: str) -> tuple[float, float, float]:
    """Map stock yellow/orange laser RGB (for unpatched child PFXs) to Imperial Green or Red."""
    mx = max(r, g, b)
    mn = min(r, g, b)
    span = mx - mn
    if role == "GREEN":
        return (mn + 0.08 * span, mx, mn + 0.10 * span)
    if role == "RED":
        return (mx, mn + 0.08 * span, mn + 0.06 * span)
    raise ValueError(f"Unexpected role {role}")


def parse_stock_var_map(sraw: bytes | None) -> dict[tuple[int, int], tuple[float, ...]]:
    """Map (nhash, vtype) -> float tuple for vec3/vec4 shader uniforms in stock MATERIAL."""
    if not sraw or len(sraw) < 136:
        return {}
    _nt, _nv, svd_off, srecs = parse_material_vars(sraw)
    svd_end = svd_off + struct.unpack_from("<I", sraw, 120)[0]
    out: dict[tuple[int, int], tuple[float, ...]] = {}
    for _vi, vt, nh, aoff in srecs:
        if vt in (2, 3):
            nf = 3 if vt == 2 else 4
            if aoff + 4 * nf <= svd_end:
                out[(nh, vt)] = struct.unpack_from(f"<{nf}f", sraw, aoff)
    return out


def recolor_material(mraw: bytes, role: str, sraw: bytes | None = None) -> tuple[bytes, int]:
    """Recolor ONLY vtype in (2, 3) (vec3/vec4) shader uniforms, or restore stock scatter uniforms by (nhash, vtype)."""
    s_vars = parse_stock_var_map(sraw)
    if role == "STOCK_SCATTER":
        if sraw is None:
            raise ValueError("STOCK_SCATTER requires stock material bytes")
        _nt_r, _nv_r, vd_off_r, recs_r = parse_material_vars(mraw)
        _nt_s, _nv_s, vd_off_s, _ = parse_material_vars(sraw)
        if len(mraw) == len(sraw) and mraw[:vd_off_r] == sraw[:vd_off_s]:
            return sraw, 1 if mraw != sraw else 0
        vd_end_r = vd_off_r + struct.unpack_from("<I", mraw, 120)[0]
        out = bytearray(mraw)
        edits = 0
        for _vi, vt, nh, aoff in recs_r:
            if vt in (2, 3) and (nh, vt) in s_vars:
                nf = 3 if vt == 2 else 4
                if aoff + 4 * nf <= vd_end_r:
                    cur = struct.unpack_from(f"<{nf}f", out, aoff)
                    sv = s_vars[(nh, vt)]
                    if cur != sv:
                        struct.pack_into(f"<{nf}f", out, aoff, *sv)
                        edits += 1
        return bytes(out), edits

    _nt, _nv, vd_off, recs = parse_material_vars(mraw)
    if not recs:
        return mraw, 0
    vd_sz = struct.unpack_from("<I", mraw, 120)[0]
    vd_end = vd_off + vd_sz
    out = bytearray(mraw)
    edits = 0
    for _vi, vtype, nhash, abs_off in recs:
        if vtype not in (2, 3):
            continue
        n_f = 3 if vtype == 2 else 4
        if abs_off + 4 * n_f > vd_end:
            continue
        vals = list(struct.unpack_from(f"<{n_f}f", out, abs_off))
        r, g, b = vals[0], vals[1], vals[2]
        if not all(math.isfinite(x) and x >= 0.0 for x in (r, g, b)):
            continue
        sv = s_vars.get((nhash, vtype))
        # Skip Z-normal vectors where stock is already (0.0, 0.0, 1.0) (e.g. 0xf32ad38b)
        if sv is not None and abs(sv[0]) < 1e-5 and abs(sv[1]) < 1e-5 and abs(sv[2] - 1.0) < 1e-5:
            if abs(r) < 1e-5 and abs(g) < 1e-5 and abs(b - 1.0) < 1e-5:
                continue
        if b > 1.18 * max(r, g) and b > 0.02:
            nr, ng, nb = blue_to_role_rgb(r, g, b, role)
            vals[0], vals[1], vals[2] = nr, ng, nb
            struct.pack_into(f"<{n_f}f", out, abs_off, *vals)
            edits += 1
        elif sv is not None:
            sr, sg, sb = sv[0], sv[1], sv[2]
            if (r, g, b) == (sr, sg, sb) and max(r, g) > 0.25 and max(r, g, b) - min(r, g, b) > 0.15 * max(r, g, b):
                if role == "GREEN" and not (g > 1.2 * max(r, b)):
                    nr, ng, nb = warm_to_role_rgb(r, g, b, role)
                    vals[0], vals[1], vals[2] = nr, ng, nb
                    struct.pack_into(f"<{n_f}f", out, abs_off, *vals)
                    edits += 1
                elif role == "RED" and not (r > 1.2 * max(g, b)):
                    nr, ng, nb = warm_to_role_rgb(r, g, b, role)
                    vals[0], vals[1], vals[2] = nr, ng, nb
                    struct.pack_into(f"<{n_f}f", out, abs_off, *vals)
                    edits += 1
    return bytes(out), edits


def find_color_graphs(raw: bytes) -> list[tuple[int, int]]:
    """Detect Stingray 10-slot ColorGraph blocks: <10f> X + <30f> RGB in [0..255].
    Handles all 3 Stingray ColorGraph X-array conventions:
      A) Monotonic 10-slot X in [0..1] (unused tail padded with x=1.0)
      B) Active monotonic prefix 0..k in [0..1] followed by unused sentinel x=10000.0 slots
      C) Active monotonic prefix 0..k in [0..1] followed by trailing x=0.0 slots
    Returns list of (cg_offset, active_slot_count=10).
    """
    found: list[tuple[int, int]] = []
    off = 64
    limit = len(raw) - 160
    while off <= limit:
        xs = struct.unpack_from("<10f", raw, off)
        if all(math.isfinite(x) for x in xs) and 0.0 <= xs[0] <= 1.0:
            ys = [struct.unpack_from("<3f", raw, off + 40 + 12 * j) for j in range(10)]
            if all(all(math.isfinite(c) and -0.01 <= c <= 255.5 for c in rgb) for rgb in ys):
                mx_all = max(max(rgb) for rgb in ys)
                # Convention B: unused slots padded with 10000.0 (Stingray ColorGraph sentinel!)
                k10k = 0
                while k10k < 10 and 0.0 <= xs[k10k] <= 1.0001 and (k10k == 0 or xs[k10k] >= xs[k10k - 1]):
                    k10k += 1
                if 1 <= k10k < 10 and all(abs(xs[j] - 10000.0) < 1e-2 for j in range(k10k, 10)) and mx_all >= 0.05:
                    found.append((off, 10))
                    off += 160
                    continue
                if all(0.0 <= x <= 1.0001 for x in xs) and xs[0] <= 0.25:
                    # Convention A: monotonic across all 10 slots in [0..1]
                    if all(xs[i] <= xs[i + 1] for i in range(9)) and xs[9] >= 0.25:
                        if mx_all > 1.05 or (abs(xs[9] - 1.0) < 1e-4 and xs[0] == 0.0 and mx_all >= 0.2):
                            found.append((off, 10))
                            off += 160
                            continue
                    # Convention C: active monotonic prefix 0..k followed by trailing x=0.0 slots
                    k = 0
                    while k + 1 < 10 and xs[k + 1] >= xs[k] and xs[k + 1] > 0.0:
                        k += 1
                    if all(xs[j] == 0.0 for j in range(k + 1, 10)):
                        active_max = max(max(ys[j]) for j in range(k + 1))
                        if (k >= 1 and xs[k] >= 0.20 and (active_max > 1.05 or (abs(xs[k] - 1.0) < 1e-4 and active_max >= 0.2))) or (
                            k == 0 and xs[0] == 0.0 and all(all(abs(c) <= 1e-4 for c in ys[j]) for j in range(1, 10)) and active_max >= 15.0
                        ):
                            found.append((off, 10))
                            off += 160
                            continue
        off += 4
    return found


def diff_aligned_rgb_offsets(raw: bytes, sraw: bytes, cg_ranges: set[int]) -> list[int]:
    """Find 12-byte RGB float triples modified between raw and sraw outside ColorGraphs
    when raw and sraw share the exact same ParticleEffect structure (header [:64] identical).
    """
    if len(raw) != len(sraw) or raw[:64] != sraw[:64]:
        return []
    out_offs: list[int] = []
    off = 32
    n = len(raw)
    while off <= n - 12:
        if off in cg_ranges:
            off += 4
            continue
        if raw[off : off + 4] != sraw[off : off + 4]:
            # Check if this changed float is G (off-4 was R==0 in both) or B (off-8..off was R==0, G==0 in both)
            start = off
            if (
                start >= 36
                and (start - 4) not in cg_ranges
                and raw[start - 4 : start] == b"\x00\x00\x00\x00"
                and sraw[start - 4 : start] == b"\x00\x00\x00\x00"
            ):
                # Could be (0, G, B) or (0, 0, B) — check if start-8 is also zero and start+4 is NOT part of triple
                if (
                    start >= 40
                    and (start - 8) not in cg_ranges
                    and raw[start - 8 : start - 4] == b"\x00\x00\x00\x00"
                    and sraw[start - 8 : start - 4] == b"\x00\x00\x00\x00"
                    and raw[start + 4 : start + 8] == sraw[start + 4 : start + 8]
                ):
                    start = start - 8
                else:
                    start = start - 4
            # Advance in 12-byte triples across this modified float region
            cur = start
            while cur <= n - 12 and cur not in cg_ranges and raw[cur : cur + 12] != sraw[cur : cur + 12]:
                out_offs.append(cur)
                cur += 12
            off = max(off + 4, cur)
            continue
        off += 4
    return out_offs


def recolor_pfx(
    rraw: bytes,
    role: str,
    sraw: bytes | None = None,
    recolor_warm_stock: bool = False,
) -> tuple[bytes, int]:
    """Recolor blue-dominant ColorGraph keys and unit-scale light/tint floats in a Stingray PARTICLES resource."""
    out = bytearray(rraw)
    edits = 0
    cgs = find_color_graphs(rraw)
    cg_ranges = set()
    for cg_off, n_active in cgs:
        for b_off in range(cg_off, cg_off + 160, 4):
            cg_ranges.add(b_off)
        for j in range(n_active):
            c_off = cg_off + 40 + 12 * j
            r, g, b = struct.unpack_from("<3f", out, c_off)
            mx = max(r, g, b)
            mn = min(r, g, b)
            if b > 1.18 * max(r, g) and b >= 0.05:
                nr, ng, nb = blue_to_role_rgb(r, g, b, role)
                struct.pack_into("<3f", out, c_off, nr, ng, nb)
                edits += 1
            elif recolor_warm_stock and mx >= 10.0 and (mx - mn) > 0.18 * mx:
                if role == "GREEN" and not (g > 1.2 * max(r, b)):
                    nr, ng, nb = warm_to_role_rgb(r, g, b, role)
                    struct.pack_into("<3f", out, c_off, nr, ng, nb)
                    edits += 1
                elif role == "RED" and not (r > 1.2 * max(g, b)):
                    nr, ng, nb = warm_to_role_rgb(r, g, b, role)
                    struct.pack_into("<3f", out, c_off, nr, ng, nb)
                    edits += 1

    if sraw is not None and len(sraw) == len(out):
        for off in diff_aligned_rgb_offsets(rraw, sraw, cg_ranges):
            sr, sg, sb = struct.unpack_from("<3f", sraw, off)
            rr, rg, rb = struct.unpack_from("<3f", out, off)
            if all(math.isfinite(x) and 0.0 <= x <= 255.5 for x in (sr, sg, sb, rr, rg, rb)):
                if rb > 1.18 * max(rr, rg) and rb >= 0.05:
                    nr, ng, nb = blue_to_role_rgb(rr, rg, rb, role)
                    struct.pack_into("<3f", out, off, nr, ng, nb)
                    edits += 1
    # NOTE: When len(sraw) != len(out) or sraw is None, never run an unaligned 4-byte sliding window outside
    # ColorGraphs: 10-slot ScalarGraph X-knot arrays ([0.0, 0.135, 1.0, 10000.0...]) and +4B phase-shifted
    # Vector3(100, 0, 0) blocks ([u32(1), u32(1), 100.0] / [y_i=0, z_i=0, x_{i+1}=100]) are non-color curves!

    return bytes(out), edits


def classify_r54_blue_pfx(pk: int, pfx_name: str, archive_topics: list[str], has_stock: bool = True) -> str:
    """Classify a blue-dyed PFX from patch_1000..1031 into GREEN, RED, or RESTORE_STOCK."""
    if pk == 0x9B4AAE5E1359FAD9 or not has_stock:
        return "RED"
    joined = (pfx_name + " | " + " | ".join(archive_topics)).lower()

    # 0. Explicit ballistic/explosive weapons -> RESTORE_STOCK before generic vehicle/ship keywords
    restore_first = (
        "grenade_launcher",
        "frag_grenade",
        "he_grenade",
        "smoke_grenade",
        "stun_grenade",
        "thermite",
        "autocannon",
        "recoilless",
        "spear_",
        "mortar_",
        "flamethrower",
    )
    if any(kw in joined for kw in restore_first):
        return "RESTORE_STOCK"

    # 1. Imperial Green Turbolasers: Orbital strikes, Star Destroyer / Super Destroyer, Extraction/Pelican/Eagle,
    #    LAS-98 Laser Cannon, Laser Sentry/Turret, Guard Dog Rover, Quasar Cannon, Heavy Cannon
    green_keywords = (
        "orbital",
        "destroyer",
        "super_destroyer",
        "ship_",
        "vista",
        "extraction",
        "pelican",
        "shuttle",
        "eagle",
        "laser_cannon",
        "las98",
        "las_98",
        "support_weapon_laser",
        "sentry_laser",
        "laser_sentry",
        "laser_turret",
        "turret_laser",
        "guard_dog",
        "backpack_drone",
        "rover",
        "quasar",
        "laser_pulse_cannon",
        "laser_rifle_charge",  # LAS-99 Quasar heavy charge cannon
    )
    # 2. Imperial Crimson Red Blasters: Handheld energy/laser/plasma rifles, pistols, shotguns, revolvers, bots
    red_keywords = (
        "laser_rifle",
        "laser_pistol",
        "laser_shotgun",
        "energy_revolver",
        "energy_weapon",
        "marksman_rifle_shark",
        "plasma_punisher",
        "plasma_rifle",
        "plasma_pistol",
        "plasma_blaster",
        "plasma_",
        "scorcher",
        "purifier",
        "loyalist",
        "talon",
        "sickle",
        "scythe",
        "dagger",
        "trident",
        "cyborg",
        "automaton",
    )
    # Check laser_rifle_charge BEFORE laser_rifle!
    if "laser_rifle_charge" in joined:
        return "GREEN"
    if any(kw in joined for kw in red_keywords):
        return "RED"
    if any(kw in joined for kw in green_keywords):
        return "GREEN"
    # Everything else (ballistic rifles/shotguns/SMGs, grenades, shields, hellpods, mortars, bug/spore/smoke hits)
    return "RESTORE_STOCK"


def pfx_has_blue(raw: bytes, sraw: bytes | None = None, orig_r54: bytes | None = None) -> int:
    """Count remaining blue-dominant ColorGraph (including unit-scale [0..1]) or aligned diffed RGB triples."""
    cnt = 0
    cgs = find_color_graphs(raw)
    cg_ranges = {b_off for cg_off, _ in cgs for b_off in range(cg_off, cg_off + 160, 4)}
    for cg_off, n_active in cgs:
        for j in range(n_active):
            c_off = cg_off + 40 + 12 * j
            r, g, b = struct.unpack_from("<3f", raw, c_off)
            if b > 1.2 * max(r, g) and b >= 0.08:
                if b < 8.0 and sraw is not None and len(sraw) == len(raw) and raw[c_off : c_off + 12] == sraw[c_off : c_off + 12]:
                    continue
                cnt += 1
    ref_for_offs = orig_r54 if orig_r54 is not None else raw
    if sraw is not None and len(sraw) == len(raw):
        for off in diff_aligned_rgb_offsets(ref_for_offs, sraw, cg_ranges):
            sr, sg, sb = struct.unpack_from("<3f", sraw, off)
            rr, rg, rb = struct.unpack_from("<3f", raw, off)
            if all(math.isfinite(x) and 0.0 <= x <= 255.5 for x in (sr, sg, sb, rr, rg, rb)):
                if rb > 1.25 * max(rr, rg) and rb >= 0.15 and not (sb > 1.25 * max(sr, sg)):
                    cnt += 1
    return cnt


def build_patch_1035(
    r54_exp: Path,
    steam_data: Path,
    nat_idx_path: Path,
    rnames_path: Path,
    bk_root: Path,
    out_dir: Path,
) -> dict:
    ns_mod = load_mod("walker_next_native", bk_root / "tools" / "walker-next-native.py")
    ns = ns_mod.NativeSlim(steam_data)
    ns.chunk = functools.lru_cache(maxsize=128)(ns.chunk)
    nat_idx = json.loads(nat_idx_path.read_text(encoding="utf-8"))
    rev64 = load_reverse_names(rnames_path, bk_root)

    @functools.lru_cache(maxsize=2048)
    def steam_arch_rows(arch: str) -> dict[tuple[int, int], tuple[int, ...]]:
        if arch not in ns.archives and not (steam_data / arch).exists():
            return {}
        hdr = ns.read(arch, 0, 72)
        magic, types, count = struct.unpack_from("<III", hdr)
        if magic != 0xF0000011:
            return {}
        raw = ns.read(arch, 72 + 32 * types, 80 * count)
        return {(r[0], r[1]): r for r in ns_mod.ROW.iter_unpack(raw)}

    def read_stock(kid: int, tid: int) -> bytes | None:
        lbl = f"{kid:016x}.{tid:016x}"
        for prov in nat_idx.get(lbl, []):
            rows = steam_arch_rows(prov["archive"])
            if (kid, tid) in rows:
                r = rows[(kid, tid)]
                return ns.read(prov["archive"], r[2], r[7])
        return None

    def archive_topics(kid: int, tid: int) -> list[str]:
        lbl = f"{kid:016x}.{tid:016x}"
        topics = []
        for prov in nat_idx.get(lbl, [])[:4]:
            a = prov["archive"]
            nms = []
            for rk, _rt in steam_arch_rows(a):
                nm = rev64.get(rk, "")
                if nm and not nm.startswith(
                    ("content/art_shared", "core/", "content/effects/materials/smoke", "content/env_ship/")
                ):
                    nms.append(nm)
            topics.append(f"{a}:{'|'.join(nms[:5])}")
        return topics

    # Load full r54 archive stack
    stack: dict[tuple[int, int], list[tuple[int, Path, tuple[int, ...]]]] = defaultdict(list)
    for p in sorted(r54_exp.iterdir()):
        if p.is_file() and P.MAIN.fullmatch(p.name):
            pnum = int(p.name.split(".patch_")[1])
            for row in P.directory(p):
                stack[(row[0], row[1])].append((pnum, p, row))
    for k in stack:
        stack[k].sort(key=lambda x: x[0])

    def read_r54_parts(key: tuple[int, int]) -> tuple[bytes, bytes, bytes]:
        _, p, row = stack[key][-1]
        return P.Resource(p, row, tuple(p.with_name(p.name + s) for s in P.SUFFIXES)).parts()

    # Load existing patch_1035 resources as starting point
    p1035_in = r54_exp / "9ba626afa44a3aa3.patch_1035"
    header_72 = p1035_in.read_bytes()[:72]
    resources: dict[tuple[int, int], P.Generated] = {}
    for row in P.directory(p1035_in):
        parts = P.Resource(p1035_in, row, tuple(p1035_in.with_name(p1035_in.name + s) for s in P.SUFFIXES)).parts()
        resources[(row[0], row[1])] = P.Generated(parts)

    stats = Counter()
    pfx_decisions = []
    mat_decisions = []

    # 1. Clean all 20 existing patch_1035 PARTICLES + determine their roles (GREEN vs RED)
    p1035_pfx_roles: dict[int, str] = {}
    for (kid, tid) in sorted(list(resources.keys())):
        if tid != PARTICLES:
            continue
        rb = resources[(kid, tid)].parts()[0]
        g_cnt, r_cnt = 0, 0
        for cg_off, n_active in find_color_graphs(rb):
            for j in range(n_active):
                rr, rg, rbl = struct.unpack_from("<3f", rb, cg_off + 40 + 12 * j)
                if rg > 1.25 * max(rr, rbl) and rg >= 20.0:
                    g_cnt += 1
                elif rr > 1.3 * max(rg, rbl) and rr >= 20.0 and rg < 0.5 * rr:
                    r_cnt += 1
        role = "RED" if (r_cnt > g_cnt and r_cnt > 0) else "GREEN"
        p1035_pfx_roles[kid] = role
        sb = read_stock(kid, PARTICLES)
        fixed_b, edits = recolor_pfx(rb, role, sb, recolor_warm_stock=False)
        resources[(kid, PARTICLES)] = P.Generated((fixed_b, b"", b""))
        if edits:
            stats["p1035_pfx_cleaned"] += 1
            stats["p1035_pfx_float_edits"] += edits
        pfx_decisions.append({"id": f"{kid:016x}", "source": "patch_1035", "role": role, "edits": edits})

    # 2. Add & recolor the 2 unpatched child PFXs referenced by patch_1035 PFXs
    for child_pk, parent_pk in [(0xCCFD98974277F784, 0x640330F0E73E9088), (0x58B9B4A05FBA232E, 0xD9A5DA96C99F326F)]:
        role = p1035_pfx_roles.get(parent_pk, "GREEN")
        cb = read_stock(child_pk, PARTICLES)
        if cb is not None:
            fixed_cb, edits = recolor_pfx(cb, role, cb, recolor_warm_stock=True)
            resources[(child_pk, PARTICLES)] = P.Generated((fixed_cb, b"", b""))
            stats["child_pfx_added"] += 1
            pfx_decisions.append({"id": f"{child_pk:016x}", "source": f"stock_child_of_{parent_pk:016x}", "role": role, "edits": edits})

    # 3. Clean all 32 existing patch_1035 MATERIALs (strictly vtype in (2, 3))
    for (mq, tid) in sorted(list(resources.keys())):
        if tid != MATERIAL:
            continue
        mraw = resources[(mq, tid)].parts()[0]
        users = [p1035_pfx_roles[pk] for pk in p1035_pfx_roles if struct.pack("<Q", mq) in resources[(pk, PARTICLES)].parts()[0]]
        role = "RED" if (users and all(u == "RED" for u in users)) else "GREEN"
        fixed_m, edits = recolor_material(mraw, role, read_stock(mq, MATERIAL))
        resources[(mq, MATERIAL)] = P.Generated((fixed_m, b"", b""))
        if edits:
            stats["p1035_mat_cleaned"] += 1
            stats["p1035_mat_var_edits"] += edits
        mat_decisions.append({"id": f"{mq:016x}", "source": "patch_1035", "role": role, "edits": edits})

    # 4. Override the 14 blue-dyed MATERIALs from patch_1000..1031
    for mq, role in sorted(MAT14_ROLES.items()):
        if (mq, MATERIAL) not in stack:
            continue
        r54_m = read_r54_parts((mq, MATERIAL))[0]
        stk_m = read_stock(mq, MATERIAL)
        fixed_m, edits = recolor_material(r54_m, role, stk_m)
        resources[(mq, MATERIAL)] = P.Generated((fixed_m, b"", b""))
        stats[f"mat14_{role.lower()}"] += 1
        mat_decisions.append({
            "id": f"{mq:016x}",
            "source": f"patch_{stack[(mq, MATERIAL)][-1][0]}",
            "name": rev64.get(mq, ""),
            "role": role,
            "edits": edits,
        })

    # 5. Scan ALL PARTICLES in patch_1000..1031 (including len!=stock & custom s_b=None) dyed blue and override in patch_1035
    for (pk, tid), ents in sorted(stack.items()):
        if tid != PARTICLES:
            continue
        pnum = ents[-1][0]
        if pnum >= 1035 or (pk, PARTICLES) in resources:
            continue
        r_b = read_r54_parts((pk, PARTICLES))[0]
        s_b = read_stock(pk, PARTICLES)
        n_blue = pfx_has_blue(r_b, s_b)
        if n_blue == 0:
            continue
        topics = archive_topics(pk, PARTICLES)
        nm = rev64.get(pk, "")
        role = classify_r54_blue_pfx(pk, nm, topics, has_stock=(s_b is not None))
        if role == "RESTORE_STOCK" and s_b is not None:
            resources[(pk, PARTICLES)] = P.Generated((s_b, b"", b""))
            stats["r54_blue_pfx_restored_stock"] += 1
            pfx_decisions.append({
                "id": f"{pk:016x}",
                "source": f"patch_{pnum}",
                "name": nm,
                "topics": topics[:2],
                "role": "RESTORE_STOCK",
                "blue_before": n_blue,
                "blue_after": 0,
            })
        else:
            fixed_b, edits = recolor_pfx(r_b, role, s_b, recolor_warm_stock=False)
            rem_blue = pfx_has_blue(fixed_b, s_b, orig_r54=r_b)
            resources[(pk, PARTICLES)] = P.Generated((fixed_b, b"", b""))
            stats[f"r54_blue_pfx_{role.lower()}"] += 1
            pfx_decisions.append({
                "id": f"{pk:016x}",
                "source": f"patch_{pnum}",
                "name": nm,
                "topics": topics[:2],
                "role": role,
                "edits": edits,
                "blue_before": n_blue,
                "blue_after": rem_blue,
            })

    # 6. Verify zero patch_1036..1047 overrides clobber any resource in our new patch_1035
    clobbered = []
    for k in resources:
        later = [e[0] for e in stack.get(k, []) if e[0] > 1035]
        if later:
            clobbered.append((f"{k[0]:016x}.{k[1]:016x}", later))
    if clobbered:
        raise RuntimeError(f"Resources in patch_1035 clobbered by higher patches: {clobbered}")

    # 7. Verify zero blue ColorGraph keys remain on any GREEN or RED PFX, and zero unpatched stock-backed blue MATERIALs remain across the entire stack!
    residual_blue_pfx = []
    for d in pfx_decisions:
        if d["role"] in ("GREEN", "RED"):
            pk = int(d["id"], 16)
            rb = resources[(pk, PARTICLES)].parts()[0]
            orig_b = read_r54_parts((pk, PARTICLES))[0] if (pk, PARTICLES) in stack else rb
            rem = pfx_has_blue(rb, read_stock(pk, PARTICLES), orig_r54=orig_b)
            if rem > 0:
                residual_blue_pfx.append((d["id"], d["role"], rem))
    if residual_blue_pfx:
        raise RuntimeError(f"Residual blue RGB keys in GREEN/RED PFXs: {residual_blue_pfx}")

    residual_blue_mat = []
    for (mq, tid) in sorted(set(k for k in stack if k[1] == MATERIAL and stack[k][-1][0] <= 1035) | {k for k in resources if k[1] == MATERIAL}):
        if mq in SKIP_CUSTOM_UNIT_MATS:
            continue
        mraw = resources[(mq, MATERIAL)].parts()[0] if (mq, MATERIAL) in resources else read_r54_parts((mq, MATERIAL))[0]
        sraw_m = read_stock(mq, MATERIAL)
        if sraw_m is None and (mq, MATERIAL) not in resources:
            continue
        if sraw_m is not None and mraw == sraw_m:
            continue
        s_vars_m = parse_stock_var_map(sraw_m)
        _, _, vd_off, recs = parse_material_vars(mraw)
        vd_end = vd_off + (struct.unpack_from("<I", mraw, 120)[0] if len(mraw) >= 136 else 0)
        for _vi, vtype, nh, abs_off in recs:
            if vtype in (2, 3) and abs_off + 12 <= vd_end:
                r, g, b = struct.unpack_from("<3f", mraw, abs_off)
                sv = s_vars_m.get((nh, vtype))
                if sv is not None and all(abs(a - b_) < 1e-5 for a, b_ in zip((r, g, b), sv)):
                    continue
                if all(math.isfinite(x) and x >= 0 for x in (r, g, b)) and b > 1.25 * max(r, g) and b > 0.05:
                    residual_blue_mat.append((f"{mq:016x}", f"{nh:08x}", round(r, 3), round(g, 3), round(b, 3)))
    if residual_blue_mat:
        raise RuntimeError(f"Residual blue vec3/vec4 shader uniforms in stack MATERIALs: {residual_blue_mat}")

    # 8. Write staged archive
    out_dir.mkdir(parents=True, exist_ok=True)
    out_main = out_dir / "9ba626afa44a3aa3.patch_1035"
    P.write_archive(out_main, header_72, resources)

    # Verify companion sizes == 0 and finite floats / archive directory roundtrip
    rows_back = P.directory(out_main)
    if len(rows_back) != len(resources):
        raise RuntimeError("Archive directory count mismatch")
    gpu_sz = out_main.with_name(out_main.name + ".gpu_resources").stat().st_size
    str_sz = out_main.with_name(out_main.name + ".stream").stat().st_size
    if gpu_sz != 0 or str_sz != 0:
        raise RuntimeError(f"Expected 0 B companions on patch_1035, got gpu={gpu_sz}, stream={str_sz}")

    main_sha = hashlib.file_digest(out_main.open("rb"), "sha256").hexdigest()
    summary = {
        "patch": out_main.name,
        "path": str(out_main),
        "size": out_main.stat().st_size,
        "sha256": main_sha,
        "gpu_size": gpu_sz,
        "stream_size": str_sz,
        "total_resources": len(resources),
        "particles_count": sum(1 for k in resources if k[1] == PARTICLES),
        "materials_count": sum(1 for k in resources if k[1] == MATERIAL),
        "stats": dict(stats),
        "mat_decisions": mat_decisions,
        "pfx_decisions": pfx_decisions,
    }
    report_json = out_dir.parent.parent / "turbolaser_vfx_r55_summary.json"
    report_json.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return summary


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--r54-exp", type=Path, default=DEFAULT_R54_EXP)
    ap.add_argument("--steam-data", type=Path, default=DEFAULT_STEAM_DATA)
    ap.add_argument("--nat-idx", type=Path, default=DEFAULT_NAT_IDX)
    ap.add_argument("--rnames", type=Path, default=DEFAULT_RNAMES)
    ap.add_argument("--bk-root", type=Path, default=DEFAULT_BK_ROOT)
    ap.add_argument("--out-dir", type=Path, default=DEFAULT_OUT_DIR)
    args = ap.parse_args()

    summary = build_patch_1035(
        args.r54_exp,
        args.steam_data,
        args.nat_idx,
        args.rnames,
        args.bk_root,
        args.out_dir,
    )
    print(json.dumps({k: v for k, v in summary.items() if k not in ("mat_decisions", "pfx_decisions")}, indent=2))
    print("\nSample PFX classifications (GREEN vs RED vs RESTORE_STOCK):")
    by_role = defaultdict(list)
    for d in summary["pfx_decisions"]:
        by_role[d["role"]].append(d)
    for role in ("GREEN", "RED", "RESTORE_STOCK"):
        print(f"  [{role}] count={len(by_role[role])}")
        for item in by_role[role][:10]:
            print(f"    {item['id']} ({item['source']}) {item.get('name') or item.get('topics', [''])[0][:72]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
