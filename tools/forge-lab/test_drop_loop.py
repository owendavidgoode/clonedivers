"""No-game regression checks. Run with python -m unittest discover -s tools/forge-lab."""
import argparse
import copy
import io
import json
from pathlib import Path
import unittest
from unittest.mock import MagicMock, Mock, patch

import drop_loop

FIXTURES = Path(__file__).with_name('fixtures')


def fixture(name: str) -> dict:
    return json.loads((FIXTURES / name).read_text(encoding='utf-8-sig'))


def qualifying_row() -> dict:
    row = fixture('good-cycle.json')
    row.update(ship_settle_ok=True, run_json=fixture('good-run.json'))
    return row


class ClassificationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.missions = [{'in_control': '2026-11-09T12:12:42-06:00',
                          'left': '2026-11-09T12:16:45-06:00'}]
        self.ended = '2026-11-09T12:17:06-06:00'

    def test_live_process_with_freeze_fails_hold(self) -> None:
        line = "18:13:56.478 freeze: the game's clock has not moved for 3000 ms"
        self.assertTrue(drop_loop.mission_stalled([line], self.missions, self.ended))
        row = {'held_s': 240, 'in_mission_freeze': True, 'crashes': []}
        drop_loop.classify_cycle(row)
        self.assertFalse(row['passed'])
        self.assertNotIn('held_s', row)
        self.assertEqual(row['attempted_hold_s'], 240)

    def test_stale_heartbeat_fails_without_freeze_log(self) -> None:
        self.assertTrue(drop_loop.mission_stalled([], self.missions, self.ended, 60))
        self.assertFalse(drop_loop.mission_stalled([], self.missions, self.ended, 59))
        self.assertFalse(drop_loop.mission_stalled([], self.missions, self.ended))

    def test_ship_and_teardown_freezes_do_not_fail_mission(self) -> None:
        for clock in ('18:12:40', '18:16:46', '18:17:07'):
            with self.subTest(clock=clock):
                self.assertFalse(drop_loop.freeze_in_mission(clock + ' freeze: stalled', self.missions, self.ended))

    def test_utc_midnight_and_open_mission(self) -> None:
        missions = [{'in_control': '2026-11-09T17:59:50-06:00'}]
        self.assertTrue(drop_loop.freeze_in_mission('00:00:05.2 freeze: stalled', missions,
                                                   '2026-11-09T18:00:30-06:00'))
        self.assertFalse(drop_loop.freeze_in_mission('23:59:49 freeze: stalled', missions,
                                                    '2026-11-09T18:00:30-06:00'))

    def test_quit_crash_remains_separate(self) -> None:
        row = qualifying_row()
        row['crashes'] = [{'site': 'helldivers2.exe+0x626296', 'handled_quit': True}]
        drop_loop.classify_cycle(row)
        self.assertTrue(row['passed'])
        self.assertTrue(row['quit_crash'])
        row['crashes'].append({'site': 'helldivers2.exe+0xa2d495'})
        drop_loop.classify_cycle(row)
        self.assertFalse(row['passed'])

    def test_process_death_or_step_error_fails(self) -> None:
        for failure in ({'died_in_mission_after_s': 70}, {'error': 'mission failed'}):
            drop_loop.classify_cycle(failure)
            self.assertFalse(failure['passed'])

    def test_copied_log_classifies_real_freeze_and_counts_waves_once(self) -> None:
        run = MagicMock()
        log = Mock()
        log.read_text.return_value = '18:13:56.478 freeze: stalled\n'
        seaf = Mock()
        seaf.read_text.return_value = 'AUTO_WAVE_DISPATCH\nshuttle 1 called in\nSPAWN:\n'
        run.rglob.side_effect = [[], [log], [seaf]]
        (run / 'run.json').read_text.return_value = json.dumps({'missions': self.missions, 'exit': {'at': self.ended}})
        facts = drop_loop.scan_run(run)
        self.assertTrue(facts['in_mission_freeze'])
        self.assertEqual(facts['auto_waves'], 1)
        self.assertEqual(facts['seaf_spawns'], 1)

    def test_recorded_zero_return_is_reduced_coverage(self) -> None:
        row = fixture('early-cycle.json')
        row['ship_settle_ok'] = True
        drop_loop.classify_cycle(row)
        self.assertTrue(row['mission_ended_early'])
        self.assertTrue(row['reduced_coverage'])
        self.assertFalse(row['qualifying'])
        self.assertFalse(row['passed'])
        self.assertFalse(row['failed'])

    def test_recorded_selection_metadata_is_promoted(self) -> None:
        row = qualifying_row()
        drop_loop.classify_cycle(row)
        self.assertEqual((row['planet'], row['mission_type'], row['difficulty'], row['faction'], row['seaf_spawns']),
                         (157, 21, 11, 'automaton', 57))
        self.assertTrue(row['passed'])
        # The control step's mission_type=1 is a runtime mode, not the selected type=21.
        self.assertEqual(row['mission']['steps']['control']['mission_type'], 1)

    def test_missing_real_return_or_settle_cannot_pass(self) -> None:
        for field in ('held_s', 'ship_settle_ok'):
            row = qualifying_row()
            row.pop(field)
            drop_loop.classify_cycle(row)
            self.assertFalse(row['qualifying'])
            self.assertFalse(row['passed'])
        row = qualifying_row()
        row['run_json']['missions'][0].pop('leave')
        drop_loop.classify_cycle(row)
        self.assertFalse(row['qualifying'])

    def test_nonzero_recorded_exit_fails_despite_successful_steps(self) -> None:
        row = qualifying_row()
        row['run_json']['exit'] = fixture('nonzero-run.json')['exit']
        self.assertNotEqual(row['run_json']['exit']['code'], 0)
        drop_loop.classify_cycle(row)
        self.assertTrue(row['failed'])
        self.assertFalse(row['passed'])

    def test_unknown_exit_does_not_pass(self) -> None:
        row = qualifying_row()
        row['run_json']['exit'] = fixture('return-fault-run.json')['exit']
        drop_loop.classify_cycle(row)
        self.assertFalse(row['passed'])

    def test_freeze_preserves_original_step_error(self) -> None:
        row = fixture('failed-cycle.json')
        first = row['error']
        row['in_mission_freeze'] = True
        drop_loop.classify_cycle(row)
        self.assertEqual(row['error'], first)


class RecordedLogTests(unittest.TestCase):
    def scan(self, log_name: str, handled: bool = False, run_json: str = 'good-run.json') -> dict:
        run = MagicMock()
        run.rglob.side_effect = [[FIXTURES / 'handled-clean-exit.log'] if handled else [],
                                [FIXTURES / log_name], []]
        (run / 'run.json').read_text.return_value = json.dumps(fixture(run_json))
        return drop_loop.scan_run(run)

    def test_scan_reads_nested_exit_code(self) -> None:
        facts = self.scan('return-fault.log')
        self.assertEqual(facts['run_json']['exit']['code'], 0)
        self.assertEqual(facts['run_json']['exit_code'], 0)
        facts = self.scan('quit-fault.log', run_json='nonzero-run.json')
        self.assertEqual(facts['run_json']['exit_code'], 3075604483)

    def test_every_native_fault_line_and_module_offset_is_kept(self) -> None:
        facts = self.scan('return-fault.log')
        lines = (FIXTURES / 'return-fault.log').read_text().splitlines()
        self.assertEqual([c['line'] for c in facts['crashes']], lines)
        self.assertEqual([c['offset'] for c in facts['crashes']],
                         ['0x1e6b3e', '0x1e6b3e', '0x129d2', '0x129d2', '0x99d7cd', '0x99d7cd'])
        self.assertEqual([c['module'] for c in facts['crashes']],
                         ['helldivers2.exe', 'helldivers2.exe', 'dbgcore.DLL', 'dbgcore.DLL', 'helldivers2.exe', 'helldivers2.exe'])
        row = qualifying_row()
        row.update(facts)
        drop_loop.classify_cycle(row)
        self.assertTrue(row['failed'])

    def test_only_evidenced_quit_fault_is_exempt(self) -> None:
        facts = self.scan('quit-fault.log', handled=True, run_json='handled-run.json')
        # Later fail-fast lines are separate faults and remain failures.
        self.assertTrue(all(c['handled_quit'] for c in facts['crashes'][:2]))
        self.assertTrue(all(not c['handled_quit'] for c in facts['crashes'][2:]))
        self.assertEqual(facts['crashes'][2]['module'], 'dbgcore.DLL')
        row = qualifying_row()
        row['crashes'] = facts['crashes'][:2]
        drop_loop.classify_cycle(row)
        self.assertTrue(row['passed'])
        self.assertTrue(row['quit_crash'])
        row['crashes'] = self.scan('quit-fault.log')['crashes'][:2]
        drop_loop.classify_cycle(row)
        self.assertTrue(row['failed'])

    def test_old_handler_lines_cannot_exempt_a_different_session(self) -> None:
        facts = self.scan('quit-fault.log', handled=True)
        self.assertFalse(facts['handled_quit_evidence'])
        self.assertTrue(all(not c['handled_quit'] for c in facts['crashes']))
        run = fixture('handled-run.json')
        run['started'] = '2026-11-10T00:00:00Z'
        run['finished'] = '2026-11-10T00:05:00Z'
        with patch('test_drop_loop.fixture', return_value=run):
            self.assertFalse(self.scan('quit-fault.log', handled=True)['handled_quit_evidence'])


class QueueTests(unittest.TestCase):
    def test_windows_paths_round_trip_through_json(self) -> None:
        request = {'name': 'check', 'kind': 'appearance-pack', 'checks': 'drop',
                   'zip_or_folder': r'D:\ClaudeWork\empire-forge\test'}
        self.assertEqual(drop_loop.parse_queue_request('\ufeff' + json.dumps(request)), request)
        path = Mock()
        path.read_text.return_value = '\ufeff' + json.dumps(request)
        self.assertEqual(drop_loop.load_queue_request(path), request)

    def test_invalid_escapes_and_wrong_shapes_are_rejected(self) -> None:
        for text in (r'{"zip_or_folder":"D:\ClaudeWork\test"}', '[]', '{}'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                drop_loop.parse_queue_request(text)

    def test_valid_escape_that_corrupts_path_is_rejected(self) -> None:
        text = r'{"name":"check","kind":"pack","checks":"drop","zip_or_folder":"D:\test"}'
        with self.assertRaisesRegex(ValueError, 'control character'):
            drop_loop.parse_queue_request(text)


class CycleTests(unittest.TestCase):
    def setUp(self) -> None:
        for name in ('MemorySampler',):
            mock = patch.object(drop_loop, name)
            mock.start()
            self.addCleanup(mock.stop)
        sleep = patch.object(drop_loop.time, 'sleep')
        self.sleep = sleep.start()
        self.addCleanup(sleep.stop)
        self.args = argparse.Namespace(label='test', difficulty=11, faction='automaton', preset='light',
                                       shots=True, hold=240)
        self.active = {'session': 'owned', 'run': 'owned-run', 'profile': 'clonedivers-armory', 'target': 'hellforge'}
        self.calls: list[tuple[str, ...]] = []

    def command(self, *args: str, **kwargs: object) -> tuple[int, dict, str]:
        self.calls.append(args)
        data = {'start': self.active, 'mission': fixture('good-cycle.json')['mission'],
                'leave': {'to_ship_s': 14.03},
                'state': {'live': {'state': 3 if ('leave',) in self.calls else 4, 'in_mission': ('leave',) not in self.calls}}}
        return 0, data.get(args[0], {}), ''

    def run_cycle(self, command: object = None) -> dict:
        with (patch.object(drop_loop, 'prestart'), patch.object(drop_loop, 'wait_for_backend_port', return_value=True),
              patch.object(drop_loop, 'sample_memory'),
              patch.object(drop_loop, 'harness', side_effect=command or self.command),
              patch.object(drop_loop, 'active_session', return_value=self.active),
              patch.object(drop_loop, 'require_owned_session', return_value=self.active),
              patch.object(drop_loop, 'game_running', return_value=True),
              patch.object(drop_loop, 'live_freeze', return_value=(False, [], 1)),
              patch.object(drop_loop, 'scan_run', return_value={'in_mission_freeze': False, 'freezes': [],
                                                               'run_json': fixture('good-run.json')})):
            return drop_loop.cycle(self.args, 1)

    def test_first_step_error_survives_cleanup_error(self) -> None:
        first = fixture('failed-cycle.json')['error']
        def command(*args: str, **kwargs: object) -> tuple[int, dict | None, str]:
            if args[0] in ('mission', 'stop'):
                return 1, None, first if args[0] == 'mission' else 'cleanup failed'
            return self.command(*args, **kwargs)
        row = self.run_cycle(command)
        self.assertEqual(row['error'], 'mission: ' + first)
        self.assertFalse(row['passed'])

    def test_stop_error_survives_successful_forced_stop(self) -> None:
        self.args.hold = 0
        def command(*args: str, **kwargs: object) -> tuple[int, dict | None, str]:
            if args == ('stop',):
                return 1, None, 'quit failed'
            return self.command(*args, **kwargs)
        row = self.run_cycle(command)
        self.assertEqual(row['error'], 'stop: quit failed')
        self.assertEqual(row['steps']['forced_stop'], 0)
        self.assertTrue(row['failed'])

    def test_no_shots_still_leaves_and_settles_on_ship(self) -> None:
        self.args.hold = 0
        self.args.shots = False
        row = self.run_cycle()
        self.assertTrue(row['passed'])
        self.assertEqual([c[0] for c in self.calls], ['start', 'ship', 'mission', 'state', 'leave', 'state', 'stop'])
        self.sleep.assert_called_once_with(10)

    def test_mission_ending_during_hold_is_not_a_pass_or_failure(self) -> None:
        def command(*args: str, **kwargs: object) -> tuple[int, dict, str]:
            code, data, text = self.command(*args, **kwargs)
            if args[0] == 'state':
                data = {'live': {'state': 3, 'in_mission': False}}
            if args[0] == 'leave':
                data = fixture('early-cycle.json')['run_json']['missions'][-1]['leave']
            return code, data, text
        row = self.run_cycle(command)
        self.assertTrue(row['mission_ended_early'])
        self.assertTrue(row['reduced_coverage'])
        self.assertFalse(row['qualifying'])
        self.assertFalse(row['passed'])
        self.assertFalse(row['failed'])
        self.assertNotIn('held_s', row)
        self.assertNotIn(('shot', 'held-ground'), self.calls)

    def test_post_return_state_drift_fails_before_quit(self) -> None:
        self.args.hold = 0
        def command(*args: str, **kwargs: object) -> tuple[int, dict, str]:
            code, data, text = self.command(*args, **kwargs)
            if args[0] == 'state' and ('leave',) in self.calls:
                data = {'live': {'state': 4}}
            return code, data, text
        row = self.run_cycle(command)
        self.assertFalse(row['ship_settle_ok'])
        self.assertFalse(row['passed'])
        self.assertIn(('stop', '--kill'), self.calls)

    def test_missing_live_state_is_a_failure(self) -> None:
        row = self.run_cycle(lambda *args, **kwargs: (0, {}, '') if args[0] == 'state' else self.command(*args, **kwargs))
        self.assertIn('no live game state', row['error'])
        self.assertTrue(row['failed'])

    def test_freeze_forces_stop_and_skips_screenshot_leave_wait(self) -> None:
        with (patch.object(drop_loop, 'prestart'), patch.object(drop_loop, 'wait_for_backend_port', return_value=True),
              patch.object(drop_loop, 'sample_memory'),
              patch.object(drop_loop, 'harness', side_effect=self.command),
              patch.object(drop_loop, 'active_session', return_value=self.active),
              patch.object(drop_loop, 'require_owned_session', return_value=self.active),
              patch.object(drop_loop, 'game_running', return_value=True),
              patch.object(drop_loop, 'live_freeze', return_value=(True, ['18:13:56 freeze: stalled'], 65)),
              patch.object(drop_loop, 'scan_run', return_value={'in_mission_freeze': False, 'freezes': [],
                                                               'run_json': fixture('good-run.json')})):
            row = drop_loop.cycle(self.args, 1)
        self.assertFalse(row['passed'])
        self.assertTrue(row['in_mission_freeze'])
        self.assertEqual(row['attempted_hold_s'], 240)
        self.assertIn(('stop', '--kill'), self.calls)
        self.assertNotIn(('leave',), self.calls)
        self.assertNotIn(('shot', 'held-ground'), self.calls)

    def test_port_timeout_prevents_launch(self) -> None:
        with (patch.object(drop_loop, 'prestart'), patch.object(drop_loop, 'wait_for_backend_port', return_value=False),
              patch.object(drop_loop, 'harness') as harness):
            row = drop_loop.cycle(self.args, 1)
        self.assertFalse(row['passed'])
        harness.assert_not_called()

    def test_port_race_retries_once(self) -> None:
        with (patch.object(drop_loop, 'prestart'), patch.object(drop_loop, 'wait_for_backend_port', return_value=True),
              patch.object(drop_loop, 'harness', return_value=(1, None, 'Port 40558 busy')) as harness):
            row = drop_loop.cycle(self.args, 1)
        self.assertFalse(row['passed'])
        self.assertTrue(row['start_retried'])
        self.assertEqual(harness.call_count, 2)
        self.assertIn('--keep-running', harness.call_args.args)

    def test_retry_port_timeout_prevents_second_launch(self) -> None:
        with (patch.object(drop_loop, 'prestart'),
              patch.object(drop_loop, 'wait_for_backend_port', side_effect=[True, False]),
              patch.object(drop_loop, 'harness', return_value=(1, None, 'Port 40558 busy')) as harness):
            row = drop_loop.cycle(self.args, 1)
        self.assertFalse(row['passed'])
        self.assertEqual(harness.call_count, 1)

    def test_healthy_cycle_keeps_screenshot_sequence(self) -> None:
        self.args.hold = 0
        with (patch.object(drop_loop, 'prestart'), patch.object(drop_loop, 'wait_for_backend_port', return_value=True),
              patch.object(drop_loop, 'sample_memory'),
              patch.object(drop_loop, 'harness', side_effect=self.command),
              patch.object(drop_loop, 'active_session', return_value=self.active),
              patch.object(drop_loop, 'require_owned_session', return_value=self.active),
              patch.object(drop_loop, 'game_running', return_value=True),
              patch.object(drop_loop, 'live_freeze', return_value=(False, [], 1)),
              patch.object(drop_loop, 'scan_run', return_value={'in_mission_freeze': False, 'freezes': [],
                                                               'run_json': fixture('good-run.json')})):
            row = drop_loop.cycle(self.args, 1)
        self.assertTrue(row['passed'])
        self.assertEqual([call[0] for call in self.calls], ['start', 'ship', 'shot', 'mission', 'state', 'shot', 'leave', 'state', 'shot', 'stop'])
        self.assertIn('--shots', self.calls[3])
        self.assertEqual(self.calls[2], ('shot', 'ship-arrival'))
        self.assertEqual(self.calls[5], ('shot', 'held-ground'))
        self.assertEqual(self.calls[8], ('shot', 'ship-return'))
        self.sleep.assert_called_once_with(10)
        self.assertTrue(row['ship_settle_ok'])
        self.assertTrue(row['qualifying'])

    def test_changed_session_is_not_stopped(self) -> None:
        with (patch.object(drop_loop, 'PROFILE', 'clonedivers-armory'),
              patch.object(drop_loop, 'active_session', return_value=dict(self.active, session='foreign')),
              patch.object(drop_loop.subprocess, 'run') as run, self.assertRaises(RuntimeError)):
            drop_loop.harness('stop', '--kill', session='owned')
        run.assert_not_called()

    def test_timeout_output_remains_json_serializable(self) -> None:
        error = drop_loop.subprocess.TimeoutExpired('harness', 30, output=b'partial output')
        with (patch.object(drop_loop, 'PROFILE', 'clonedivers-armory'),
              patch.object(drop_loop, 'active_session', return_value=None),
              patch.object(drop_loop.subprocess, 'run', side_effect=error)):
            code, data, text = drop_loop.harness('start', timeout=30)
        self.assertEqual(code, 124)
        self.assertIn('partial output', text)
        json.dumps({'code': code, 'data': data, 'text': text})

    def test_default_profile_and_foreign_session_are_refused(self) -> None:
        with patch.object(drop_loop, 'PROFILE', 'default'), self.assertRaises(RuntimeError):
            drop_loop.require_lab_profile()
        with (patch.object(drop_loop, 'active_session', return_value=dict(self.active, session='foreign')),
              self.assertRaises(RuntimeError)):
            drop_loop.require_owned_session('owned')
        with (patch.object(drop_loop, 'active_session', return_value=dict(self.active, profile='default')),
              self.assertRaises(RuntimeError)):
            drop_loop.require_owned_session('owned')


class HarnessIsolationTests(unittest.TestCase):
    def test_controls_env_is_removed_and_other_settings_survive(self) -> None:
        with (patch.dict(drop_loop.os.environ, {'HD2H_CONTROLS_DLL': 'lab.dll', 'HD2H_HOME': 'recorded-home'}),
              patch.object(drop_loop, 'active_session', return_value=None),
              patch.object(drop_loop, 'PROFILE', 'clonedivers-armory'),
              patch.object(drop_loop.subprocess, 'run', return_value=Mock(returncode=0, stdout='{}', stderr='')) as run):
            drop_loop.harness('state')
            self.assertEqual(drop_loop.os.environ['HD2H_CONTROLS_DLL'], 'lab.dll')
        self.assertNotIn('HD2H_CONTROLS_DLL', run.call_args.kwargs['env'])
        self.assertEqual(run.call_args.kwargs['env']['HD2H_HOME'], 'recorded-home')
        command = run.call_args.args[0]
        self.assertEqual(command[1:4], ['-B', '-c', drop_loop.HARNESS_WRAPPER])

    def test_wrapper_sets_dpi_before_running_harness_and_preserves_arguments(self) -> None:
        order = []
        user32 = Mock()
        user32.SetProcessDpiAwarenessContext.side_effect = lambda context: order.append(('dpi', context.value))
        def run_path(script: str, **kwargs: object) -> None:
            order.append(('harness', script))
            self.assertEqual(drop_loop.sys.argv, ['recorded/harness.py', '--target', 'hellforge', 'state'])
            self.assertEqual(kwargs['run_name'], '__main__')
        with (patch.object(drop_loop.ctypes, 'windll', Mock(user32=user32)),
              patch('runpy.run_path', side_effect=run_path),
              patch.object(drop_loop.sys, 'argv', ['-c', 'recorded/harness.py', '--target', 'hellforge', 'state']),
              patch.object(drop_loop.sys, 'path', list(drop_loop.sys.path))):
            exec(drop_loop.HARNESS_WRAPPER, {})
        # c_void_p represents -4 as the pointer-sized unsigned value.
        self.assertEqual(order, [('dpi', drop_loop.ctypes.c_void_p(-4).value), ('harness', 'recorded/harness.py')])


class MemoryTests(unittest.TestCase):
    def test_native_peak_uses_pid_and_preserves_large_handles_and_sizes(self) -> None:
        kernel, psapi = Mock(), Mock()
        handle = 0x123456789
        kernel.OpenProcess.return_value = handle
        def memory_info(process: int, pointer: object, size: int) -> int:
            self.assertEqual(process, handle)
            pointer._obj.PeakWorkingSetSize = 8 * 1024 ** 3
            return 1
        psapi.GetProcessMemoryInfo.side_effect = memory_info
        with patch.object(drop_loop.ctypes, 'WinDLL', side_effect=[kernel, psapi]):
            self.assertEqual(drop_loop.game_peak_working_set(27228), 8 * 1024 ** 3)
        kernel.OpenProcess.assert_called_once_with(0x1000, False, 27228)
        kernel.CloseHandle.assert_called_once_with(handle)
        self.assertIs(kernel.OpenProcess.restype, drop_loop.wintypes.HANDLE)

    def test_native_memory_query_failure_closes_handle(self) -> None:
        kernel, psapi = Mock(), Mock()
        kernel.OpenProcess.return_value = 123
        psapi.GetProcessMemoryInfo.return_value = 0
        with patch.object(drop_loop.ctypes, 'WinDLL', side_effect=[kernel, psapi]):
            self.assertIsNone(drop_loop.game_peak_working_set(27228))
        kernel.CloseHandle.assert_called_once_with(123)

    def test_peaks_are_kept_and_missing_nvidia_is_tolerated(self) -> None:
        sampler = drop_loop.MemorySampler(123)
        with (patch.object(drop_loop, 'game_peak_working_set', side_effect=[1024, 4096, None]),
              patch.object(drop_loop.subprocess, 'run', side_effect=[Mock(stdout='8000\n1000\n'),
                                                                    Mock(stdout='7000\n'), FileNotFoundError()]) as run):
            for _ in range(3):
                sampler.sample()
        self.assertEqual(sampler.peak_working_set_bytes, 4096)
        self.assertEqual(sampler.peak_vram_mib, 8000)
        self.assertEqual(run.call_args.args[0], ['nvidia-smi', '--query-gpu=memory.used', '--format=csv,noheader,nounits'])

    def test_sampler_thread_stops_and_writes_cycle_fields(self) -> None:
        sampler = drop_loop.MemorySampler(123)
        sampled = drop_loop.threading.Event()
        def sample() -> None:
            sampler.peak_working_set_bytes = 2048
            sampler.peak_vram_mib = 4096
            sampled.set()
        row = {}
        with patch.object(sampler, 'sample', side_effect=sample):
            sampler.start()
            try:
                self.assertTrue(sampled.wait(2))
            finally:
                sampler.stop(row)
        self.assertFalse(sampler.thread.is_alive())
        self.assertEqual(row, {'peak_working_set_bytes': 2048, 'peak_vram_mib': 4096})

    def test_unknown_memory_is_null(self) -> None:
        sampler = drop_loop.MemorySampler(None)
        with patch.object(drop_loop.subprocess, 'run', side_effect=FileNotFoundError()):
            sampler.sample()
        self.assertIsNone(sampler.peak_working_set_bytes)
        self.assertIsNone(sampler.peak_vram_mib)


class MainTests(unittest.TestCase):
    def test_exit_zero_requires_every_cycle_to_pass(self) -> None:
        good = qualifying_row()
        bad = fixture('failed-cycle.json')
        early = fixture('early-cycle.json')
        for row in (good, bad, early):
            drop_loop.classify_cycle(row)
        for rows, expected in (([good, good], 0), ([bad, good], 1), ([good, bad], 1), ([early, good], 1)):
            with self.subTest(expected=expected, passes=[r['passed'] for r in rows]):
                args = ['drop_loop.py', '--label', 'fixture', '--cycles', '2', '--out', 'fixture-output']
                sink = io.StringIO()
                opened = MagicMock()
                opened.__enter__.return_value = sink
                with (patch.object(drop_loop.sys, 'argv', args), patch.object(drop_loop, 'require_lab_profile'),
                      patch.object(drop_loop, 'cycle', side_effect=copy.deepcopy(rows)) as cycle,
                      patch.object(Path, 'mkdir'), patch('builtins.open', return_value=opened), patch('builtins.print')):
                    self.assertEqual(drop_loop.main(), expected)
                self.assertEqual(cycle.call_count, 2)
                written = [json.loads(line) for line in sink.getvalue().splitlines()]
                self.assertEqual([r['passed'] for r in written], [r['passed'] for r in rows])


if __name__ == '__main__':
    unittest.main()
