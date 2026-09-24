#!/usr/bin/env python3
"""Download selected ZIP members via validated HTTP byte ranges, without extraction."""

import argparse
import io
import json
import logging
import re
import sys
import urllib.request
import zipfile
from pathlib import Path

LOG = logging.getLogger(__name__)


class RemoteZip(io.RawIOBase):
    def __init__(self, url: str):
        self.url = url
        self.position = 0
        self.blocks: dict[int, bytes] = {}
        request = urllib.request.Request(url, headers={'Range': 'bytes=0-0'})
        with urllib.request.urlopen(request, timeout=30) as response:
            match = re.fullmatch(r'bytes 0-0/(\d+)', response.headers.get('Content-Range', ''))
            if response.status != 206 or not match:
                raise ValueError('Server does not support a validated range download')
            self.length = int(match.group(1))

    def seekable(self) -> bool:
        return True

    def readable(self) -> bool:
        return True

    def tell(self) -> int:
        return self.position

    def seek(self, offset: int, whence: int = 0) -> int:
        self.position = offset + (self.position if whence == 1 else self.length if whence == 2 else 0)
        if self.position < 0:
            raise ValueError('Negative ZIP offset')
        return self.position

    def read(self, size: int = -1) -> bytes:
        stop = self.length if size < 0 else min(self.length, self.position + size)
        result = bytearray()
        block_size = 1024 * 1024
        while self.position < stop:
            index, inside = divmod(self.position, block_size)
            if index not in self.blocks:
                start = index * block_size
                end = min(self.length, start + block_size) - 1
                request = urllib.request.Request(self.url, headers={'Range': f'bytes={start}-{end}'})
                with urllib.request.urlopen(request, timeout=60) as response:
                    if response.status != 206 or response.headers.get('Content-Range') != f'bytes {start}-{end}/{self.length}':
                        raise ValueError('Unexpected ZIP range response')
                    data = response.read(end-start+2)
                    if len(data) != end-start+1:
                        raise ValueError('Truncated ZIP range')
                    self.blocks[index] = data
            count = min(stop-self.position, len(self.blocks[index])-inside)
            result.extend(self.blocks[index][inside:inside+count])
            self.position += count
        return bytes(result)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url', required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--prefix', action='append', default=[])
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        if args.out.exists():
            raise ValueError('Choose a fresh output directory')
        args.out.mkdir(parents=True)
        remote = RemoteZip(args.url)
        with zipfile.ZipFile(remote) as archive:
            inventory = [{'name': item.filename, 'size': item.file_size, 'compressed': item.compress_size} for item in archive.infolist()]
            (args.out / 'inventory.json').write_text(json.dumps(inventory, indent=2), encoding='utf-8')
            with zipfile.ZipFile(args.out / 'selected.zip', 'w', compression=zipfile.ZIP_DEFLATED) as selected:
                for item in archive.infolist():
                    if not item.is_dir() and (item.filename.lower() == 'manifest.json' or any(item.filename.startswith(prefix.rstrip('/') + '/') for prefix in args.prefix)):
                        selected.writestr(item.filename, archive.read(item))
        LOG.info('Read %s ZIP members; fetched %.1f MiB of %.1f MiB.', len(inventory), sum(map(len, remote.blocks.values())) / 2**20, remote.length / 2**20)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, zipfile.BadZipFile):
        # Signed download URLs must not be written to failure logs.
        LOG.error('ZIP retrieval failed; check availability, range support and the selected names.')
        return 1


if __name__ == '__main__':
    sys.exit(main())
