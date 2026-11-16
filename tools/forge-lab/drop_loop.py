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
import ctypes
from ctypes import wintypes
from datetime import datetime, time as datetime_time, timedelta, timezone
import json
import os
import re
import subprocess
import sys
import threading
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
SHIP_SETTLE_S = 10
RETURN_TIMEOUT_S = 180
# Executed in the child before importing any harness/capture code. run_path does not
# add the script directory to sys.path (unlike `python harness.py`), so do that too.
HARNESS_WRAPPER = '''import ctypes, runpy, sys
ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
script = sys.argv.pop(1)
sys.argv[0] = script
sys.path.insert(0, str(__import__('pathlib').Path(script).parent))
runpy.run_path(script, run_name='__main__')
'''


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
    command = [sys.executable, '-B', '-c', HARNESS_WRAPPER, str(HARNESS / 'harness.py'), '--target', 'hellforge', '--hellforge-root', ROOT,
               '--profile', PROFILE, *args]
    env = {key: value for key, value in os.environ.items() if key.upper() != 'HD2H_CONTROLS_DLL'}
    try:
        done = subprocess.run(command, cwd=str(HARNESS), capture_output=True, text=True, timeout=timeout,
                              encoding='utf-8', errors='replace', env=env)
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


def mission_end_after(folder: Path, mission: dict[str, Any], ended: str,
                      ignore_incomplete: bool = False) -> float | None:
    """Match UTC mission-body clocks to this mission, including midnight rollover."""
    if not mission.get('in_control'):
        return None
    control = datetime.fromisoformat(mission['in_control']).astimezone(timezone.utc)
    end = datetime.fromisoformat(mission.get('left') or ended).astimezone(timezone.utc)
    times = []
    for body in folder.glob('*-Operation_Mission_end.json'):
        clock = re.match(r'(\d{2})(\d{2})(\d{2})(\d{3})-', body.name)
        if not clock:
            continue
        # Reading also verifies the body was completely written. Any mission end
        # (success or failure) ends player exposure; abandon is a different body.
        try:
            data = read_object(body)
        except (OSError, ValueError):
            if ignore_incomplete:
                continue
            raise
        if not isinstance(data.get('missionSuccess'), bool):
            continue
        h, m, s, ms = map(int, clock.groups())
        at = datetime.combine(control.date(), datetime_time(h, m, s, ms * 1000), timezone.utc)
        if at < control:
            at += timedelta(days=1)
        if control <= at <= end:
            times.append((at - control).total_seconds())
    return min(times) if times else None


def live_mission_end(active: dict[str, Any], mission: dict[str, Any]) -> float | None:
    if not active.get('hellforge_run'):
        return None
    return mission_end_after(Path(active['hellforge_run']) / 'backend/mission-bodies', mission,
                             datetime.now(timezone.utc).isoformat(), ignore_incomplete=True)


def fault_signatures(crashes: list[dict[str, Any]]) -> set[str]:
    faults = [c for c in crashes if c.get('kind', 'fault') == 'fault' and not c.get('handled_quit')]
    sites = {(c.get('site') or 'native-fault:unknown').lower() for c in faults}
    if 'helldivers2.exe+0x1e6b3e' in sites:
        sites.discard('helldivers2.exe+0x99d7cd')
    return sites


def scan_run(run: Path | None) -> dict[str, Any]:
    """Crash lines and SEAF activity from the copied HellForge session folder of a harness run."""
    facts = {'crashes': [], 'freezes': [], 'in_mission_freeze': False,
             'seaf_spawns': 0, 'auto_waves': 0, 'squad_lost': 0, 'run_json': None}
    if run is None:
        return facts
    run_json = run / 'run.json'
    data = read_object(run_json) if run_json.exists() else {}
    # Engine log copies can contain older sessions. Require the handler's PID and
    # timestamp to match this run; loading/arming clean-exit is not mitigation.
    handled = []
    for log in run.rglob('*clean-exit*.log'):
        for line in log.read_text(encoding='utf-8', errors='replace').splitlines():
            match = re.match(r'(\S+) pid=(\d+) .*teardown fault at \+0x626296', line, re.I)
            if match and int(match[2]) == data.get('pid') and data.get('started') and data.get('finished'):
                at = datetime.fromisoformat(match[1])
                if datetime.fromisoformat(data['started']) <= at <= datetime.fromisoformat(data['finished']):
                    handled.append(line)
    facts['handled_quit_evidence'] = handled
    for log in run.rglob('modloader.log'):
        for line in log.read_text(encoding='utf-8', errors='replace').splitlines():
            if 'freeze:' in line:
                facts['freezes'].append(line)
            if 'crash:' in line:
                m = CRASH.search(line)
                kind = 'fail-fast' if 'crash: fail-fast' in line else 'by-module' if 'crash: by module:' in line else 'fault'
                site = re.search(r'\bat ([\w.-]+\.(?:exe|dll))\+(0x[0-9a-f]+)', line, re.I) if kind == 'fault' else None
                module, offset = site.groups() if site else (None, None)
                is_handled = bool(module and module.lower() == 'helldivers2.exe'
                                  and offset.lower() == '0x626296' and handled)
                facts['crashes'].append({'kind': kind, 'code': m.group('code') if m else None,
                                         'site': module + '+' + offset if site else None,
                                         'module': module, 'offset': offset,
                                         'handled_quit': is_handled, 'line': line})
    for log in run.rglob('mod-local.heavy-seaf-presence.log'):
        text = log.read_text(encoding='utf-8', errors='replace')
        facts['seaf_spawns'] += text.count('SPAWN:')
        dispatches = text.count('AUTO_WAVE_DISPATCH')
        facts['auto_waves'] += dispatches or len(re.findall(r'shuttle \d+ called in', text))
        facts['squad_lost'] += text.count('no longer alive')
    if data:
        facts['run_json'] = {k: data.get(k) for k in ('outcome', 'clean', 'exit', 'pid', 'started', 'finished', 'dumps', 'missions')
                             if k in data}
        facts['run_json']['exit_code'] = (data.get('exit') or {}).get('code')
        ended = (data.get('exit') or {}).get('at') or data.get('finished')
        if ended:
            facts['in_mission_freeze'] = mission_stalled(facts['freezes'], data.get('missions') or [], ended)
            missions = data.get('missions') or []
            if missions:
                after = mission_end_after(run / 'hellforge-session/backend/mission-bodies', missions[-1], ended)
                if after is not None:
                    facts['mission_end_after_s'] = after
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
    run = row.get('run_json') or {}
    missions = run.get('missions') or []
    mission = row.get('mission') or (missions[-1] if missions else {})
    select = (mission.get('steps') or {}).get('select') or {}
    row['planet'] = mission.get('planet', select.get('planet', row.get('planet')))
    row['mission_type'] = select.get('mission_type', row.get('mission_type'))
    row['difficulty'] = mission.get('difficulty', row.get('difficulty'))
    row['faction'] = {2: 'terminid', 3: 'automaton', 4: 'illuminate'}.get(mission.get('race'), row.get('faction'))
    row.setdefault('seaf_spawns', 0)
    leave = row.get('leave') or (missions[-1].get('leave') if missions else {}) or {}
    hold = row.get('hold_s', row.get('held_s', row.get('attempted_hold_s')))
    end_after = row.get('mission_end_after_s')
    # Harness in_control is truncated to whole seconds; a body in the final
    # clock second is conservatively reduced (the recorded 120 s case is 120.571).
    row['mission_ended_early'] = bool(row.get('mission_ended_early') or leave.get('to_ship_s') == 0
                                    or end_after is not None and (hold is None or end_after < hold + 1))
    kinds = [d.get('kind') for d in (run.get('dumps') or [])]
    crashes = row.get('crashes') or []
    row['quit_crash'] = 'quit-render-task-pool' in kinds or any((c.get('site') or '').lower().endswith('+0x626296') for c in crashes)
    signatures = fault_signatures(crashes)
    if row.get('in_mission_freeze'):
        sites = [re.search(r'\bat ([\w.-]+\.(?:exe|dll)\+0x[0-9a-f]+)', line, re.I)
                 for line in row.get('freezes', [])]
        site = next((m[1].lower() for m in sites if m and m[1].lower() == 'helldivers2.exe+0x700afb'), 'in-mission')
        signatures.add('freeze:' + site)
    row['event_signatures'] = sorted(signatures)
    row['event'] = bool(signatures)
    if row.get('in_mission_freeze'):
        if 'held_s' in row:
            row['attempted_hold_s'] = row.pop('held_s')
    exit_code = (run.get('exit') or {}).get('code', run.get('exit_code'))
    row['driver_error'] = bool(row.get('error') or exit_code is None and not row['event']
                               or not row['event'] and (exit_code != 0 or 'died_in_mission_after_s' in row
                                                        or row.get('died_after_return'))
                               or any(code != 0 and not (row['event'] and step in ('stop', 'finish') and code == 1)
                                      for step, code in row.get('steps', {}).items()))
    row['failed'] = row['event'] or row['driver_error']
    duration_keys = (('mission_end_after_s',) if end_after is not None else ('mission_s', 'mission_ended_after_s'))
    durations = [row[k] for k in (*duration_keys, 'stalled_after_s', 'died_in_mission_after_s', 'held_s')
                 if isinstance(row.get(k), (int, float))]
    if hold is not None and durations:
        durations.append(hold)
    row['mission_s'] = round(max(0, min(durations)), 3) if durations else 0
    normal_quit = row.get('normal_quit', row.get('steps', {}).get('stop') == 0 and exit_code == 0)
    row['return_observed'] = bool(row.get('ship_settle_ok') and normal_quit and leave)
    row['reduced_coverage'] = row['mission_ended_early']
    row['qualifying'] = bool('held_s' in row and not row['reduced_coverage']
                             and (hold is None or row['held_s'] >= hold)
                             and row['return_observed'])
    row['reduced_coverage'] = not row['qualifying']
    row['passed'] = not row['failed']


def game_peak_working_set(pid: int) -> int | None:
    """Windows' lifetime process peak, in bytes; access failures are unknown."""
    class Counters(ctypes.Structure):
        _fields_ = [('cb', wintypes.DWORD), ('PageFaultCount', wintypes.DWORD)] + [
            (name, ctypes.c_size_t) for name in ('PeakWorkingSetSize', 'WorkingSetSize', 'QuotaPeakPagedPoolUsage',
                                               'QuotaPagedPoolUsage', 'QuotaPeakNonPagedPoolUsage',
                                               'QuotaNonPagedPoolUsage', 'PagefileUsage', 'PeakPagefileUsage')]
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    psapi = ctypes.WinDLL('psapi', use_last_error=True)
    psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(Counters), wintypes.DWORD]
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        return None
    try:
        counters = Counters()
        counters.cb = ctypes.sizeof(counters)
        return counters.PeakWorkingSetSize if psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb) else None
    finally:
        kernel.CloseHandle(handle)


class MemorySampler:
    """Small per-cycle sampler. GPU memory is total device use, not process attribution."""
    def __init__(self, pid: int | None) -> None:
        self.pid = pid
        self.peak_working_set_bytes: int | None = None
        self.peak_vram_mib: int | None = None
        self.done = threading.Event()
        self.thread = threading.Thread(target=self.poll, daemon=True)

    def sample(self) -> None:
        if self.pid:
            try:
                peak = game_peak_working_set(self.pid)
            except OSError:
                peak = None
            if peak is not None:
                self.peak_working_set_bytes = max(self.peak_working_set_bytes or 0, peak)
        try:
            result = subprocess.run(['nvidia-smi', '--query-gpu=memory.used', '--format=csv,noheader,nounits'],
                                    capture_output=True, text=True, timeout=3, check=True)
            values = [int(line.strip()) for line in result.stdout.splitlines() if line.strip()]
            if values:
                self.peak_vram_mib = max(self.peak_vram_mib or 0, max(values))
        except (OSError, ValueError, subprocess.SubprocessError):
            pass

    def poll(self) -> None:
        while not self.done.is_set():
            self.sample()
            self.done.wait(3)

    def start(self) -> None:
        self.thread.start()

    def stop(self, row: dict[str, Any]) -> None:
        self.done.set()
        self.thread.join()
        row.update(peak_working_set_bytes=self.peak_working_set_bytes, peak_vram_mib=self.peak_vram_mib)


def check_state(row: dict[str, Any], session: str, step: str) -> dict[str, Any]:
    code, data, text = harness('state', timeout=30, session=session)
    row['steps'][step] = code
    live = (data or {}).get('live') or {}
    if code != 0 or live.get('state') is None:
        raise RuntimeError('%s: %s' % (step, text[-1500:] or 'no live game state'))
    return live


def poll_state(row: dict[str, Any], session: str, step: str) -> dict[str, Any] | None:
    """Read live game state, returning None if the game exited before or during the poll."""
    if not game_running():
        return None
    try:
        return check_state(row, session, step)
    except RuntimeError:
        if not game_running():
            return None
        raise


def sample_memory(row: dict[str, Any], step: str) -> None:
    script = os.environ.get('CLONEDIVERS_LAB_MEMORY_SCRIPT')
    if script:
        sample = subprocess.run(['powershell.exe', '-NoProfile', '-File', script,
                                 '-Label', '%s-%s-%s' % (row['label'], row['cycle'], step)],
                                capture_output=True, text=True, timeout=30)
        row.setdefault('memory_samples', {})[step] = sample.returncode


def cycle(args: argparse.Namespace, number: int) -> dict[str, Any]:
    row = {'label': args.label, 'cycle': number, 'difficulty': args.difficulty, 'faction': args.faction,
           'planet': None, 'mission_type': None, 'seaf_spawns': 0, 'qualifying': False,
           'mission_ended_early': False, 'reduced_coverage': False, 'ship_settle_ok': False,
           'peak_working_set_bytes': None, 'peak_vram_mib': None, 'failed': True,
           'hold_s': args.hold, 'mission_s': 0, 'event': False, 'event_signatures': [],
           'return_observed': False, 'driver_error': False,
           'started': time.strftime('%Y-%m-%dT%H:%M:%S'), 'steps': {}, 'passed': False}
    prestart()
    if not wait_for_backend_port():
        row['error'] = 'backend port did not become free'
        row['driver_error'] = True
        return row
    prestart()
    code, data, text = harness('start', '--preset', args.preset, '--keep-running', timeout=300)
    if code != 0 and 'Port 40558' in text:
        if not wait_for_backend_port():
            row['error'] = 'backend port did not become free before retry'
            row['steps']['start'] = code
            row['driver_error'] = True
            return row
        prestart()
        code, data, text = harness('start', '--preset', args.preset, '--keep-running', timeout=300)
        row['start_retried'] = True
    row['steps']['start'] = code
    if code != 0:
        row['error'] = text[-1500:]
        row['driver_error'] = True
        return row

    active = active_session()
    if not active or not isinstance(data, dict) or active.get('session') != data.get('session'):
        raise RuntimeError('Start response does not match the active harness manifest')
    session = active['session']
    require_owned_session(session)
    run = Path(active['run'])
    memory = MemorySampler(active.get('pid'))
    try:
        memory.start()
        for step, extra, limit in (('ship', [], 300),
                                   ('mission', ['--difficulty', str(args.difficulty), '--faction', args.faction]
                                    + (['--shots'] if args.shots else []), 600)):
            code, data, text = harness(step, *extra, timeout=limit, session=session)
            row['steps'][step] = code
            if step == 'mission' and data:
                row['mission'] = data
            if code != 0:
                row.setdefault('error', '%s: %s' % (step, text[-1500:]))
                break
            if step == 'ship' and args.shots:
                code, _, text = harness('shot', 'ship-arrival', timeout=30, session=session)
                row['steps']['ship_shot'] = code
                if code != 0:
                    row.setdefault('error', 'ship_shot: ' + text[-1500:])
                    break
            sample_memory(row, step)
        else:
            landed = time.monotonic()
            control = (row.get('mission') or {}).get('in_control')
            if control:
                landed -= max(0, (datetime.now(timezone.utc) - datetime.fromisoformat(control)).total_seconds())
            while True:
                active = require_owned_session(session)
                elapsed = time.monotonic() - landed
                row['mission_s'] = round(min(elapsed, args.hold), 3)
                if not game_running():
                    row['died_in_mission_after_s'] = round(elapsed)
                    break
                frozen, freezes, age = live_freeze(active, elapsed)
                if frozen:
                    row.update(in_mission_freeze=True, freezes=freezes, heartbeat_age_s=round(age, 1),
                               attempted_hold_s=args.hold, stalled_after_s=round(elapsed))
                    break
                live = poll_state(row, session, 'hold_state')
                if live is None:
                    row['died_in_mission_after_s'] = round(elapsed)
                    break
                end_after = live_mission_end(active, row.get('mission') or {})
                if end_after is not None:
                    row['mission_end_after_s'] = end_after
                if (end_after is not None and end_after < args.hold + 1
                        or live['state'] != 4 or live.get('in_mission') is False):
                    row['mission_ended_early'] = True
                    row['mission_ended_after_s'] = round(elapsed)
                    break
                if elapsed >= args.hold:
                    row['held_s'] = args.hold
                    break
                time.sleep(min(10, args.hold - elapsed))
        if 'held_s' in row or row['mission_ended_early']:
            if args.shots and 'held_s' in row:
                sample_memory(row, 'held')
                code, _, text = harness('shot', 'held-ground', timeout=30, session=session)
                row['steps']['held_shot'] = code
                if code != 0:
                    row.setdefault('error', 'held_shot: ' + text[-1500:])
            if row['mission_ended_early']:
                # Mission_end precedes the game's own abandon by about 29 s.
                # Never abandon from the load screen or while that return runs.
                deadline = time.monotonic() + RETURN_TIMEOUT_S
                while live['state'] != 3:
                    now = time.monotonic()
                    if now >= deadline:
                        raise RuntimeError('stuck returning after mission end (state %s)' % live['state'])
                    active = require_owned_session(session)
                    if not game_running():
                        row['died_in_mission_after_s'] = round(now - landed)
                        break
                    frozen, freezes, age = live_freeze(active, now - landed)
                    if frozen:
                        row.update(in_mission_freeze=True, freezes=freezes, heartbeat_age_s=round(age, 1),
                                   attempted_hold_s=args.hold, stalled_after_s=round(now - landed))
                        break
                    time.sleep(2)
                    polled = poll_state(row, session, 'auto_return_state')
                    if polled is None:
                        row['died_in_mission_after_s'] = round(time.monotonic() - landed)
                        break
                    live = polled
                else:
                    row['auto_return'] = True
            if not row['mission_ended_early'] or row.get('auto_return'):
                # On state 3 this is a no-op that records left/in_mission in the
                # harness manifest; on a full hold it performs the normal abandon.
                code, data, text = harness('leave', timeout=300, session=session)
                row['steps']['leave'] = code
                if code == 0:
                    row['leave'] = data
                    row['mission_ended_early'] |= (data or {}).get('to_ship_s') == 0
                    time.sleep(SHIP_SETTLE_S)
                    live = poll_state(row, session, 'ship_settle_state')
                    if live is None:
                        row['died_after_return'] = True
                    else:
                        row['ship_settle_ok'] = live['state'] == 3
                        if not row['ship_settle_ok']:
                            row.setdefault('error', 'ship settle: expected state 3, got %s' % live['state'])
                        if args.shots and row['ship_settle_ok']:
                            code, _, text = harness('shot', 'ship-return', timeout=30, session=session)
                            row['steps']['return_shot'] = code
                            if code != 0:
                                row.setdefault('error', 'return_shot: ' + text[-1500:])
                else:
                    row.setdefault('error', 'leave: ' + text[-1500:])
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        row.setdefault('error', str(error))
    finally:
        try:
            require_owned_session(session)
            if game_running():
                force = row.get('in_mission_freeze') or 'error' in row or not row['ship_settle_ok']
                row['normal_quit'] = not force
                code, data, text = harness('stop', *(['--kill'] if force else []), timeout=300, session=session)
                row['steps']['stop'] = code
                if code != 0 and not (code == 1 and (force and row.get('in_mission_freeze') or not game_running())):
                    row.setdefault('error', 'stop: ' + text[-1500:])
                    if game_running():
                        code, data, text = harness('stop', '--kill', timeout=180, session=session)
                        row['steps']['forced_stop'] = code
            else:
                code, data, text = harness('finish', timeout=300, session=session)
                row['steps']['finish'] = code
            if code != 0 and not (code == 1 and (row.get('in_mission_freeze') or not game_running())):
                row.setdefault('error', 'cleanup: ' + text[-1500:])
        except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
            row.setdefault('error', 'cleanup: ' + str(error))
        finally:
            memory.stop(row)
    row['run'] = str(run)
    facts = scan_run(run)
    facts['in_mission_freeze'] |= bool(row.get('in_mission_freeze'))
    facts['freezes'] = list(dict.fromkeys(row.get('freezes', []) + facts['freezes']))
    facts['mission_ended_early'] = bool(row['mission_ended_early'] or facts.get('mission_ended_early'))
    row.update(facts)
    classify_cycle(row)
    return row


def print_summary(rows: list[dict[str, Any]], planned: int, error: str | None = None) -> int:
    errors = sum(bool(r.get('driver_error')) for r in rows) + bool(error)
    code = (2 if errors else 1 if any(r.get('event') for r in rows)
            else 3 if any(not r.get('qualifying') for r in rows) else 0)
    print(json.dumps({'summary': True, 'cycles': len(rows), 'planned_cycles': planned,
                      'events': sum(bool(r.get('event')) for r in rows), 'driver_errors': errors,
                      'qualifying': sum(bool(r.get('qualifying')) for r in rows),
                      'reduced_coverage': sum(bool(r.get('reduced_coverage')) for r in rows),
                      'returns': sum(bool(r.get('return_observed')) for r in rows),
                      'mission_s': round(sum(r.get('mission_s', 0) for r in rows), 3),
                      'exit_code': code, 'error': error}), flush=True)
    return code


def main() -> int:
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
    if args.cycles < 1 or args.hold < 0 or not 1 <= args.difficulty <= 11:
        parser.error('cycles must be positive, hold nonnegative, and difficulty within 1-11')
    out = Path(args.out)
    try:
        require_lab_profile()
        out.mkdir(parents=True, exist_ok=True)
    except Exception as error:
        return print_summary([], args.cycles, str(error))
    rows = []
    for number in range(1, args.cycles + 1):
        try:
            row = cycle(args, number)
        except Exception as error:
            row = {'label': args.label, 'cycle': number, 'passed': False, 'failed': True,
                   'driver_error': True, 'event': False, 'event_signatures': [], 'mission_s': 0,
                   'qualifying': False, 'return_observed': False, 'reduced_coverage': True, 'error': str(error)}
        rows.append(row)
        try:
            with open(out / 'cycles.jsonl', 'a', encoding='utf-8') as sink:
                sink.write(json.dumps(row) + '\n')
        except Exception as error:
            return print_summary(rows, args.cycles, 'write cycles.jsonl: ' + str(error))
        print(json.dumps({k: row.get(k) for k in ('label', 'cycle', 'passed', 'failed', 'driver_error', 'event',
                                                    'event_signatures', 'mission_s', 'return_observed', 'qualifying', 'reduced_coverage',
                                                    'mission_ended_early', 'ship_settle_ok', 'planet', 'mission_type',
                                                    'difficulty', 'faction', 'peak_working_set_bytes', 'peak_vram_mib',
                                                    'quit_crash', 'in_mission_freeze', 'steps', 'held_s',
                                                    'died_in_mission_after_s', 'crashes', 'seaf_spawns', 'auto_waves',
                                                    'squad_lost', 'error')}),
              flush=True)
        if row.get('driver_error'):
            break
    return print_summary(rows, args.cycles)


if __name__ == '__main__':
    sys.exit(main())
