"""Unattended HellForge drop loop for Clonedivers QA (lab use only).

Each cycle: launch (harness `start`) -> title skip to ship (`ship`) -> solo deploy (`mission`) -> hold in the
mission for --hold seconds while polling the game -> abandon + engine quit (`stop`) or collect after a crash
(`finish`). Results go to <out>/cycles.jsonl, one row per cycle, with HellForge's own crash lines from
game/modloader.log (result.json misses some crashes) and Heavy SEAF wave/squad counts.

Usage:
  python drop_loop.py --label baseline --cycles 3 --difficulty 11 --faction automaton --hold 240

Needs: Steam running, BingusDevelopmentHarness (HARNESS), a HellForge lab profile (PROFILE), and the
container's Steam ActiveProcess pid corrected when run from a packaged app (see the harness README).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

HARNESS = Path(os.environ.get('CLONEDIVERS_HARNESS', r'D:\ClaudeWork\bingus-harness'))
ROOT = os.environ.get('HD2H_HELLFORGE_ROOT', r'D:\HellForge')
PROFILE = os.environ.get('HD2H_HELLFORGE_PROFILE', 'clonedivers-armory')
HOME = Path(os.environ.setdefault('HD2H_HOME', r'D:\ClaudeWork\harness-home'))
os.environ.setdefault('HD2H_LOADER_ZIP', r'D:\ClaudeWork\releases\Bingus-Shared-Loader-v20.zip')
CRASH = re.compile(r'crash: (?:(?P<code>[0-9a-fA-F]{8}) on thread \d+ )?at (?P<site>\S+)')


def harness(*args, timeout=600):
    """Runs one harness command; returns (exit code, parsed JSON or None, combined text)."""
    command = [sys.executable, str(HARNESS / 'harness.py'), '--target', 'hellforge', '--hellforge-root', ROOT,
               '--profile', PROFILE, *args]
    try:
        done = subprocess.run(command, cwd=str(HARNESS), capture_output=True, text=True, timeout=timeout,
                              encoding='utf-8', errors='replace')
    except subprocess.TimeoutExpired as expired:
        return 124, None, 'timeout after %ss: %s' % (timeout, (expired.stdout or '')[-2000:])
    text = done.stdout + done.stderr
    data = None
    start = done.stdout.find('{')
    if start >= 0:
        try:
            data = json.loads(done.stdout[start:])
        except json.JSONDecodeError:
            pass
    return done.returncode, data, text


def game_running():
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq helldivers2.exe', '/FO', 'CSV', '/NH'],
                         capture_output=True, text=True).stdout
    return 'helldivers2.exe' in out.lower()


def latest_run():
    runs = sorted((HOME / 'runs').glob('*'), key=lambda p: p.stat().st_mtime) if (HOME / 'runs').exists() else []
    return runs[-1] if runs else None


def scan_run(run):
    """Crash lines and SEAF activity from the copied HellForge session folder of a harness run."""
    facts = {'crashes': [], 'seaf_spawns': 0, 'auto_waves': 0, 'squad_lost': 0, 'run_json': None}
    if run is None:
        return facts
    for log in run.rglob('modloader.log'):
        for line in log.read_text(encoding='utf-8', errors='replace').splitlines():
            m = CRASH.search(line)
            if m and 'by module' not in line:
                facts['crashes'].append({'code': m.group('code'), 'site': m.group('site'), 'line': line[:300]})
    for log in run.rglob('mod-local.heavy-seaf-presence.log'):
        text = log.read_text(encoding='utf-8', errors='replace')
        facts['seaf_spawns'] += text.count('SPAWN:')
        facts['auto_waves'] += len(re.findall(r'AUTO_WAVE_DISPATCH|shuttle \d+ called in', text))
        facts['squad_lost'] += text.count('no longer alive')
    run_json = run / 'run.json'
    if run_json.exists():
        data = json.loads(run_json.read_text(encoding='utf-8'))
        facts['run_json'] = {k: data.get(k) for k in ('outcome', 'clean', 'exit_code', 'ended', 'dumps', 'missions')
                             if k in data}
    return facts


def wait_for_backend_port(port=40558, limit=300):
    """Waits until the previous session's backend has released HellForge's port (a quick restart was refused)."""
    deadline = time.time() + limit
    while time.time() < deadline:
        out = subprocess.run(['netstat', '-ano', '-p', 'TCP'], capture_output=True, text=True).stdout
        # Any state counts: HellForge refuses the port while the old backend's sockets are still closing.
        if not re.search(r':%d\s' % port, out):
            return True
        time.sleep(5)
    return False


def cycle(args, number):
    row = {'label': args.label, 'cycle': number, 'difficulty': args.difficulty, 'faction': args.faction,
           'started': time.strftime('%Y-%m-%dT%H:%M:%S'), 'steps': {}}
    wait_for_backend_port()
    code, data, text = harness('start', '--preset', args.preset, timeout=300)
    if code != 0 and 'Port 40558' in text:
        harness('recover'); time.sleep(30); wait_for_backend_port()
        code, data, text = harness('start', '--preset', args.preset, timeout=300)
        row['start_retried'] = True
    row['steps']['start'] = code
    if code != 0:
        row['error'] = text[-1500:]
        harness('close'); harness('recover')
        return row
    for step, extra, limit in (('ship', [], 300),
                               ('mission', ['--difficulty', str(args.difficulty), '--faction', args.faction]
                                + (['--shots'] if args.shots else []), 600)):
        code, data, text = harness(step, *extra, timeout=limit)
        row['steps'][step] = code
        if step == 'mission' and data:
            row['mission'] = data
        if code != 0:
            row['error'] = '%s: %s' % (step, text[-1500:])
            break
    else:
        landed = time.time()
        while time.time() - landed < args.hold:
            if not game_running():
                row['died_in_mission_after_s'] = round(time.time() - landed)
                break
            time.sleep(10)
        else:
            row['held_s'] = args.hold
    if game_running():
        code, data, text = harness('stop', timeout=300)
        row['steps']['stop'] = code
        if code != 0 and game_running():
            harness('stop', '--kill', timeout=180)
    else:
        code, data, text = harness('finish', timeout=300)
        row['steps']['finish'] = code
    run = latest_run()
    row['run'] = str(run) if run else None
    row.update(scan_run(run))
    # The first engine quit after any mission hits the vanilla render-task-pool teardown crash (+0x626296);
    # count it separately so in-mission stability is measured on its own.
    kinds = [d.get('kind') for d in ((row.get('run_json') or {}).get('dumps') or [])]
    row['quit_crash'] = 'quit-render-task-pool' in kinds or any(c['site'].endswith('+0x626296') for c in row['crashes'])
    mission_crashes = [c for c in row['crashes'] if not c['site'].endswith('+0x626296')]
    row['passed'] = (not mission_crashes and 'error' not in row and 'died_in_mission_after_s' not in row)
    return row


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--label', required=True)
    parser.add_argument('--cycles', type=int, default=1)
    parser.add_argument('--difficulty', type=int, default=11)
    parser.add_argument('--faction', default='automaton', choices=('automaton', 'terminid', 'illuminate'))
    parser.add_argument('--hold', type=int, default=240, help='seconds in mission after the player has control')
    parser.add_argument('--preset', default='light', choices=('light', 'private'))
    parser.add_argument('--shots', action='store_true', help='capture briefing, landing and ship screenshots')
    parser.add_argument('--out', default=r'D:\ClaudeWork\clonedivers-lab\results')
    args = parser.parse_args()
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    for number in range(1, args.cycles + 1):
        row = cycle(args, number)
        with open(out / 'cycles.jsonl', 'a', encoding='utf-8') as sink:
            sink.write(json.dumps(row) + '\n')
        print(json.dumps({k: row.get(k) for k in ('label', 'cycle', 'passed', 'quit_crash', 'steps', 'held_s',
                                                    'died_in_mission_after_s', 'crashes', 'seaf_spawns', 'auto_waves',
                                                    'squad_lost', 'error')}),
              flush=True)


if __name__ == '__main__':
    main()
