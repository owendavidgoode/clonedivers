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
python drop_loop.py --label baseline --cycles 4 --difficulty 11 --faction automaton --hold 300
```

From a packaged (MSIX) app such as the Claude desktop app, correct the container's copy of Steam's pid
once per Steam start: `Set-ItemProperty 'HKCU:\Software\Valve\Steam\ActiveProcess' -Name pid -Value (Get-Process steam).Id -Type DWord`.
Results so far: [QA sweep 2026-11-08](../../docs/QA-SWEEP-2026-11-08.md).

The driver accepts only `clonedivers-armory` and refuses existing game/Forge or harness sessions.
It waits for backend port 40558 and retries a port-related start failure once. During the mission hold,
a HellForge `freeze:` diagnostic or a heartbeat stalled for 60 seconds fails the cycle and forces a
harness stop with log collection and restoration. Teardown freezes remain separate from mission freezes.
`--shots` captures ship arrival, briefing, landing, held ground and ship return in sequence.
Optional `CLONEDIVERS_LAB_GUARD` and `CLONEDIVERS_LAB_MEMORY_SCRIPT` hooks have no dated defaults.

Queue consumers can use `parse_queue_request(text)` or `load_queue_request(path)` from the driver.
Requests require string fields `name`, `zip_or_folder`, `kind` and `checks`. Generate JSON with
`json.dumps` so Windows backslashes are escaped correctly. Invalid escapes are rejected.

Run the no-game regression checks with `python -B -m unittest discover -s tools/forge-lab -v`.
