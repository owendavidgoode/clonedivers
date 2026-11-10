"""Regression tests for option provenance, payload isolation and catalog gates."""
import json
from pathlib import Path
import struct
import shutil
import tempfile
import unittest
from uuid import uuid4

import empire_port as P
import prepare_droids as D


class DroidTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.gettempdir()) / f'empire-droids-test-{uuid4().hex}'
        self.root.mkdir()
        self.old = P.OLD
        P.OLD = self.root

    def tearDown(self) -> None:
        P.OLD = self.old
        P.SOURCE_PINS.clear()
        shutil.rmtree(self.root)

    def archive(self, path: Path, keys: list[int]) -> dict:
        header = bytearray(72)
        struct.pack_into('<I', header, 0, 0xf0000011)
        P.write_archive(path, header, {(key, P.UNIT): P.Generated((bytes([key]) * 128, b'', b'gpu')) for key in keys})
        return P.pin(path)

    def test_classifier_uses_hashes_and_retains_shared_providers(self) -> None:
        common = self.archive(self.root / 'common', [1])
        cis = self.archive(self.root / 'cis', [1, 2])
        on = [{**common, 'name': '9ba626afa44a3aa3.patch_1'}, {**cis, 'name': '9ba626afa44a3aa3.patch_2'}]
        off = [{**common, 'name': '9ba626afa44a3aa3.patch_99'}]
        selector, registry = self.root / 'selector.json', self.root / 'registry.json'
        selector.write_text(json.dumps({'selections': [
            {'mode': 'EmpireDivers', 'profile': 'full', 'requestedOptions': {'droids': enabled, 'covenant': False}, 'fileSet': key}
            for enabled, key in ((True, 'on'), (False, 'off'))], 'fileSets': {'on': on, 'off': off}}))
        registry.write_text(json.dumps({r['sha256']: r['path'] for r in on}))
        keys, proof = D.option_keys(selector, registry)
        self.assertEqual(keys, {(2, P.UNIT)})
        self.assertEqual(proof['sharedRetained'], [D.label((1, P.UNIT))])

    def test_native_variant_removes_only_option_payload_and_preserves_source(self) -> None:
        source = self.root / 'source'
        source.mkdir()
        archive = source / '9ba626afa44a3aa3.patch_1000'
        self.archive(archive, [1, 2])
        (source / 'manifest.json').write_text('{}')
        pins = [P.pin(source / (archive.name + s)) for s in P.SUFFIXES]
        output = self.root / 'native'
        D.build_variant(source, output, {(2, P.UNIT)}, 'fixture native')
        actual, original = D.resources(output), D.resources(source)
        self.assertEqual(set(actual), {(1, P.UNIT)})
        self.assertEqual(actual[1, P.UNIT].parts(), original[1, P.UNIT].parts())
        self.assertEqual(pins, [P.pin(source / (archive.name + s)) for s in P.SUFFIXES])

    def test_catalog_uses_mutually_exclusive_rows_and_reuses_on_assets(self) -> None:
        on, off, assets = (self.root / s for s in ('on', 'off', 'assets'))
        for folder in (on, off, assets):
            folder.mkdir()
        name = '9ba626afa44a3aa3.patch_1000'
        (on / name).write_bytes(b'cis')
        (off / name).write_bytes(b'native')
        pin = P.pin(on / name)
        feed = {'pack': {'files': [{'name': name, 'textureProfiles': ['full'], 'sha256': pin['sha256'], 'size': pin['size'], 'url': 'original'}]}}
        source, output = self.root / 'source.json', self.root / 'catalog.json'
        source.write_text(json.dumps(feed))
        uploads = D.stage_catalog(source, output, {'full': (on, off)}, assets)
        rows = P.read(output)['pack']['files']
        self.assertEqual((rows[0]['option'], rows[1]['unlessOption']), ('droids', 'droids'))
        self.assertEqual(rows[0]['url'], 'original')
        self.assertEqual(len(uploads), 1)
        self.assertEqual((assets / uploads[0]['sha256']).read_bytes(), b'native')


if __name__ == '__main__':
    unittest.main()
