# Forge lab loop

`drop_loop.py` runs unattended HellForge drop cycles through
[BingusDevelopmentHarness](https://github.com/CowboyBingus/BingusDevelopmentHarness) (0BSD): launch, title
skip, ship, a solo mission at the chosen difficulty, a timed hold in the mission, abandon and engine quit.
Each cycle appends a row to `results/cycles.jsonl` with the mission, SEAF activity and every crash line
from HellForge's `modloader.log` (its `result.json` misses some crashes).

Lab use only, on a lab profile (never the player's own), with Steam running and the game closed:

```powershell
git clone https://github.com/CowboyBingus/BingusDevelopmentHarness D:\ClaudeWork\bingus-harness
# harness.py: --difficulty choices=range(1, 12) for Hell Forge level 11
$env:HD2H_HELLFORGE_PROFILE = 'clonedivers-armory'     # an unlock-all lab profile with settings seeded
$env:HD2H_LOADER_ZIP = '<Bingus-Shared-Loader-v20.zip>'
$env:HD2H_HOME = 'D:\ClaudeWork\harness-home'
$env:HD2_GAME_LUA = '<path to game_lua.py>'
$env:HD2_LUA51_DLL = '<path to the lab game copy\bin\lua51.dll>'
python drop_loop.py --label baseline --cycles 4 --difficulty 11 --faction automaton --hold 300
```

Use the Python interpreter with which the harness was verified (Python 3.13 on this lab PC).
`HD2_GAME_LUA` selects the runner for the game's Lua VM; `HD2_LUA51_DLL` selects that VM's DLL.
Set both explicitly for harness builds/tests rather than relying on the harness's sibling-checkout and
Steam-install defaults. `HD2H_HOME` holds `state/active.json` and collected `runs/`;
`HD2H_LOADER_ZIP` is the published Shared Loader archive; `HD2H_HELLFORGE_PROFILE` must be
`clonedivers-armory`. The driver defaults HOME and LOADER_ZIP to this machine's D: lab paths.
Optional `CLONEDIVERS_HARNESS` and `HD2H_HELLFORGE_ROOT` override the checkout and installation paths.

From a packaged (MSIX) app such as the Claude desktop app, correct the container's copy of Steam's pid
once per Steam start: `Set-ItemProperty 'HKCU:\Software\Valve\Steam\ActiveProcess' -Name pid -Value (Get-Process steam).Id -Type DWord`.
Results so far: [QA sweep 2026-11-08](../../docs/QA-SWEEP-2026-11-08.md).

The driver accepts only `clonedivers-armory` and refuses existing game/Forge or harness sessions.
It waits for backend port 40558 and retries a port-related start failure once. During the mission hold,
a HellForge `freeze:` diagnostic or a heartbeat stalled for 60 seconds fails the cycle and forces a
harness stop with log collection and restoration. Teardown freezes remain separate from mission freezes.
`--shots` captures ship arrival, briefing, landing, held ground and ship return in sequence.
Every harness child removes `HD2H_CONTROLS_DLL` from its environment. It runs through a small `runpy`
wrapper that calls Windows `SetProcessDpiAwarenessContext(-4)` before importing capture code, preserving
the game's 1280x720 window pixels instead of 853x480 DPI-scaled captures. Installed mods still need to be
checked by the release runner; removing the environment override does not audit the installed mod list.
With or without `--shots`, a completed hold is followed by `leave`, a further 10-second ship wait and a
harness `state` recheck for live state 3 before stopping. During the hold, state is checked about every
10 seconds to detect missions that finish themselves. The driver also watches the active HellForge
session's `backend/mission-bodies/*-Operation_Mission_end.json`: state 4 can persist for about 29 seconds
after a squad wipe. Once a mission ends itself, the driver waits up to 180 seconds, polling every two
seconds, for state 3 before calling the harness's ship no-op `leave` to record the return. It then settles
and quits normally. A stuck return is a driver error; a squad wipe by itself is reduced coverage.
Optional `CLONEDIVERS_LAB_GUARD` and `CLONEDIVERS_LAB_MEMORY_SCRIPT` hooks have no dated defaults.

Exit codes are 0 when every cycle is clean and qualifying, 1 when any cycle has an event, 2 for a driver
or infrastructure error (including start/harness/cleanup failure or invalid arguments), and 3 when there
are no events or errors but some cycles have reduced coverage. Errors take precedence over events.
The last stdout line is a summary JSON object (`summary: true`) with counts for `cycles`, `planned_cycles`,
`events`, `driver_errors`, `qualifying`, `reduced_coverage`, `returns`, total `mission_s`, and `exit_code`.
A later cleanup error never replaces the first error. A failed stop remains an error even if a forced
stop succeeds, except the expected unclean stop/finish status when collecting a recorded game event.

Cycle rows retain the raw `mission`, `steps`, `run`, freeze and SEAF counters, and add these gate fields:

| Field | Meaning |
| --- | --- |
| `planet`, `mission_type`, `difficulty`, `faction`, `seaf_spawns` | Top-level scenario and activity. Mission type comes from `mission.steps.select.mission_type`, not the control step's runtime mode. Planet/type are null when selection never completed. |
| `mission_ended_early` | A mission-body end or state change ended gameplay before the hold, or `leave.to_ship_s == 0`. Mission-body clocks are UTC and matched to the mission across midnight. Harness `in_control` is saved to whole seconds, so an end in the final clock second is conservatively early, including the recorded 120.571 s end on a 120 s hold. |
| `mission_s`, `hold_s`, `mission_end_after_s` | Mission exposure in seconds, capped at the requested hold and the earliest observed mission end; live elapsed time starts at the harness's player-control timestamp. `hold_s` is the requested duration. `mission_end_after_s`, when present, retains the body timestamp's millisecond precision. Exposure is rounded to three decimals. |
| `return_observed` | The game reached the ship (by harness abandon or its own return), passed the ship settle check, and took the normal quit path. An early mission can still provide return coverage. |
| `qualifying`, `reduced_coverage` | Qualifying means a full hold and an observed return. Reduced coverage means that full combination was not observed; it is not an event or error by itself. |
| `ship_settle_ok` | State was still 3 after the extra 10 seconds on the ship. |
| `event`, `event_signatures` | One event per affected cycle, on **every** cycle regardless of qualification: an in-mission freeze or a native fault except an evidenced, handled `+0x626296`. Signatures are deduplicated. Fail-fast/by-module follow-ons add no signature; `+0x99d7cd` accompanying `+0x1e6b3e` belongs to that primary fault. |
| `passed`, `failed`, `driver_error` | `passed` means no event and no driver error; `failed` is its inverse. A clean early-ended cycle has `passed: true`, `qualifying: false`. `driver_error` separates harness/cleanup errors and unexplained process exits from game events. Missing exit evidence without a recorded event is a driver error. |
| `run_json.exit`, `run_json.exit_code` | Original nested exit details and a convenient copy of `exit.code`; the harness has no top-level `exit_code`. |
| `peak_working_set_bytes` | Game PID's Windows lifetime peak working set, sampled from launch through cleanup. Null when unavailable. |
| `peak_vram_mib` | Highest device memory usage reported by `nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits`, sampled in a thread about every 3 seconds. Maximum across GPUs; includes other applications and is not process-attributed VRAM. Null if NVIDIA tooling/data is unavailable. |
| `crashes` | Every `crash:` line from the copied `modloader.log`. Each has `kind` (`fault`, `by-module`, `fail-fast`), full `line`, `code`, `site`, `module`, `offset`, `handled_quit`. Only a primary `at module+offset` supplies a site; follow-on entries retain their text with null module/offset. |
| `handled_quit_evidence`, `quit_crash` | Only `helldivers2.exe+0x626296` with an actual teardown-handler message matching this run's PID and time window is tagged `handled_quit` and exempt. Copied engine logs can contain older sessions; loading/arming clean-exit, an unscoped legacy message, or exit code 0 alone is insufficient. `quit_crash` preserves the separate quit-signature indicator. |

The local A/B wrapper `D:\ClaudeWork\orch\lab\pack-gate.ps1` treats driver codes 0, 1 and 3 as data and
continues. Code 2 (or a missing/unexpected exit) makes the gate INVALID: stop, restore, and report.
`-TopUp 4` is the default: after the planned AABB cycles it adds up to four one-cycle blocks per short
arm, interleaved A/B, checking exposure and returns after each. `-TopUp 0` disables extra cycles.
`-DryRun` prints the complete plan, including conditional top-ups, exit handling and restoration.

`score_ab.py A B --expected 12 --hold 180` scores events over all cycles. Its rule is PASS when B has
at most one event or B events are no greater than A; FAIL when B has at least two and A has zero, or
`+0x1e6b3e` occurs only on B; otherwise INCONCLUSIVE. Additionally, the verdict is INCONCLUSIVE for
coverage unless **each arm** reaches at least 80% of planned mission exposure (`cycles x hold`) and
80% of planned returns. Top-ups add exposure/returns without changing those planned denominators.
The report shows both coverages, events per 1000 mission-seconds (unknown at zero exposure), and
BASELINE for signatures shared by both arms. `rule_verdict` retains the event-only result when coverage
is short. PASS means only “no gross regression.” Legacy rows use their recorded durations and clean
returns when explicit coverage fields are absent; new gate runs always use explicit fields.

Queue consumers can use `parse_queue_request(text)` or `load_queue_request(path)` from the driver.
Requests require string fields `name`, `zip_or_folder`, `kind` and `checks`. Generate JSON with
`json.dumps` so Windows backslashes are escaped correctly. Invalid escapes are rejected.

Run the no-game regression checks with `python -B -m unittest discover -s tools/forge-lab -v`.
Recorded fixture provenance is listed in [fixtures/SOURCES.md](fixtures/SOURCES.md). Tests mock all
harness, process-control and NVIDIA calls; they never launch the game.
