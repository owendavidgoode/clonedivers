#!/usr/bin/env python3
"""Rebase the sealed extraction peer onto the LastAccessTime-corrected package."""
from __future__ import annotations

import hashlib
from pathlib import Path


def main() -> None:
    prior = Path(__file__).with_name("yoda-death-armor-package-peer.py")
    raw = prior.read_bytes()
    if hashlib.sha256(raw).hexdigest() != "04509df8f8a39b5f68efaf67274a9f11c2e2f0ca8524b80e69c758dd9b107381":
        raise ValueError("Independent v3 extraction peer source changed")
    source = raw.decode().replace("candidate-v3", "candidate-v4").replace("package-peer-v1", "package-peer-v2")
    source = source.replace("bb8f255af5294903fe07c042658b8df566715d39b7aba5b67d70025cc2acb015", "204e10bfbdca5085b7518d051fc6b0242c8c07e348bc9a32f11861506f355cf3")
    source = source.replace('len(context_result["checks"]) == 60', 'len(context_result["checks"]) == 63')
    source = source.replace('    output.mkdir(parents=True)\n', '''    checks = {item['name']: item for item in context_result['checks']}
    need(checks['last-access-only-change-accepted']['accepted'] is True, 'Normal file reads cannot disarm')
    need(checks['last-write-change-disarms']['accepted'] is False, 'Changed write time must disarm')
    need(checks['file-size-change-disarms']['accepted'] is False, 'Changed file size must disarm')
    need('return ffi.string(data,12)..ffi.string(data+5,16)' in modules['context'], 'Exact28B Win32 stamp excludes LastAccessTime only')
    for name in set(modules) - {'context'}:
        need(modules[name] == (AREA / 'runtime/candidate-v3' / (name + '.lua')).read_text(), 'Other seven packaged modules unchanged')
    output.mkdir(parents=True)
''')
    exec(compile(source, str(Path(__file__).resolve()), "exec"), {"__name__": "__main__", "__file__": __file__})


if __name__ == "__main__":
    main()
