"""No-game regression checks. Run with python -m unittest discover -s tools/forge-lab."""
import argparse
import json
import unittest
from unittest.mock import MagicMock, Mock, patch

import drop_loop


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
        row = {'held_s': 240, 'crashes': [{'site': 'helldivers2.exe+0x626296'}]}
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
        run.rglob.side_effect = [[log], [seaf]]
        (run / 'run.json').read_text.return_value = json.dumps({'missions': self.missions, 'exit': {'at': self.ended}})
        facts = drop_loop.scan_run(run)
        self.assertTrue(facts['in_mission_freeze'])
        self.assertEqual(facts['auto_waves'], 1)
        self.assertEqual(facts['seaf_spawns'], 1)


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
        self.args = argparse.Namespace(label='test', difficulty=11, faction='automaton', preset='light',
                                       shots=True, hold=240)
        self.active = {'session': 'owned', 'run': 'owned-run', 'profile': 'clonedivers-armory', 'target': 'hellforge'}
        self.calls: list[tuple[str, ...]] = []

    def command(self, *args: str, **kwargs: object) -> tuple[int, dict, str]:
        self.calls.append(args)
        return 0, self.active if args[0] == 'start' else {}, ''

    def test_freeze_forces_stop_and_skips_screenshot_leave_wait(self) -> None:
        with (patch.object(drop_loop, 'prestart'), patch.object(drop_loop, 'wait_for_backend_port', return_value=True),
              patch.object(drop_loop, 'sample_memory'),
              patch.object(drop_loop, 'harness', side_effect=self.command),
              patch.object(drop_loop, 'active_session', return_value=self.active),
              patch.object(drop_loop, 'require_owned_session', return_value=self.active),
              patch.object(drop_loop, 'game_running', return_value=True),
              patch.object(drop_loop, 'live_freeze', return_value=(True, ['18:13:56 freeze: stalled'], 65)),
              patch.object(drop_loop, 'scan_run', return_value={'in_mission_freeze': False, 'freezes': []})):
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
              patch.object(drop_loop, 'scan_run', return_value={'in_mission_freeze': False, 'freezes': []})):
            row = drop_loop.cycle(self.args, 1)
        self.assertTrue(row['passed'])
        self.assertEqual([call[0] for call in self.calls], ['start', 'ship', 'shot', 'mission', 'shot', 'leave', 'shot', 'stop'])
        self.assertIn('--shots', self.calls[3])
        self.assertEqual(self.calls[2], ('shot', 'ship-arrival'))
        self.assertEqual(self.calls[4], ('shot', 'held-ground'))
        self.assertEqual(self.calls[6], ('shot', 'ship-return'))

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


if __name__ == '__main__':
    unittest.main()
