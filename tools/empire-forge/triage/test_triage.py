#!/usr/bin/env python3
"""Focused regressions for captured crash arithmetic and delivered variant deltas."""
from __future__ import annotations

import json
from pathlib import Path
import struct
import sys
import unittest

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import empire_port as P
from analyze_dump import Dump

OUT = ROOT / 'triage-r45'


class CrashEvidenceTests(unittest.TestCase):
    def test_three_dumps_have_exact_fault_arithmetic(self) -> None:
        for name in ('game-dump', 'second-game-dump', 'third-game-dump'):
            with self.subTest(name=name):
                report = P.read(OUT / f'{name}.json')
                dump = Dump(Path(report['dump']))
                regs = dump.registers
                self.assertEqual(dump.code, 0xc0000005)
                self.assertEqual(dump.address - report['module']['base'], 0x2bd8ad)
                self.assertEqual(dump.read(dump.address, 4), bytes.fromhex('4a8b04c0'))
                self.assertEqual(regs['rbx'], 0xa61438bc)
                self.assertEqual(regs['rdx'], 0xffffffff)
                self.assertEqual(regs['r8'], 2 * (regs['rdx'] & 0xffffffff))
                self.assertEqual(dump.parameters, (0, regs['rax'] + regs['r8'] * 8))
                self.assertEqual(dump.parameters[1] - regs['rax'], 0xffffffff0)
                self.assertEqual(regs['r14'], 0x85d)

    def test_hellforge_dump_captures_runtime_table(self) -> None:
        report = P.read(OUT / 'hellforge-dump.json')
        dump = Dump(Path(report['dump']))
        regs = dump.registers
        unit = dump.read(regs['rcx'], 256)
        self.assertIsNotNone(unit)
        self.assertEqual(struct.unpack_from('<I', unit, 8)[0], regs['r14'])
        self.assertEqual(struct.unpack_from('<II', unit, 0xc8), (0, 4))
        self.assertEqual(struct.unpack_from('<Q', unit, 0xd0)[0], regs['rax'])

    def test_cape_native_contract_and_r19_inline_geometry(self) -> None:
        records = P.read(OUT / 'cape-contracts.json')
        self.assertEqual(len(records), 2)
        for record in records:
            self.assertEqual(record['r45']['payloadSha256'], record['nativeProviders'][0]['payloadSha256'])
            self.assertEqual(record['r45']['sizes'][2], 0)
            self.assertGreater(record['r19']['sizes'][2], 0)
            self.assertEqual(struct.unpack_from('<Q', bytes.fromhex(record['r45']['header']), 16)[0], 0xc6a69260cc389bcd)
            self.assertEqual(struct.unpack_from('<Q', bytes.fromhex(record['r19']['header']), 16)[0], 0)
        inventory = P.read(OUT / 'expanded-inventory.json')
        self.assertEqual({r['key'] for r in inventory['nativeOnlyGroupUnits']}, {r['key'] for r in records})


class VariantTests(unittest.TestCase):
    def test_exact_variant_payload_deltas_and_unique_guids(self) -> None:
        expected = P.read(ROOT / 'expected-resources.json')
        source_guid = P.read(ROOT / 'empire-forge-r45-v3/manifest.json')['Guid']
        guids = {source_guid}
        variants = ('a-medium-r19', 'b-native-capes', 'c-cape-geometry', 'd-native-text')
        for name in variants:
            with self.subTest(name=name):
                evidence = OUT / name
                build = P.read(evidence / 'build.json')
                actual = P.read(evidence / 'expected-resources.json')
                changed = {key for key in expected.keys() | actual.keys() if expected.get(key) != actual.get(key)}
                self.assertEqual(changed, {r['key'] for r in build['changes']})
                self.assertEqual(len(changed), {'a-medium-r19': 1, 'b-native-capes': 2, 'c-cape-geometry': 1, 'd-native-text': 13}[name])
                manifest = P.read(Path(build['folder']) / 'manifest.json')
                self.assertNotIn(manifest['Guid'], guids)
                guids.add(manifest['Guid'])
                self.assertEqual(manifest['Options'], [])
                report = P.read(evidence / 'verification-summary.json')
                self.assertTrue(build['sourceFilesUnchanged'])
                self.assertTrue(build['verificationPassed'])
                self.assertTrue(report['passed'])
                self.assertTrue(report['archiveRulesPassed'])
                self.assertTrue(report['dependencyClosurePassed'])
                self.assertEqual(report['archives'], 32)
                self.assertEqual(report['files'], 102)
                self.assertFalse(report['roundtripErrors'])
                self.assertEqual(report['missingDependencies'], 0)


if __name__ == '__main__':
    unittest.main()
