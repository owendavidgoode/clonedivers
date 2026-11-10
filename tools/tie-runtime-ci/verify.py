"""Verify complete held packages before copying an explicit upload allowlist."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import zipfile

BASE = 0x2C26BC4C6592FA14
LOADER = 'sdk-shims/hellforge_modloader.dll'
PRIVATE_INPUTS = {
    '1db3fd414039d3e5815a5721925dd2e0a3a9f2549603c6cab7c49b84966a1af3',
    '80a6c2196f6048749ee35a141c91083b29613fd7c56c2207c9fc73ef87566547',
}


def need(ok: bool, why: str) -> None:
    if not ok:
        raise ValueError(why)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def pairs(items: list[tuple[str, object]]) -> dict[str, object]:
    result = dict(items)
    need(len(result) == len(items), 'Duplicate JSON keys')
    return result


def json_bytes(data: bytes) -> dict:
    return json.loads(data.decode('utf-8-sig'), object_pairs_hook=pairs)


def entries(archive: zipfile.ZipFile) -> dict[str, zipfile.ZipInfo]:
    result = {}
    folded = set()
    for entry in archive.infolist():
        name = entry.filename.replace('\\', '/')
        if name.endswith('/'):
            continue
        need(re.fullmatch(r'[A-Za-z0-9_. -]+(?:/[A-Za-z0-9_. -]+)*', name) is not None
             and not ({'.', '..'} & set(name.split('/'))), 'Unsafe ZIP path')
        need(name.casefold() not in folded, 'Duplicate ZIP entry')
        folded.add(name.casefold())
        result[name] = entry
    return result


def catalogue(header: bytes) -> list[tuple[int, str]]:
    section = header.replace(b'\r\n', b'\n').split(b'static const RefPackage kRefPackages[] = {\n', 1)[1].split(b'};', 1)[0]
    rows = [(int(key, 16), name.decode()) for key, name in re.findall(rb'\{(0x[0-9A-F]+)ull, "([^"]+)"\}', section)]
    need(len(rows) == len(dict(rows)), 'Duplicate catalogue ID')
    return rows


def compiled_catalogue(data: bytes, rows: list[tuple[int, str]]) -> int:
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    need(data[pe:pe+4] == b'PE\0\0', 'Invalid PE')
    count = struct.unpack_from('<H', data, pe+6)[0]
    optional_size = struct.unpack_from('<H', data, pe+20)[0]
    optional = pe+24
    need(struct.unpack_from('<H', data, optional)[0] == 0x20B, 'Expected PE32+')
    image_base = struct.unpack_from('<Q', data, optional+24)[0]
    sections = [struct.unpack_from('<III', data, optional+optional_size+40*i+12) for i in range(count)]
    pattern = re.escape(struct.pack('<Q', rows[0][0]))+b'.{8}'+re.escape(struct.pack('<Q', rows[1][0]))
    hits = list(re.finditer(pattern, data, re.DOTALL))
    need(len(hits) == 1, 'Nonunique compiled catalogue')
    start = hits[0].start()
    for index, (key, name) in enumerate(rows):
        actual, pointer = struct.unpack_from('<QQ', data, start+index*16)
        need(actual == key, f'Compiled ID differs: {index}')
        rva = pointer-image_base
        offsets = [raw+rva-va for va, length, raw in sections if va <= rva < va+length]
        need(len(offsets) == 1, 'Invalid catalogue string address')
        offset = offsets[0]
        need(data[offset:data.index(b'\0', offset)].decode() == name, 'Compiled name differs')
    need((struct.pack('<Q', BASE) in data) == (BASE in dict(rows)), 'Unexpected Eagle base identity')
    return start


def verify(tree: Path, variant: str, output: Path) -> None:
    dirty = variant == 'patched'
    engine = tree/'artifacts/engine-package/hellforge-engine-0.1.47.zip'
    payload = tree/'artifacts/package/payload'
    payload_zip = tree/'artifacts/package/payload.zip'
    setup = tree/'artifacts/package/HellForge-Setup.exe'
    rows = catalogue((tree/'native/mod-loader/reference_data.h').read_bytes())
    need(len(rows) == (180 if dirty else 179), 'Wrong catalogue size')
    if dirty:
        need(rows[-1] == (BASE, 'packages/generated/loadout/eagle_base'), 'Wrong appended row')
    with zipfile.ZipFile(engine) as archive:
        names = entries(archive)
        metadata = json_bytes(archive.read(names['engine.json']))
        need((metadata['build'], metadata['version'], metadata['commit'], metadata['steamCensus'])
             == ('25480438', '0.1.47', 'd8a8b46', False), 'Engine identity differs')
        files = {name: pin.lower() for name, pin in metadata['files'].items()}
        need(set(names) == set(files)|{'engine.json'}, 'Engine inventory mismatch')
        need({'session/HellForge.Session.exe','backend/HellForge.LocalBackend.exe',LOADER,
              'sdk-shims/steam_api64_bridge.dll','sdk-shims/hellforge_steam_broker.exe'} <= files.keys(), 'Incomplete engine')
        for name, pin in files.items():
            need(digest(archive.read(names[name])) == pin, f'Engine file hash: {name}')
        shims = json_bytes(archive.read(names['sdk-shims/build.json']))
        shim_pins = {LOADER:shims['modLoader'], 'scripts/hellforge_wfp.exe':shims['networkBlock'],
                     'sdk-shims/steam_api64_bridge.dll':shims['steamBridge'],
                     'sdk-shims/hellforge_steam_broker.exe':shims['steamBroker']}
        shim_pins.update({f'sdk-shims/{name}':pin for name,pin in shims['standIns'].items()})
        need(all(files[name] == pin.lower() for name,pin in shim_pins.items()), 'Shim receipt differs')
        offset = compiled_catalogue(archive.read(names[LOADER]), rows)
        need((payload/'app/engine.json').read_bytes() == archive.read(names['engine.json']), 'Payload engine metadata differs')
    app = json_bytes((payload/'app/app-build.json').read_bytes())
    engine_pin = file_digest(engine)
    need(file_digest(payload/'app/engine-package.zip') == engine_pin, 'Embedded engine differs')
    need((app['version'],app['commit'],app['engine'],app['uncommittedChanges'],app['enginePackageSha256'].lower())
         == ('0.4.0-beta','d8a8b46','0.1.47',dirty,engine_pin), 'App build receipt differs')
    inventory = {path.relative_to(payload).as_posix():file_digest(path) for path in sorted(payload.rglob('*')) if path.is_file()}
    need(not (set(inventory.values()) & PRIVATE_INPUTS), 'Original Steam/SDK input leaked to payload')
    need({'HellForge.exe','app/cli/HellForge.Cli.exe','app/launcher/HellForge.Launcher.exe'} <= inventory.keys(), 'Incomplete app payload')
    need(all(inventory['app/'+name] == pin for name,pin in files.items()), 'Payload engine file differs')
    with zipfile.ZipFile(payload_zip) as archive:
        names = entries(archive)
        need(names.keys() == inventory.keys(), 'Payload ZIP inventory differs')
        for name, entry in names.items():
            with archive.open(entry) as stream:
                need(hashlib.file_digest(stream,'sha256').hexdigest() == inventory[name], f'Payload ZIP differs: {name}')
    result = {'variant':variant,'sourcePath':str(tree.resolve()),'catalogue':rows,'tableOffset':hex(offset),
              'engineFiles':files,'payloadFiles':inventory,'appBuild':app,'complete':True,'releaseAccepted':False}
    for source, suffix in ((engine,'engine.zip'),(payload_zip,'payload.zip'),(setup,'setup.exe')):
        target = output/f'{variant}-{suffix}'
        need(not target.exists(), 'Artifact collision')
        shutil.copyfile(source,target)
        result[suffix] = {'sha256':file_digest(target),'bytes':target.stat().st_size}
    (output/f'{variant}-verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')


def compare(output: Path) -> None:
    baseline = json_bytes((output/'baseline-verification.json').read_bytes())
    patched = json_bytes((output/'patched-verification.json').read_bytes())
    need(baseline['sourcePath'] == patched['sourcePath'], 'Different build paths')
    need(baseline['catalogue'] == patched['catalogue'][:-1], 'Existing indices changed')
    changes = {}
    for key in ('engineFiles','payloadFiles'):
        old,new = baseline[key],patched[key]
        need(old.keys() == new.keys(), f'{key} inventory parity differs')
        changes[key] = {name:[old[name],new[name]] for name in old if old[name] != new[name]}
    (output/'pair-verification.json').write_text(json.dumps({'complete':True,'releaseAccepted':False,
        'changesRequireReview':changes},indent=2),encoding='utf-8')


def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tree',type=Path)
    parser.add_argument('--variant',choices=('baseline','patched'))
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--compare',action='store_true')
    args=parser.parse_args()
    if args.compare:
        compare(args.output)
    else:
        need(args.tree is not None and args.variant is not None,'Tree and variant required')
        verify(args.tree,args.variant,args.output)


if __name__=='__main__':
    main()
