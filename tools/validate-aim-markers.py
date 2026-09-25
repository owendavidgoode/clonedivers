#!/usr/bin/env python3
"""Independently validate a Spider Droid marker candidate with filediver.

filediver ignores loose ``.patch_N`` files, and a full export falls back to the stock
War Strider. This tool instead builds a slim-edition game view of hard links to the
installed stock files (read-only use; never edit them), adds one uncompressed DSAR
bundle holding the source and candidate units under unique fake names (plus the Spider
Droid materials they need), exports both through filediver, then:

* checks every original primitive is unchanged (indices, UVs, joints, weights exactly;
  positions/normals up to the rigid transform filediver applies to shared buffers),
* reports the added marker primitives (material, counts, rigid bone weights, UVs),
* renders orthographic views of LOD0 with the markers in red.

usage: validate-aim-markers.py CANDIDATE_PATCH OUT_DIR [--zoom x,y,z,halfwidth]

Filediver artefacts to expect: LODs 15-17 share one vertex buffer, so filediver remaps
the shared marker joints three times (boss 38 -> 36 -> 45); the stored byte is correct.
"""
import argparse, hashlib, json, os, struct, subprocess, sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r'C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2\data')
FILEDIVER = ROOT / 'dist/player-update-2026-09-23/current-kits/filediver.exe'
SPIDER = ROOT / 'dist/aim-voice-2026-09-24/spider/9ba626afa44a3aa3.patch_192'
UNIT_TYPE, UNIT_ID = 0xe0a48d0be9a7453f, 0xef570293245a17c2
SOURCE_NAME, CANDIDATE_NAME = 0x5eed00000000a001, 0x5eed00000000a00c
BUNDLE = '5eed000000000001'


# --- archives -------------------------------------------------------------------------
def archive_rows(path):
    b = path.read_bytes()
    nt, nf = struct.unpack_from('<II', b, 4)
    gpu_path = Path(str(path) + '.gpu_resources')
    g = gpu_path.read_bytes() if gpu_path.exists() else b''
    for i in range(nf):
        n, t, mo, so, go, _, _, ms, ss, gs = struct.unpack_from('<QQQQQQQIII', b, 72 + nt * 32 + i * 80)
        assert ss == 0, 'stream data unsupported'
        yield n, t, b[mo:mo + ms], g[go:go + gs]


def unit_of(path):
    return next((m, g) for n, t, m, g in archive_rows(path) if (n, t) == (UNIT_ID, UNIT_TYPE))


def dsar(segments):
    """Uncompressed DSAR container; each segment is one chunk (filediver needs file starts on chunk starts)."""
    info = 32 + 32 * len(segments)
    head = struct.pack('<4s4sIIQ8s', b'DSAR', bytes([3, 0, 1, 0]), len(segments), info, sum(map(len, segments)), b'\0' * 8)
    rows, uoff, coff = [], 0, info
    for i, s in enumerate(segments):
        rows.append(struct.pack('<QQIIBB6s', uoff, coff, len(s), len(s), 0, 2 if i == 0 else 0, b'\0' * 6))
        uoff += len(s); coff += len(s)
    return head + b''.join(rows) + b''.join(segments)


def write_bundle(out, entries):
    align = lambda v, a: (v + a - 1) // a * a
    types = sorted({e[1] for e in entries})
    header_size = 72 + 32 * len(types) + 80 * len(entries)
    mcur = align(header_size, 16); pad0 = mcur - header_size
    main_segments, gpu_segments, rows, gcur = [], [], [], 0
    for i, (name, typ, main, gpu) in enumerate(entries):
        mo = mcur; main_segments.append(main); mcur += len(main)
        if (p := align(mcur, 16) - mcur) and i + 1 < len(entries): main_segments.append(b'\0' * p); mcur += p
        go = gcur if gpu else 0
        if gpu:
            gpu_segments.append(gpu); gcur += len(gpu)
            if (p := align(gcur, 256) - gcur) and i + 1 < len(entries): gpu_segments.append(b'\0' * p); gcur += p
        rows.append(struct.pack('<QQQQQQQIIIIII', name, typ, mo, 0, go, 0, 0, len(main), 0, len(gpu), 16, 256 if gpu else 16, i))
    head = struct.pack('<4sII20sQQ24s', b'\x11\x00\x00\xf0', len(types), len(entries), b'\0' * 20, align(mcur, 256), align(gcur, 256), b'\0' * 24)
    tbl = b''.join(struct.pack('<IIQIIII', 0, 0, t, sum(e[1] == t for e in entries), 0, 16, 256) for t in types)
    out.write_bytes(dsar([head + tbl + b''.join(rows) + b'\0' * pad0] + main_segments))
    Path(str(out) + '.gpu_resources').write_bytes(dsar(gpu_segments))


def game_view(data):
    """Hard links to installed stock files only (no *.patch_*); contents are shared, so read-only."""
    data.mkdir(parents=True, exist_ok=True)
    for f in GAME.iterdir():
        if '.patch_' in f.name or not f.is_file():
            continue
        target = data / f.name
        if target.exists():
            assert os.path.samefile(target, f), f'{target} is not a link to the stock file'
        else:
            os.link(f, target)


# --- glTF -----------------------------------------------------------------------------
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def load_glb(path):
    b = path.read_bytes(); assert b[:4] == b'glTF'
    jl = struct.unpack_from('<I', b, 12)[0]; doc = json.loads(b[20:20 + jl])
    return doc, b[28 + jl:28 + jl + struct.unpack_from('<I', b, 20 + jl)[0]]


def accessor(doc, binc, i):
    a = doc['accessors'][i]; v = doc['bufferViews'][a['bufferView']]
    dt = np.dtype(CT[a['componentType']]); n = NC[a['type']]
    off = v.get('byteOffset', 0) + a.get('byteOffset', 0); stride = v.get('byteStride', 0) or dt.itemsize * n
    raw = np.frombuffer(binc, np.uint8, count=stride * (a['count'] - 1) + dt.itemsize * n, offset=off)
    arr = np.lib.stride_tricks.as_strided(raw, (a['count'], dt.itemsize * n), (stride, 1)).copy().view(dt).reshape(a['count'], n)
    return arr.astype(np.float32) / np.iinfo(dt).max if a.get('normalized') and dt.kind in 'ui' else arr


def primitives(doc, binc):
    out = {}
    for mi, m in enumerate(doc['meshes']):
        for pi, p in enumerate(m['primitives']):
            d = {k: accessor(doc, binc, ai) for k, ai in p['attributes'].items()}
            d['indices'] = accessor(doc, binc, p['indices']).ravel()
            d['material'] = doc['materials'][p['material']].get('name', '')
            out[(mi, pi)] = d
    return out


def compare(src, cand):
    sp, cp = primitives(*load_glb(src)), primitives(*load_glb(cand))
    problems, rigid = [], 0
    for k, a in sp.items():
        b = cp[k]
        for at in a:
            if at in ('POSITION', 'NORMAL', 'TANGENT'):
                continue
            if at == 'material':
                if a[at] != b[at]: problems.append(f'{k} material')
            elif not np.array_equal(a[at], b[at]):
                problems.append(f'{k} {at}')
        for extra in set(b) - set(a):
            if extra.startswith('TEXCOORD') and np.abs(b[extra]).max() != 0:
                problems.append(f'{k} new {extra} not zero')
        P, Q = a['POSITION'].astype(float), b['POSITION'].astype(float)
        X = np.c_[P, np.ones(len(P))]; M = np.linalg.lstsq(X, Q, rcond=None)[0]; R = M[:3]
        if (np.abs(R @ R.T - np.eye(3)).max() < 1e-4 and np.abs(X @ M - Q).max() < 1e-3
                and np.abs(a['NORMAL'][:, :3] @ R - b['NORMAL'][:, :3]).max() < 2e-2):
            rigid += 1
        else:
            problems.append(f'{k} geometry')
    added = []
    for k in sorted(set(cp) - set(sp)):
        d = cp[k]; used = np.unique(d['indices']); weights = {}
        for j, w in zip(d['JOINTS_0'][used], d['WEIGHTS_0'][used]):
            key = ','.join(f'{int(x)}:{float(y):.3f}' for x, y in zip(j, w) if y > 0); weights[key] = weights.get(key, 0) + 1
        added.append({'mesh': k[0], 'primitive': k[1], 'material': d['material'], 'vertices': int(len(used)),
                      'triangles': int(len(d['indices']) // 3), 'jointWeights': weights,
                      'uvMeans': {t: d[t][used].mean(0).round(4).tolist() for t in sorted(d) if t.startswith('TEXCOORD')}})
    return {'originalPrimitives': len(sp), 'rigidOrExact': rigid, 'problems': problems, 'added': added}


# --- rendering ------------------------------------------------------------------------
def render(glb, mesh_index, out, zoom=None):
    doc, binc = load_glb(glb)
    parts = []
    for p in doc['meshes'][mesh_index]['primitives']:
        pos = accessor(doc, binc, p['attributes']['POSITION']).astype(float)
        tri = accessor(doc, binc, p['indices']).ravel().reshape(-1, 3)
        parts.append((pos, tri, 'boteye' in doc['materials'][p['material']].get('name', '')))
    V = np.concatenate([p[0] for p in parts]); base = np.cumsum([0] + [len(p[0]) for p in parts])
    F = np.concatenate([p[1] + base[i] for i, p in enumerate(parts)])
    mark = np.concatenate([np.full(len(p[1]), p[2]) for p in parts]); T = V[F]
    # Stingray frame: x lateral, y forward, z up.
    views = [('front (from +Y)', [[-1, 0, 0], [0, 0, 1]], [0, 1, 0]), ('rear (from -Y)', [[1, 0, 0], [0, 0, 1]], [0, -1, 0]),
             ('right side (from +X)', [[0, 1, 0], [0, 0, 1]], [1, 0, 0]), ('top (from +Z)', [[1, 0, 0], [0, 1, 0]], [0, 0, 1]),
             ('below (from -Z)', [[1, 0, 0], [0, -1, 0]], [0, 0, -1]), ('3/4 front-left-above', None, [-0.6, 0.6, 0.53])]
    W = H = 560; img = Image.new('RGB', (W * 3, H * 2), '#0f172a')
    for vi, (label, axes, eye) in enumerate(views):
        eye = np.array(eye, float); eye /= np.linalg.norm(eye)
        if axes is None:
            right = np.cross([0, 0, 1], eye); right /= np.linalg.norm(right); axes = [right, np.cross(eye, right)]
        axes = np.array(axes, float); p2 = V @ axes.T; lo, hi = p2.min(0), p2.max(0)
        if zoom is not None:
            c2 = np.array(zoom[:3]) @ axes.T; lo, hi = c2 - zoom[3], c2 + zoom[3]
        s = 0.9 * min(W / (hi[0] - lo[0]), H / (hi[1] - lo[1]))
        scr = np.column_stack(((p2[:, 0] - (lo[0] + hi[0]) / 2) * s + W / 2, H / 2 - (p2[:, 1] - (lo[1] + hi[1]) / 2) * s))
        n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]); shade = np.abs(n @ eye) / np.maximum(np.linalg.norm(n, axis=1), 1e-12)
        tile = Image.new('RGB', (W, H), '#0f172a'); d = ImageDraw.Draw(tile)
        for t in np.argsort((V @ eye)[F].mean(1)):  # painter's order; small markers can be under-drawn at full scale
            g = int(60 + 150 * shade[t]); d.polygon([tuple(q) for q in scr[F[t]]], fill=(255, 40, 40) if mark[t] else (g, g, g))
        d.text((10, 10), label, fill='white'); img.paste(tile, ((vi % 3) * W, (vi // 3) * H))
    img.save(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('candidate', type=Path, help='candidate .patch_N (UNIT override for ef570293245a17c2)')
    ap.add_argument('out', type=Path)
    ap.add_argument('--zoom', action='append', default=[], help='x,y,z,halfwidth close-up (repeatable)')
    args = ap.parse_args()
    out = args.out; out.mkdir(parents=True, exist_ok=True); data = out / 'game/data'
    game_view(data)
    entries = [(n, t, m, g) for n, t, m, g in archive_rows(SPIDER)]  # materials/textures the unit references
    for name, path in [(SOURCE_NAME, SPIDER), (CANDIDATE_NAME, args.candidate)]:
        m, g = unit_of(path); entries.append((name, UNIT_TYPE, m, g))
    write_bundle(data / BUNDLE, entries)
    export = out / 'export'
    log = subprocess.run([str(FILEDIVER), '--gamedir', str(out / 'game'), '--include', '*5eed00000000a00*', '--types', 'unit',
                          '--model-format', 'glb', '--model-include-lods', '--model-include-gibs', '--out', str(export)],
                         capture_output=True, text=True)
    (out / 'filediver.log').write_text(log.stdout + log.stderr)
    src, cand = export / f'0x{SOURCE_NAME:016x}.unit.glb', export / f'0x{CANDIDATE_NAME:016x}.unit.glb'
    assert src.exists() and cand.exists(), f'filediver export failed; see {out / "filediver.log"}'
    report = compare(src, cand)
    report['candidateSha256'] = hashlib.sha256(args.candidate.read_bytes()).hexdigest()
    lod0 = max((a for a in report['added']), key=lambda a: a['mesh'])['mesh'] if report['added'] else 18
    render(cand, lod0, out / 'views.png')
    for i, z in enumerate(args.zoom):
        render(cand, lod0, out / f'zoom-{i}.png', [float(x) for x in z.split(',')])
    (out / 'report.json').write_text(json.dumps(report, indent=2))
    print(json.dumps({k: v for k, v in report.items() if k != 'added'}, indent=1))
    for a in report['added']:
        print(f"mesh {a['mesh']}: {a['material']} {a['vertices']} verts {a['triangles']} tris weights {a['jointWeights']}")
    sys.exit(1 if report['problems'] else 0)


if __name__ == '__main__':
    main()
