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
10 seconds to detect missions that finish themselves.
Optional `CLONEDIVERS_LAB_GUARD` and `CLONEDIVERS_LAB_MEMORY_SCRIPT` hooks have no dated defaults.

The command exits 0 only if **every** requested cycle passed. Failures and reduced coverage both give
exit 1; invalid arguments give exit 2. Reduced coverage needs another qualifying run, and is not a crash
event by itself. A later cleanup error never replaces the first error. A failed stop remains a failure
even if a forced stop succeeds.

Cycle rows retain the raw `mission`, `steps`, `run`, freeze and SEAF counters, and add these gate fields:

| Field | Meaning |
| --- | --- |
| `planet`, `mission_type`, `difficulty`, `faction`, `seaf_spawns` | Top-level scenario and activity. Mission type comes from `mission.steps.select.mission_type`, not the control step's runtime mode. Planet/type are null when selection never completed. |
| `mission_ended_early`, `reduced_coverage` | Mission left gameplay during the hold, or `leave.to_ship_s == 0`. Such a cycle cannot pass; without an independent error it has `failed: false`. |
| `qualifying` | Full requested hold, a real return (`leave.to_ship_s > 0`), and verified ship settle. This describes coverage independently of later quit faults. |
| `ship_settle_ok` | State was still 3 after the extra 10 seconds on the ship. |
| `passed`, `failed` | Pass requires qualifying coverage, no detected failure and a recorded process exit of 0. `failed` covers step/cleanup errors, process death, mission stalls, other native faults and nonzero process exits. Missing exit evidence cannot pass. |
| `run_json.exit`, `run_json.exit_code` | Original nested exit details and a convenient copy of `exit.code`; the harness has no top-level `exit_code`. |
| `peak_working_set_bytes` | Game PID's Windows lifetime peak working set, sampled from launch through cleanup. Null when unavailable. |
| `peak_vram_mib` | Highest device memory usage reported by `nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits`, sampled in a thread about every 3 seconds. Maximum across GPUs; includes other applications and is not process-attributed VRAM. Null if NVIDIA tooling/data is unavailable. |
| `crashes` | Every native `crash:` line from the session's copied `modloader.log`, including secondary and by-module lines. Each has full `line`, `code`, `site`, `module`, `offset`, `handled_quit`. Module/offset are null if the source line has none. |
| `handled_quit_evidence`, `quit_crash` | Only `helldivers2.exe+0x626296` with an actual teardown-handler message matching this run's PID and time window is tagged `handled_quit` and exempt. Copied engine logs can contain older sessions; merely loading/arming clean-exit or an unscoped legacy message is insufficient. `quit_crash` preserves the separate quit-signature indicator; any other fault, including secondary faults, fails the cycle. |

Queue consumers can use `parse_queue_request(text)` or `load_queue_request(path)` from the driver.
Requests require string fields `name`, `zip_or_folder`, `kind` and `checks`. Generate JSON with
`json.dumps` so Windows backslashes are escaped correctly. Invalid escapes are rejected.

Run the no-game regression checks with `python -B -m unittest discover -s tools/forge-lab -v`.
Recorded fixture provenance is listed in [fixtures/SOURCES.md](fixtures/SOURCES.md). Tests mock all
harness, process-control and NVIDIA calls; they never launch the game.
