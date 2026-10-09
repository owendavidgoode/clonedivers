#!/usr/bin/env python3
"""Seal the armor/peer handoff after final packaged v4 review."""
from __future__ import annotations

from pathlib import Path


def main() -> None:
    source = Path(__file__).with_name("yoda-death-armor-handoff.py").read_text()
    source = source.replace('"handoff-v1"', '"handoff-v2"')
    source = source.replace('EXPECTED = {', 'EXPECTED = {\n    "package-peer-v2": "4e02c49734194a86f650f10f1969410737c2c191f388ca333735867b14e0a111",')
    source = source.replace('"preparedV2Fixtures": 81,', '"preparedV2Fixtures": 81, "finalPackagedBodyFixtures": 81, "finalContextFixtures": 63, "extractedPrivateModules": 8,')
    source = source.replace('"Verify exact modules embedded in final addon against the reviewed prepared reader.",\n                            ', '')
    exec(compile(source, str(Path(__file__).resolve()), "exec"), {"__name__": "__main__", "__file__": __file__})


if __name__ == "__main__":
    main()
