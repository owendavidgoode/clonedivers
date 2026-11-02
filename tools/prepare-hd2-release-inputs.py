#!/usr/bin/env -S uv run
# /// script
# requires-python = ">=3.14"
# dependencies = ["lz4>=4.4"]
# ///
"""Materialize verified release profiles without changing an installed game."""

import argparse
import importlib
import logging
import os
import shutil
import sys
import urllib.request
from pathlib import Path

U = importlib.import_module('update-hd2-compatibility')
LOG = logging.getLogger(__name__)


def prepare(args: argparse.Namespace) -> None:
    U.need(not args.out.exists(), 'Fresh release input directory')
    qa = U.read(args.verification/'report.json')
    U.need(qa['passed'] and not qa['issues'] and qa['manifest'] == U.pin(args.stage/'manifest.json'), 'Verified exact candidate')
    manifest = U.read(args.stage/'manifest.json')
    selection = U.read(args.stage/'production96.json')
    source = U.read(args.stage.parent/'source-selection.json')
    assets = U.Assets(args.workspace, manifest['pack']['files'], args.stage.parent/'source-cache')
    for proof in U.read(args.stage/'assets.json')['assets']:
        assets.sources[proof['sha256']].insert(0, Path(proof['path']))
    defaults = {o['id']: o['default'] for o in manifest['pack']['options']}

    def default_set(doc: dict, profile: str) -> list[dict]:
        matches = [s for s in doc['selections'] if s['mode'] == 'Clonedivers' and s['profile'] == profile and all(s['requestedOptions'][k] == defaults[k] for k in ('droids', 'covenant', 'commandos', 'aimpoints'))]
        U.need(len(matches) == 1, 'One actual default production selection')
        return doc['fileSets'][matches[0]['fileSet']]

    args.out.mkdir(parents=True)
    profiles = []
    for profile in ('full', 'lighter'):
        # r24's immutable release inputs provide physical-name sources for large
        # unchanged assets that are not present in a hash-named cache.
        old_dir = args.workspace/'dist/production-r24-2026-10-09/release-inputs-v2/profiles'/profile
        for file in default_set(source, profile):
            assets.sources[file['sha256']].append(old_dir/file['name'])
        target_dir = args.out/'profiles'/profile
        target_dir.mkdir(parents=True)
        for file in default_set(selection, profile):
            target = target_dir/file['name']
            if not file['size']:
                target.write_bytes(b'')
                continue
            path = assets.path(file)
            try:
                os.link(path, target)
            except OSError:
                shutil.copyfile(path, target)
        profiles.append({'id': profile, 'directory': str(target_dir.resolve())})
        LOG.info('Prepared %s: %d selected files', profile, len(default_set(selection, profile)))
    U.save(args.out/'profile-directories.json', profiles)
    app = manifest['app']
    U.need(app['url'].startswith('https://github.com/owendavidgoode/clonedivers/releases/download/'), 'Published launcher URL')
    target = args.out/'Clonedivers.exe'
    with urllib.request.urlopen(app['url'], timeout=60) as source_app, target.open('wb') as handle:
        shutil.copyfileobj(source_app, handle)
    proof = U.pin(target)
    U.need(proof['sha256'] == app['sha256'] and proof['bytes'] == app['size'], 'Existing public launcher identity')
    U.save(args.out/'report.json', {'passed': True, 'manifest': qa['manifest'], 'verification': U.pin(args.verification/'report.json'), 'profileDirectories': profiles, 'app': proof, 'installed': False, 'published': False})


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, suggest_on_error=True)
    for name in ('workspace', 'stage', 'verification', 'out'):
        parser.add_argument('--'+name, type=Path, required=True)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    try:
        prepare(args)
        return 0
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, KeyError):
        LOG.exception('Release input preparation failed')
        return 1


if __name__ == '__main__':
    sys.exit(main())
