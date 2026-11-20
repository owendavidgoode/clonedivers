"""
Generate src/atst_asset_pins.h deterministically from ../delta/atst-pins.json.
"""

from __future__ import annotations

import json
import struct
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
PINS_JSON = HERE.parent / "delta" / "atst-pins.json"
OUT_H = HERE / "src" / "atst_asset_pins.h"


def f32_u32(val: float) -> int:
    return struct.unpack("<I", struct.pack("<f", float(val)))[0]


def mat4_to_r48_f32(m_flat16: list[float]) -> list[float]:
    """Column-major 4x4 flattened row-wise -> top 3x4 row-major float32 (12 floats)."""
    return [float(m_flat16[i]) for i in range(12)]


def main() -> int:
    pins = json.loads(PINS_JSON.read_text(encoding="utf-8"))
    s2c_n = int(pins["s2c_count"])
    assert s2c_n == 106
    orig_joint = pins["s2c_orig_joint"]
    s2c_par = pins["s2c_parent"]
    prot = set(pins["s2c_protected_nodes"])
    skin_bones = pins["skin_bones_s2c"]
    skin_orig = pins["skin_bones_orig"]
    remaps = pins["remaps"]
    hashes_198 = pins["native_hashes_198"]
    parents_198 = pins["native_parents_198"]

    # Convert stored row-major 4x4 skin binds -> column-major 3x4 render48 float32 words
    binds_rm = np.array(pins["binds_row_major_flat"], dtype=np.float32).reshape(8, 4, 4)
    binds_cm = binds_rm.transpose(0, 2, 1)[:, :3, :].reshape(8, 12)

    nat_loc = np.array(pins["native_locals_198"], dtype=np.float32).reshape(198, 4, 4)[:, :3, :].reshape(198, 12)
    nat_wld = np.array(pins["native_worlds_198"], dtype=np.float32).reshape(198, 4, 4)[:, :3, :].reshape(198, 12)
    auth_loc = np.array(pins["authored_locals_198"], dtype=np.float32).reshape(198, 4, 4)[:, :3, :].reshape(198, 12)
    auth_wld = np.array(pins["authored_worlds_198"], dtype=np.float32).reshape(198, 4, 4)[:, :3, :].reshape(198, 12)

    film_scale = float(np.linalg.norm(auth_wld[66, [3, 7, 11]]) / np.linalg.norm(nat_wld[66, [3, 7, 11]]))
    ratios = []
    for i in range(198):
        if i == 0:
            ratios.append(film_scale)
        else:
            nl = float(np.linalg.norm(nat_loc[i, [3, 7, 11]]))
            al = float(np.linalg.norm(auth_loc[i, [3, 7, 11]]))
            ratios.append(al / nl if nl > 1e-8 else 1.0)

    def fmt_f32(val: float) -> str:
        s = f"{float(val):.9g}"
        if "." not in s and "e" not in s and "E" not in s:
            s += ".0"
        return s + "f"

    def fmt_f12_rows(arr: np.ndarray, name: str) -> str:
        lines = [f"static const float {name}[{len(arr)}][12] = {{"]
        for row in arr:
            vals = ", ".join(fmt_f32(float(x)) for x in row)
            lines.append(f"    {{ {vals} }},")
        lines.append("};")
        return "\n".join(lines)

    def fmt_u32_rows(arr: np.ndarray, name: str) -> str:
        lines = [f"static const uint32_t {name}[{len(arr)}][12] = {{"]
        for row in arr:
            vals = ", ".join(f"0x{f32_u32(float(x)):08X}u" for x in row)
            lines.append(f"    {{ {vals} }},")
        lines.append("};")
        return "\n".join(lines)

    s2c_hash = [hashes_198[j] for j in orig_joint]
    s2c_prot_flags = [1 if i in prot else 0 for i in range(s2c_n)]
    # Descriptor nodes inside S2c (old 4..7, 154..157 -> s2c indices)
    o2n = {int(k): int(v) for k, v in pins["old_to_new"].items()}
    desc_nodes = [o2n[j] for j in [4, 5, 6, 7, 154, 155, 156, 157]]

    out = []
    out.append("/* AUTO-GENERATED from delta/atst-pins.json by generate_pins.py - DO NOT EDIT BY HAND. */")
    out.append("#ifndef ATST_ASSET_PINS_H")
    out.append("#define ATST_ASSET_PINS_H\n")
    out.append("#include <stdint.h>\n")
    out.append("#define AT_UNIT_RESOURCE_ID 0x79E4B3D2DA5E45E3ULL")
    out.append("#define AT_JOINTS 198u")
    out.append("#define AT_S2C_NODES 106u")
    out.append("#define AT_GRAPH_CAP 128u")
    out.append("#define AT_SKIN_BONES_COUNT 8u")
    out.append("#define AT_SKIN_GROUPS_COUNT 3u")
    out.append("#define AT_SKIN_OUTPUT_COUNT 20u")
    out.append("#define AT_MESH_DESC_COUNT 8u")
    out.append(f"#define AT_FILM_SCALE_STRIDE {film_scale:.17g}\n")

    out.append(f"static const uint16_t AT_PARENT_198[198] = {{ {', '.join(str(x) for x in parents_198)} }};")
    out.append(f"static const uint32_t AT_HASH_198[198] = {{ {', '.join(f'0x{x:08X}u' for x in hashes_198)} }};")
    out.append(f"static const uint16_t AT_S2C_ORIG_JOINT[106] = {{ {', '.join(str(x) for x in orig_joint)} }};")
    out.append(f"static const uint16_t AT_S2C_PARENT[106] = {{ {', '.join(str(x) for x in s2c_par)} }};")
    out.append(f"static const uint32_t AT_S2C_HASH[106] = {{ {', '.join(f'0x{x:08X}u' for x in s2c_hash)} }};")
    out.append(f"static const uint8_t AT_S2C_IS_PROTECTED[106] = {{ {', '.join(str(x) for x in s2c_prot_flags)} }};")
    out.append(f"static const uint32_t AT_SKIN_BONES[8] = {{ {', '.join(f'{x}u' for x in skin_bones)} }};")
    out.append(f"static const uint16_t AT_SKIN_ORIG_JOINTS[8] = {{ {', '.join(f'{x}u' for x in skin_orig)} }};")
    out.append(f"static const uint32_t AT_GROUP_COUNTS[3] = {{ {', '.join(f'{len(r)}u' for r in remaps)} }};")
    out.append(f"static const uint32_t AT_DESC_NODES[8] = {{ {', '.join(f'{x}u' for x in desc_nodes)} }};")
    for gi, r in enumerate(remaps):
        out.append(f"static const uint32_t AT_PALETTE_{gi}[{len(r)}] = {{ {', '.join(f'{x}u' for x in r)} }};")
    out.append("")
    out.append(f"static const double AT_RATIO_198[198] = {{\n    " + ",\n    ".join(f"{r:.17g}" for r in ratios) + "\n};\n")
    out.append(fmt_f12_rows(nat_loc, "AT_NAT_LOCAL_198"))
    out.append("")
    out.append(fmt_f12_rows(auth_loc, "AT_AUTH_LOCAL_198"))
    out.append("")
    out.append(fmt_u32_rows(binds_cm, "AT_BINDS_48_U32"))
    out.append("\n#endif /* ATST_ASSET_PINS_H */\n")

    OUT_H.write_text("\n".join(out), encoding="utf-8")
    print(f"Generated {OUT_H} (film_scale={film_scale:.15f}, desc_nodes={desc_nodes}, skin_bones={skin_bones})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
