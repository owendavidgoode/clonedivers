#!/usr/bin/env python3
"""Unit tests for Clonedivers r26 Stingray binary mutators, helmet rig grafting, hiders, and SLIM rewriter."""
from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path

import numpy as np

TOOLS_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS_DIR))
sys.path.insert(0, str(TOOLS_DIR / "empire-forge"))

import build_clones_r26 as B  # noqa: E402
import empire_port as P  # noqa: E402
import validate_clones_r26 as V  # noqa: E402
import verify_candidate as VC  # noqa: E402


def _make_synthetic_stock_unit(
    *,
    bones_id: int = B.EAGLE_UNIT,
    joint_hashes: tuple[int, ...] = (0x11111111, 0x22222222),
    with_mesh_and_layout: bool = True,
) -> bytes:
    """Create a valid Stingray UNIT with @48 LODData, @52 JointData, optional @92/@100, and @112 mat_map."""
    buf = bytearray(128)
    struct.pack_into("<Q", buf, 8, bones_id)
    struct.pack_into("<Q", buf, 32, B.EAGLE_UNIT)

    # @52 JointData
    nn = len(joint_hashes)
    buf.extend(bytes(-len(buf) % 16))
    struct.pack_into("<I", buf, 52, len(buf))
    eye = np.eye(4, dtype="<f4").tobytes()
    buf.extend(struct.pack("<4I", nn, 0, 0, 0))
    buf.extend(eye * nn)  # local
    buf.extend(eye * nn)  # world
    buf.extend(struct.pack(f"<{nn}I", *([0] * nn)))  # parents
    buf.extend(struct.pack(f"<{nn}I", *joint_hashes))  # hashes

    if with_mesh_and_layout:
        # @48 LODData inside native prefix (1 group, 1 level, 1 footer with tiny 0.2m AABB & inflated ls=12.0)
        buf.extend(bytes(-len(buf) % 16))
        struct.pack_into("<I", buf, 48, len(buf))
        lod_grp = bytearray()
        lod_grp.extend(struct.pack("<5I", 0, 0, 0, 1, 1))  # footers=1, levels=1
        lod_grp.extend(struct.pack("<2I", 28, 44))  # entry_rel=28, footer_rel=44
        lod_grp.extend(struct.pack("<2f2I", 100.0, 0.0, 1, 0))
        lod_grp.extend(struct.pack("<7fIfI", -0.1, -0.1, 0.0, 0.1, 0.1, 0.2, 0.2, nn - 1, 12.0, 0))
        buf.extend(B._pack_pointer_table([bytes(lod_grp)]))

        # @88 SkinData (1 bone pointing to last joint `nn - 1`)
        buf.extend(bytes(-len(buf) % 16))
        struct.pack_into("<I", buf, 88, len(buf))
        buf.extend(B._pack_pointer_table([B._pack_single_joint_skin(nn - 1, np.eye(4, dtype=np.float64), 1)]))

        # @92 Layout (12B pos vertex, u16 indices)
        buf.extend(bytes(-len(buf) % 16))
        struct.pack_into("<I", buf, 92, len(buf))
        lay = bytearray(448)
        struct.pack_into("<3I", lay, 8, 0, 2, 0)  # pos f32x3
        struct.pack_into("<I", lay, 328, 1)  # 1 attr
        struct.pack_into("<II", lay, 352, 4, 12)  # vc=4, stride=12
        struct.pack_into("<II", lay, 392, 6, 0)  # ic=6, u16
        struct.pack_into("<4I", lay, 416, 0, 48, 48, 12)
        buf.extend(B._pack_pointer_table([bytes(lay)]))

        # @100 MeshObject inside native prefix (tiny 0.2m AABB)
        buf.extend(bytes(-len(buf) % 16))
        struct.pack_into("<I", buf, 100, len(buf))
        mh = bytearray(128)
        struct.pack_into("<7f", mh, 8, -0.1, -0.1, 0.0, 0.1, 0.1, 0.2, 0.2)
        struct.pack_into("<4I", mh, 36, 9, joint_hashes[-1], nn - 1, nn - 1)  # drawn meshType, transformIdx=nn-1
        struct.pack_into("<2i", mh, 56, 0, 0)  # skinIdx=0, layoutIdx=0
        struct.pack_into("<2I", mh, 104, 1, 128)
        struct.pack_into("<2I", mh, 120, 1, 132)
        mh.extend(struct.pack("<I", 0x1EB1F87F))
        mh.extend(struct.pack("<6I", 0, 0, 4, 0, 6, 0))
        buf.extend(B._pack_mesh_info_table([bytes(mh)], [0xAABBCCDD]))
    else:
        buf.extend(bytes(-len(buf) % 16))
        struct.pack_into("<I", buf, 48, len(buf))
        buf.extend(struct.pack("<I", 0))

    # @112 material_map inside native prefix
    buf.extend(bytes(-len(buf) % 16))
    struct.pack_into("<I", buf, 112, len(buf))
    buf.extend(struct.pack("<IIQ", 1, 0x1EB1F87F, 0xC445AF59E22D6FB6))
    return bytes(buf)


class ClonesR26UnitTests(unittest.TestCase):
    def test_murmur64a_matches_known_ids(self) -> None:
        self.assertEqual(B.murmur64a("clonedivers/empire_tie_switch/v1/fighter"), 0xF62538008A0EDC18)
        self.assertEqual(B.murmur64a("clonedivers/empire_tie_switch/v1/bomber"), 0x81787742B65C43F4)
        self.assertEqual(B.CLONE_FIGHTER, 0xFB909F602A347374)
        self.assertEqual(B.CLONE_BOMBER, 0x21F2F1745DF2D0B9)
        self.assertEqual(B.CLONE_GUNSHIP, 0xFE2802E03035E982)
        self.assertEqual(B.CIS_FIGHTER, 0x5DA3526B21F1DC24)

    def test_patch_unit_bounds_preserves_unit_safe_via_append_rebase(self) -> None:
        stock = _make_synthetic_stock_unit()
        self.assertTrue(P.unit_safe((stock, b"", b""), stock))
        patched = B.patch_unit_bounds_and_matmap(
            stock,
            stock,
            (-3.0, -3.0, -2.5, 3.0, 3.0, 3.5),
            5.5,
            clamp_lod_scale=True,
            clamp_cull_node=True,
            mat_rebinds={0x1EB1F87F: B.M_COLLISION},
        )
        self.assertGreater(len(patched), len(stock))
        self.assertTrue(P.unit_safe((patched, b"", b""), stock))
        VC.lod_descriptors(patched)
        self.assertEqual(VC.material_map(patched)[0x1EB1F87F], B.M_COLLISION)
        self.assertEqual(V.check_unit_matrices_and_skins(patched), [])

    def test_graft_helmet_onto_stock_unit_remaps_joints_and_preserves_external_bones(self) -> None:
        # Stock target has external bones != 0 and 3 joints (0x6F6E4346 is index 1 in dst, index 3 in src!)
        stock_dst = _make_synthetic_stock_unit(
            bones_id=0x651CCF16AB84901A,
            joint_hashes=(0x11111111, 0x6F6E4346, 0x33333333),
        )
        src_main = _make_synthetic_stock_unit(
            bones_id=0,
            joint_hashes=(0x11111111, 0x99999999, 0x33333333, 0x6F6E4346),
        )
        dummy_gpu = bytes(60)
        grafted = B.graft_helmet_onto_stock_unit(stock_dst, src_main, dummy_gpu)
        self.assertTrue(P.unit_safe((grafted, b"", dummy_gpu), stock_dst))
        self.assertEqual(struct.unpack_from("<Q", grafted, 8)[0], 0x651CCF16AB84901A)
        self.assertEqual(V.joint_hashes(grafted), [0x11111111, 0x6F6E4346, 0x33333333])
        self.assertEqual(V.check_unit_matrices_and_skins(grafted), [])
        # Verify @88 skin joint, @100 mp+44/48, and @48 cull_node all remapped from src j=3 -> dst j=1
        sat = VC.pointers(grafted, 88)[0]
        bc, _, bo, _ = struct.unpack_from("<4I", grafted, sat)
        self.assertEqual(struct.unpack_from(f"<{bc}I", grafted, sat + bo), (1,))
        mh = VC.pointers(grafted, 100)[0]
        self.assertEqual(struct.unpack_from("<2I", grafted, mh + 44), (1, 1))
        lp = VC.pointers(grafted, 48)[0]
        fc, lv = struct.unpack_from("<II", grafted, lp + 12)
        frel = struct.unpack_from("<I", grafted, lp + 20 + 4 * lv)[0]
        self.assertEqual(struct.unpack_from("<I", grafted, lp + frel + 28)[0], 1)

    def test_build_zero_draw_unit_hider_handles_corpse_and_meshless_wreck_units(self) -> None:
        corpse_stk = _make_synthetic_stock_unit()
        hm, hg = B.build_zero_draw_unit_hider(corpse_stk)
        self.assertTrue(P.unit_safe((hm, b"", hg), corpse_stk))
        self.assertEqual(V.drawn_index_count(hm), 0)
        self.assertEqual(VC.material_map(hm)[0x1EB1F87F], B.M_COLLISION)
        self.assertEqual(V.check_unit_matrices_and_skins(hm), [])
        self.assertEqual(V.check_gpu_vertices_finite(hm, hg), [])

        wreck_stk = _make_synthetic_stock_unit(with_mesh_and_layout=False)
        wm, wg = B.build_zero_draw_unit_hider(wreck_stk)
        self.assertTrue(P.unit_safe((wm, b"", wg), wreck_stk))
        self.assertEqual(wg, b"")
        self.assertEqual(V.drawn_index_count(wm), 0)
        self.assertEqual(VC.material_map(wm)[0x1EB1F87F], B.M_COLLISION)

    def test_vertex_converters_preserve_xyz_and_uvs_exact(self) -> None:
        v44 = struct.pack("<I3fI6f", 0xFFFFFFFF, 1.5, -2.5, 3.25, 0x12345678, 0.25, 0.75, 0.5, -0.5, 1.0, 0.0)
        out56 = B._convert_44_to_56_eagle_vertices(v44 * 3, 3)
        self.assertEqual(len(out56), 3 * 56)
        self.assertEqual(out56[:44], v44)
        self.assertEqual(struct.unpack_from("<4e4B", out56, 44), (1.0, 0.0, 0.0, 0.0, 0, 0, 0, 0))

        v36 = struct.pack("<I3fI8e", 0xFFFFFFFF, -4.0, 8.0, 0.5, 0x87654321, 0.5, -0.25, 1.0, 0.125, 0.0, 1.0, 0.0, 0.0)
        out56_v = B._convert_36_f16uv_to_56_eagle_vertices(v36 * 2, 2)
        self.assertEqual(len(out56_v), 2 * 56)
        self.assertEqual(out56_v[:20], v36[:20])
        self.assertEqual(struct.unpack_from("<6f", out56_v, 20), (0.5, -0.25, 1.0, 0.125, 0.0, 1.0))
        self.assertEqual(struct.unpack_from("<4e4B", out56_v, 44), (1.0, 0.0, 0.0, 0.0, 0, 0, 0, 0))

    def test_rewrite_main_archive_preserves_companion_offsets_and_preflight(self) -> None:
        u_orig = _make_synthetic_stock_unit()
        header = bytearray(72)
        struct.pack_into("<I", header, 0, 0xF0000011)
        tmp = Path(__file__).resolve().parent / "_tmp_unit_test_arch"
        tmp.mkdir(exist_ok=True)
        try:
            arch = tmp / "9ba626afa44a3aa3.patch_0"
            P.write_archive(
                arch,
                bytes(header),
                {
                    (10, B.WWISE_STREAM_T): P.Generated((bytes(12), b"RIFFdata", b"")),
                    (20, B.UNIT_T): P.Generated((u_orig, b"", b"gpu_payload_bytes")),
                },
            )
            orig_main = arch.read_bytes()
            stm_sz = arch.with_name(arch.name + ".stream").stat().st_size
            gpu_sz = arch.with_name(arch.name + ".gpu_resources").stat().st_size
            rewritten, chg = B.rewrite_main_archive_preserving_companions(
                orig_main,
                (stm_sz, gpu_sz),
                lambda uid, raw: B.patch_unit_bounds_and_matmap(raw, u_orig, (-2, -2, -2, 2, 2, 2), 4.0),
            )
            self.assertEqual(chg, 1)
            rep = B.PREFLIGHT.validate_directory(rewritten, len(rewritten), (stm_sz, gpu_sz))
            self.assertTrue(rep["passed"])
        finally:
            for f in tmp.iterdir():
                f.unlink()
            tmp.rmdir()

    def test_protected_units_exclude_scorch_fixer_sev_sc30_and_droids(self) -> None:
        protected = B.load_protected_units()
        self.assertIn(0xDE118AD3932E0A23, protected)  # CM-09 Sev (copied from, never mutated)
        self.assertIn(0x48E63F84996BE394, protected)  # DP-11 Boss (never mutated)
        self.assertIn(0x79E4B3D2DA5E45E3, protected)  # EXO-45 AT-TE walker
        for editable in (
            0xC96CB2E72D7A0525,  # B-01v3 Scorch
            0x697D6A57971DA101,  # CE-35 Scorch
            0x7B23E3C0AB4CF618,  # B-01v2 Fixer
            0x6C5BACF02C5AA0F0,  # CM-10 Fixer
            0x651CCF16AB84901A,  # SC-30 Sev
            0x9F44121C554DAE89,  # MagnaGuard female
            0x746A7F3BEDA32699,  # MagnaGuard male
            *B.CORPSE_SHARED_DROIDS,
            *B.CORPSE_ALL_DROIDS_ONLY,
        ):
            self.assertNotIn(editable, protected)


if __name__ == "__main__":
    unittest.main()
