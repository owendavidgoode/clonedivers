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
from datetime import datetime, time as datetime_time, timedelta, timezone
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

HARNESS = Path(os.environ.get('CLONEDIVERS_HARNESS', r'D:\ClaudeWork\bingus-harness'))
ROOT = os.environ.get('HD2H_HELLFORGE_ROOT', r'D:\HellForge')
PROFILE = os.environ.get('HD2H_HELLFORGE_PROFILE', 'clonedivers-armory')
HOME = Path(os.environ.setdefault('HD2H_HOME', r'D:\ClaudeWork\harness-home'))
os.environ.setdefault('HD2H_LOADER_ZIP', r'D:\ClaudeWork\releases\Bingus-Shared-Loader-v20.zip')
CRASH = re.compile(r'crash: (?:(?P<code>[0-9a-fA-F]{8}) on thread \d+ )?at (?P<site>\S+)')
HEARTBEAT_LIMIT = 60


def parse_queue_request(text: str) -> dict[str, Any]:
    """Read strict JSON, preserving escaped Windows paths. Never repair invalid escapes."""
    data = json.loads(text.lstrip('\ufeff'))
    if not isinstance(data, dict):
        raise ValueError('queue request must be a JSON object')
    for key in ('name', 'zip_or_folder', 'kind', 'checks'):
        if not isinstance(data.get(key), str) or not data[key].strip():
            raise ValueError('queue request requires a nonempty string: ' + key)
    if any(ord(char) < 32 for char in data['zip_or_folder']):
        raise ValueError('queue path contains a control character; serialize paths with json.dumps')
    return data


def load_queue_request(path: Path) -> dict[str, Any]:
    return parse_queue_request(path.read_text(encoding='utf-8-sig'))


def read_object(path: Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(data, dict):
        raise ValueError('expected a JSON object in ' + str(path))
    return data


def active_session() -> dict[str, Any] | None:
    try:
        return read_object(HOME / 'state' / 'active.json')
    except FileNotFoundError:
        return None


def require_lab_profile() -> None:
    if PROFILE != 'clonedivers-armory':
        raise RuntimeError('This driver requires the explicit lab profile clonedivers-armory')


def require_owned_session(session: str) -> dict[str, Any]:
    active = active_session()
    if not active or active.get('session') != session:
        raise RuntimeError('The active harness session changed; refusing to control it')
    if active.get('target') != 'hellforge' or active.get('profile') != PROFILE:
        raise RuntimeError('Refusing a non-lab harness session')
    return active


def prestart() -> None:
    require_lab_profile()
    if active_session() is not None:
        raise RuntimeError('An active harness session already exists; resolve it before starting')
    processes = subprocess.run(['tasklist', '/FO', 'CSV', '/NH'], capture_output=True, text=True, check=True).stdout.lower()
    if 'helldivers2.exe' in processes or 'hellforge.exe' in processes:
        raise RuntimeError('Game or HellForge is already running')
    status = subprocess.run([str(Path(ROOT) / 'app/cli/HellForge.Cli.exe'), 'status'],
                            capture_output=True, text=True, encoding='utf-8', check=True, timeout=60)
    data = json.loads(status.stdout.lstrip('\ufeff'))
    if (data.get('session') or {}).get('running') is not False:
        raise RuntimeError('HellForge did not report an idle session')
    guard = os.environ.get('CLONEDIVERS_LAB_GUARD')
    if guard:
        subprocess.run(['powershell.exe', '-NoProfile', '-File', guard], check=True, timeout=60)


def harness(*args: str, timeout: int = 600, session: str | None = None) -> tuple[int, Any, str]:
    """Runs one harness command; returns (exit code, parsed JSON or None, combined text)."""
    require_lab_profile()
    active = active_session()
    if active and (active.get('target') != 'hellforge' or active.get('profile') != PROFILE):
        raise RuntimeError('Refusing a non-lab harness session')
    if session is not None:
        require_owned_session(session)
    command = [sys.executable, str(HARNESS / 'harness.py'), '--target', 'hellforge', '--hellforge-root', ROOT,
               '--profile', PROFILE, *args]
    try:
        done = subprocess.run(command, cwd=str(HARNESS), capture_output=True, text=True, timeout=timeout,
                              encoding='utf-8', errors='replace')
    except subprocess.TimeoutExpired as expired:
        output = expired.stdout or b''
        if isinstance(output, bytes):
            output = output.decode('utf-8', errors='replace')
        return 124, None, 'timeout after %ss: %s' % (timeout, output[-2000:])
    text = done.stdout + done.stderr
    data = None
    start = done.stdout.find('{')
    if start >= 0:
        try:
            data = json.loads(done.stdout[start:])
        except json.JSONDecodeError:
            pass
    return done.returncode, data, text


def game_running() -> bool:
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq helldivers2.exe', '/FO', 'CSV', '/NH'],
                         capture_output=True, text=True, check=True).stdout
    return 'helldivers2.exe' in out.lower()


def freeze_in_mission(line: str, missions: list[dict[str, Any]], ended: str) -> bool:
    """HellForge log clocks are UTC time-of-day. Match each mission across midnight too."""
    clock = re.match(r'(\d{2}:\d{2}:\d{2}(?:\.\d+)?)', line)
    if 'freeze:' not in line or not clock:
        return False
    for mission in missions:
        if not mission.get('in_control'):
            continue
        control = datetime.fromisoformat(mission['in_control']).astimezone(timezone.utc)
        end = datetime.fromisoformat(mission.get('left') or ended).astimezone(timezone.utc)
        at = datetime.combine(control.date(), datetime_time.fromisoformat(clock[1]), timezone.utc)
        if at < control:
            at += timedelta(days=1)
        if control <= at <= end:
            return True
    return False


def mission_stalled(freezes: list[str], missions: list[dict[str, Any]], ended: str,
                    heartbeat_age: float | None = None) -> bool:
    return (heartbeat_age is not None and heartbeat_age >= HEARTBEAT_LIMIT
            or any(freeze_in_mission(line, missions, ended) for line in freezes))


def live_freeze(active: dict[str, Any], elapsed: float) -> tuple[bool, list[str], float]:
    log = Path(active['hellforge_run']) / 'modloader.log'
    freezes = [line for line in log.read_text(encoding='utf-8', errors='replace').splitlines() if 'freeze:' in line]
    heartbeat = Path(ROOT) / 'userdata/environment/Local/CowboyBingus/Helldivers2/TestAgent/heartbeat.json'
    try:
        age = time.time() - heartbeat.stat().st_mtime
    except FileNotFoundError:
        age = elapsed
    ended = datetime.now(timezone.utc).isoformat()
    return mission_stalled(freezes, active.get('missions') or [], ended, age), freezes, age


def scan_run(run: Path | None) -> dict[str, Any]:
    """Crash lines and SEAF activity from the copied HellForge session folder of a harness run."""
    facts = {'crashes': [], 'freezes': [], 'in_mission_freeze': False,
             'seaf_spawns': 0, 'auto_waves': 0, 'squad_lost': 0, 'run_json': None}
    if run is None:
        return facts
    for log in run.rglob('modloader.log'):
        for line in log.read_text(encoding='utf-8', errors='replace').splitlines():
            if 'freeze:' in line:
                facts['freezes'].append(line)
            m = CRASH.search(line)
            if m and 'by module' not in line:
                facts['crashes'].append({'code': m.group('code'), 'site': m.group('site'), 'line': line[:300]})
    for log in run.rglob('mod-local.heavy-seaf-presence.log'):
        text = log.read_text(encoding='utf-8', errors='replace')
        facts['seaf_spawns'] += text.count('SPAWN:')
        dispatches = text.count('AUTO_WAVE_DISPATCH')
        facts['auto_waves'] += dispatches or len(re.findall(r'shuttle \d+ called in', text))
        facts['squad_lost'] += text.count('no longer alive')
    run_json = run / 'run.json'
    if run_json.exists():
        data = read_object(run_json)
        facts['run_json'] = {k: data.get(k) for k in ('outcome', 'clean', 'exit_code', 'ended', 'dumps', 'missions')
                             if k in data}
        ended = (data.get('exit') or {}).get('at') or data.get('finished')
        if ended:
            facts['in_mission_freeze'] = mission_stalled(facts['freezes'], data.get('missions') or [], ended)
    return facts


def wait_for_backend_port(port: int = 40558, limit: int = 300) -> bool:
    """Waits until the previous session's backend has released HellForge's port (a quick restart was refused)."""
    deadline = time.monotonic() + limit
    while time.monotonic() < deadline:
        out = subprocess.run(['netstat', '-ano', '-p', 'TCP'], capture_output=True, text=True, check=True).stdout
        # Any state counts: HellForge refuses the port while the old backend's sockets are still closing.
        if not re.search(r':%d\s' % port, out):
            return True
        time.sleep(5)
    return False


def classify_cycle(row: dict[str, Any]) -> None:
    kinds = [d.get('kind') for d in ((row.get('run_json') or {}).get('dumps') or [])]
    crashes = row.get('crashes') or []
    row['quit_crash'] = 'quit-render-task-pool' in kinds or any(c['site'].endswith('+0x626296') for c in crashes)
    mission_crashes = [c for c in crashes if not c['site'].endswith('+0x626296')]
    if row.get('in_mission_freeze'):
        if 'held_s' in row:
            row['attempted_hold_s'] = row.pop('held_s')
        row['error'] = 'game stalled during mission; see freezes, heartbeat_age_s and run.json'
    row['passed'] = (not row.get('in_mission_freeze') and not mission_crashes and 'error' not in row
                     and 'died_in_mission_after_s' not in row)


def sample_memory(row: dict[str, Any], step: str) -> None:
    script = os.environ.get('CLONEDIVERS_LAB_MEMORY_SCRIPT')
    if script:
        sample = subprocess.run(['powershell.exe', '-NoProfile', '-File', script,
                                 '-Label', '%s-%s-%s' % (row['label'], row['cycle'], step)],
                                capture_output=True, text=True, timeout=30)
        row.setdefault('memory_samples', {})[step] = sample.returncode


def cycle(args: argparse.Namespace, number: int) -> dict[str, Any]:
    row = {'label': args.label, 'cycle': number, 'difficulty': args.difficulty, 'faction': args.faction,
           'started': time.strftime('%Y-%m-%dT%H:%M:%S'), 'steps': {}, 'passed': False}
    prestart()
    if not wait_for_backend_port():
        row['error'] = 'backend port did not become free'
        return row
    prestart()
    code, data, text = harness('start', '--preset', args.preset, '--keep-running', timeout=300)
    if code != 0 and 'Port 40558' in text:
        if not wait_for_backend_port():
            row['error'] = 'backend port did not become free before retry'
            row['steps']['start'] = code
            return row
        prestart()
        code, data, text = harness('start', '--preset', args.preset, '--keep-running', timeout=300)
        row['start_retried'] = True
    row['steps']['start'] = code
    if code != 0:
        row['error'] = text[-1500:]
        return row

    active = active_session()
    if not active or not isinstance(data, dict) or active.get('session') != data.get('session'):
        raise RuntimeError('Start response does not match the active harness manifest')
    session = active['session']
    require_owned_session(session)
    run = Path(active['run'])
    try:
        for step, extra, limit in (('ship', [], 300),
                                   ('mission', ['--difficulty', str(args.difficulty), '--faction', args.faction]
                                    + (['--shots'] if args.shots else []), 600)):
            code, data, text = harness(step, *extra, timeout=limit, session=session)
            row['steps'][step] = code
            if step == 'mission' and data:
                row['mission'] = data
            if code != 0:
                row['error'] = '%s: %s' % (step, text[-1500:])
                break
            if step == 'ship' and args.shots:
                row['steps']['ship_shot'] = harness('shot', 'ship-arrival', timeout=30, session=session)[0]
            sample_memory(row, step)
        else:
            landed = time.monotonic()
            while True:
                active = require_owned_session(session)
                elapsed = time.monotonic() - landed
                if not game_running():
                    row['died_in_mission_after_s'] = round(elapsed)
                    break
                frozen, freezes, age = live_freeze(active, elapsed)
                if frozen:
                    row.update(in_mission_freeze=True, freezes=freezes, heartbeat_age_s=round(age, 1),
                               attempted_hold_s=args.hold, stalled_after_s=round(elapsed))
                    break
                if elapsed >= args.hold:
                    row['held_s'] = args.hold
                    break
                time.sleep(min(10, args.hold - elapsed))
        if 'held_s' in row and args.shots:
            sample_memory(row, 'held')
            row['steps']['held_shot'] = harness('shot', 'held-ground', timeout=30, session=session)[0]
            code, data, text = harness('leave', timeout=300, session=session)
            row['steps']['leave'] = code
            if code == 0:
                row['steps']['return_shot'] = harness('shot', 'ship-return', timeout=30, session=session)[0]
            else:
                row['error'] = 'leave: ' + text[-1500:]
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        row['error'] = str(error)
    finally:
        require_owned_session(session)
        if game_running():
            force = row.get('in_mission_freeze') or 'error' in row or 'held_s' not in row
            code, data, text = harness('stop', *(['--kill'] if force else []), timeout=300, session=session)
            row['steps']['stop'] = code
            if code != 0 and game_running():
                code, data, text = harness('stop', '--kill', timeout=180, session=session)
                row['steps']['forced_stop'] = code
        else:
            code, data, text = harness('finish', timeout=300, session=session)
            row['steps']['finish'] = code
        if code != 0:
            row['error'] = 'cleanup: ' + text[-1500:]
    row['run'] = str(run)
    facts = scan_run(run)
    facts['in_mission_freeze'] |= bool(row.get('in_mission_freeze'))
    facts['freezes'] = list(dict.fromkeys(row.get('freezes', []) + facts['freezes']))
    row.update(facts)
    classify_cycle(row)
    return row


def main() -> None:
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
    require_lab_profile()
    if args.cycles < 1 or args.hold < 0 or not 1 <= args.difficulty <= 11:
        parser.error('cycles must be positive, hold nonnegative, and difficulty within 1-11')
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    for number in range(1, args.cycles + 1):
        row = cycle(args, number)
        with open(out / 'cycles.jsonl', 'a', encoding='utf-8') as sink:
            sink.write(json.dumps(row) + '\n')
        print(json.dumps({k: row.get(k) for k in ('label', 'cycle', 'passed', 'quit_crash', 'in_mission_freeze', 'steps', 'held_s',
                                                    'died_in_mission_after_s', 'crashes', 'seaf_spawns', 'auto_waves',
                                                    'squad_lost', 'error')}),
              flush=True)


if __name__ == '__main__':
    main()
