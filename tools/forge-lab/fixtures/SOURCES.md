# Recorded samples

Copied on 2026-11-15 from retained local evidence, without changing the originals. JSON samples retain
the source values; run samples select `exit`, `outcome`, `clean`, `finished`, `dumps`, `missions`, with
`pid` and `started` also retained in the handler sample for session attribution.
Cycle samples omit only the unrelated candidate inventory. No live session was read or controlled.

| Fixture | Original source |
| --- | --- |
| `good-cycle.json` | `D:\ClaudeWork\clonedivers-lab\results\lab-20261109-r3\cycles.jsonl`, label `r47-cis-stability`, cycle 1 |
| `early-cycle.json` | Same file, label `r47-nodroids-stability`, cycle 1; recorded `leave.to_ship_s: 0` |
| `failed-cycle.json` | `D:\ClaudeWork\clonedivers-lab\results\cycles.jsonl`, label `empire-r45-v3`, cycle 1 |
| `good-run.json` | `D:\ClaudeWork\harness-home\runs\20261109-190346-mods-hellforge\run.json` |
| `failed-run.json` | `D:\ClaudeWork\harness-home\runs\20261108-212200-mods-hellforge\run.json` |
| `nonzero-run.json` | `D:\ClaudeWork\harness-home\runs\20261108-162458-mods-hellforge\run.json`; real `exit.code: 3075604483` |
| `return-fault-run.json` | `D:\ClaudeWork\harness-home\runs\20261109-161045-mods-hellforge\run.json`; exit object is empty despite crash evidence |
| `return-fault.log` | Every `crash:` line from that return-fault run's `hellforge-session\modloader.log` |
| `quit-fault.log` | Every `crash:` line from `20261108-162458-mods-hellforge\hellforge-session\modloader.log` |
| `handled-clean-exit.log` | The two `committed teardown` lines in `20261108-193640-mods-hellforge\hellforge-session\engine-logs\Clonedivers-clean-exit.log` (an accumulated log copied into a later session) |
| `handled-run.json` | `D:\ClaudeWork\harness-home\runs\20261108-193149-mods-hellforge\run.json`; PID 27228 matches one handler line |

The handled-message test deliberately combines the earlier quit-fault lines with later real handler
messages and their matching PID/time metadata to exercise classification. It does not claim those files
came from the same historical session. Separate tests reject older handler messages and reused PIDs
outside the run window.
Cycle-flow tests supply synthetic live state responses using the shape returned by harness
`command_state` (`live.state`, `live.in_mission`), and add the new `ship_settle_ok` observation to the
historical qualifying cycle. Environment, DPI and memory APIs are mocked.

The requested `D:\ClaudeWork\orch\reports\test-loop\REPORT.md` and `VERIFY.md` were absent when this
work was done. W2 in `PLAN.md`, the retained samples above and read-only harness source were available.
