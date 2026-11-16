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

W2b review replays (copied 2026-11-15):

| Fixture | Original source and retained fields |
| --- | --- |
| `reviewed-mission-ends.json` | Harness runs `20261108-191823`, `194108`, `200242`, `20261109-124213`, `130946` (five late failures), and `20261108-163100`, `165357`, `210021`, `221633`, `20261109-192826` (five squad wipes), all with suffix `-mods-hellforge`. Selects `run.json` start/finish/exit and mission in_control/left/leave; copies each `hellforge-session/backend/mission-bodies/*-Operation_Mission_end.json` filename, `missionSuccess` and participants' `extracted` booleans only. Account identifiers, loadouts, mission GUIDs and all other body fields are omitted. `hold_s` supplies the replay's requested hold; it is not a body field. |
| `reviewed-return-faults.json` | Every crash line from `D:\HellForge\logs\sessions\1283fd50f2af4b3f9a38a57d7f5c69d2\modloader.log` and `500450cafa144353bbbac7b95636b1b3\modloader.log`. These are the two retained `+0x1e6b3e` sessions. Session IDs identify local runtime recordings, not player accounts. |
| `reviewed-freeze-cycle.json` | The `r47-lighter-boot` row in `lab-20261109-r3/cycles.jsonl`: selects label/cycle, freeze lines, exposure timing and SEAF count, mission in_control, and run exit/timestamps. |
| `startup-fault.log` | Every crash line from the existing `20261108-212200-mods-hellforge` source; primary `+0x2bd8ad` with dbgcore fail-fast follow-ons, used to reject a fake shared baseline. |

New JSON fixtures were built with a field allowlist and scanned for Steam account IDs, account-name
keys and Windows user paths. No user names, Steam IDs or account names are included. Cycle-flow
state sequences (4 -> 5 -> 3) are mocked from the harness's documented return behavior and the
review's timing evidence; the recordings do not contain a full live state trace. Historical auto-end
replays supply successful driver return/settle/quit observations to test the corrected path; they do
not rewrite the original run outcomes. The five late-failure exposure expectations are 112.230,
97.949, 97.095, 230.302 and 120.000 seconds; the last is capped at its 120-second hold because the
harness control clock was saved only to whole seconds (body offset 120.571).
Cycle-flow tests supply synthetic live state responses using the shape returned by harness
`command_state` (`live.state`, `live.in_mission`), and add the new `ship_settle_ok` observation to the
historical qualifying cycle. Environment, DPI and memory APIs are mocked.

The requested `D:\ClaudeWork\orch\reports\test-loop\REPORT.md` and `VERIFY.md` were absent when this
work was done. W2 in `PLAN.md`, the retained samples above and read-only harness source were available.
