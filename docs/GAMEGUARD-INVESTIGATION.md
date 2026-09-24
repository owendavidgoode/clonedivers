# GameGuard history review — September 23, 2026

## September 24 follow-up: manual Steam also retains exited game

User reports the shutdown hang again after manual Steam startup. Captured Steam
PID 21500, started at 16:11:18, outside a Windows job with an unrestricted,
non-elevated token. Steam launched HD2 PID 17592 at 16:13:42 and crash helper
PID 11564 at 16:13:53. Both process handles are now signaled (`waitStatus=0`),
with exit code 0, while the per-app Steam registry still says `Running=1`.
No new 114 appears in this session's Steam launch records. Steam has been left open.

This directly establishes that the shutdown-tracking hang can occur without
automation job inheritance. It does not establish or refute that inheritance as
the cause of the separate earlier 114 failures. Keep the two hypotheses separate.
Receipts: `dist/gameguard-history-2026-09-23/manual-steam-20260924.json` and
`manual-steam-exit-20260924.json`. The September 23 state below is historical.

The camera changes are not an established cause of error 114. The strongest current
lead is the context/state of Steam startup, particularly starting Steam through
automation. The exact failing GameGuard operation remains unknown. Do not describe
job membership, the camera loader, FOV, or any specific utility as the proven cause.

Steam was forcibly closed at the user's request after its Stop operation stalled.
The subsequent process check confirms Steam and HD2 are absent. No automatic restart
was performed. The current installation retains 1,133 active pack files and native
`vertical_fov = 85`; the rejected cutaway and six Lua diagnostic files remain absent.
No pack, executable, security setting, or public release changed during this review.

## What the original records establish

Dates/times below are America/Chicago. Codex JSONL timestamps are UTC, five hours
ahead for these dates. Sources are original public messages/tool records, local
repair receipts and Steam's process log, not repeated auto-review transcripts.

| Incident | Exact change / comparison | Result and implication |
| --- | --- | --- |
| September 7, texture benchmark | JohnsonMode/skinny comparison; original 754-file pack restored and hash-verified. No camera change in that trial. | Error 114 before gameplay. This predates the September 12 FOV work. PresentMon separately failed ETW permissions; that does not prove it caused GameGuard's failure. |
| September 12, first camera trial | Only `vertical_fov` in the native user settings changed, 55 to 75. No camera-distance edit, injected camera DLL, or executable patch. An RC intro patch was installed later. | First launch attempted the game executable through `sky.launch_app`; Steam started during that sequence. Initial 114 cannot be assigned to FOV from timing alone. |
| September 12, 19:16–19:22 | Restored FOV 55; then parked all 756 mod files; then Steam verification reacquired `bin/GameGuard/nplsm.des`. | All three launch conditions still produced 114. Reacquiring that file did not fix the immediate next launch. FOV 75 and active mods were not necessary for the failure. |
| September 12, later that evening | User reported clean launches with clones off and on after reboot/verification. Later reapplied FOV 75. | RC intro/game launched at 21:03; user said “75deg is better” at 21:10. This is positive evidence that FOV 75 can run successfully. FOV was subsequently set to 85. |
| September 13, performance trial | Restored all 16 modified geometry files to exact original hashes before the final baseline. | A separate error **110**, with `GameMon.des` PID 9292 still present from 20:47:10 after HD2 exited. Capture workers and benchmark tracing sessions were gone. Reboot removed the old process and the final 90-second gameplay capture succeeded. Stale GameGuard state was a plausible cause; the internal cause of 110 was not proven. |
| September 23, Lua camera investigation | Public Bingus v16 loader plus a bounded API-inventory probe, installed as six additive asset files. No camera mutation or native binary edit. | 114; no probe log. We cannot tell whether the loader/probe ran at all. Removing all six files did not clear 114, including after a subsequent reboot. This does not validate the loader as safe/working either. |
| September 23, baseline isolation | All 1,133 mod files parked. Signed GameGuard reinstall had already returned 0; later the per-game GameGuard folder was preserved and freshly downloaded. | Vanilla still failed at 21:24:07; fresh GameGuard still failed at 21:26:32. Repeated pack rollback or GameGuard reinstall did not explain or resolve this instance. |
| September 23, startup-path comparison | Closed Steam, user opened it from Windows Start and launched vanilla from the library. Later user reopened Steam manually and launched restored Clonedivers through its launcher. | User confirmed both worked. The second run had all 1,133 pack files active. A working Steam session can accept the Clonedivers launch request. Startup context/state is a stronger lead than FOV or pack contents. |

The September 13 successful post-reboot launch also used an automated Steam URI
request. Therefore **not every automated launch fails**. Starting a new Steam client
and sending a launch request to an already running client must be recorded separately.

## The specific launch-context hypothesis

Historical failing calls include direct game launch through the old computer-use
API, `Start-Process steam.exe -applaunch 553850`, and starting a new Steam session
via `steam://rungameid/553850`. Several retries restarted Steam from the same
automation path. They changed assets or repaired GameGuard while keeping that
potential confounder. The historical logs did not capture Steam's token/job state.

Read-only measurements made during this review found:

| Current process | In a Windows job | Restricted token | Elevated |
| --- | --- | --- | --- |
| Default command-runner PowerShell | Yes | Yes | No |
| Approved command-runner PowerShell | Yes | No | No |
| Codex CLI process | Yes | No | No |
| Windows Explorer | No | No | No |
| Working Clonedivers launcher | No | No | No |

Thus approval removes the observed restricted-token difference but does not remove
the observed job membership. It is incorrect to assume an approved command was an
ordinary desktop launch, or to claim that all failing calls used a restricted token.

Windows normally associates child processes with their parent's job, subject to
the job's configuration. Job membership alone does not establish harmful limits,
and the failed Steam processes were gone before this measurement. These are present
day observations, not retrospective proof of inherited restrictions in those runs.
See Microsoft's [job-object documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

The hypothesis to test is: **a newly automation-started Steam process has a different
process environment/lifecycle than desktop-started Steam, and that difference can
interfere with GameGuard initialization.** Candidate mechanisms include inherited
job configuration, token/environment differences, or client/service lifecycle state.
We have not identified a particular failing Windows API or GameGuard check.
Elapsed time and state cleanup also differed between failed and successful trials.

## Failures that must remain separate

- **Current Steam hang:** HD2 PID 6844 and `crs-handler` PID 13548 both had signaled
  process handles and exit code 0 while Steam retained `Running=1`. This is confirmed
  stale tracking after a successful game session, not a still-running game/helper
  or evidence of another 114. Whether a shared lifecycle issue underlies both
  symptoms remains unknown.
- **Earlier Steam hang:** September 7 and several failed September 23 launches
  caused Steam to track Arc/browser helpers spawned by the GameGuard error page.
  That explains those sessions' lingering process trees. The latest successful
  session did not show that browser chain, so that explanation cannot simply be reused.
- **Actual RC intro crash:** the September 12 19:30:45 dump recorded access violation
  `0xC0000005` in `bink2w64.dll+0x22AD9`. This was a video-decoder failure after the
  GameGuard hurdle, distinct from 114. The original two-slice Bink encode differed
  from the game's working four-slice encode; re-encoding was part of the successful fix.
- **AT-TE cutaway:** it failed visually and removed exterior geometry. A prior crash
  was reported, but no evidence established a GameGuard cause for that crash.
- **Old service-path repair:** September 7 changed `npggsvc` from System32 to SysWOW64.
  `Start-Service` failed; a later native start only reported START_PENDING. The
  original vendor path was restored. This was not a verified repair and should not
  be repeated as a historical “known fix.”

Arrowhead explicitly describes [114 as having multiple possible causes](https://arrowhead.zendesk.com/hc/en-us/articles/14732747845020-I-receive-Error-114-when-attempting-to-launch-HELLDIVERS-2).
The code itself is not evidence that FOV was detected as cheating. An earlier
September 13 response linked a vendor **115** duplicate-execution article while
discussing **110**; retain the observed lingering-process evidence, but do not use
that article as an authoritative definition of 110.

## Next discriminating test

Keep FOV, pack, GameGuard files and the removed camera probe fixed. No more repair
or rollback between comparison arms. This test needs actual launches and has not
been performed during the review.

1. User opens Steam from Start. Capture its process context **before** launching.
   Launch from Clonedivers, record success/error, then capture exit state.
2. Close Steam completely. Start it through the same approved automation command
   used in failing trials, **without launching HD2 yet**. Capture Steam's context.
   Launch HD2 using the same Clonedivers button. Record result and exit state.
3. Return to manual Steam startup, with no file/settings changes, and repeat the
   same launch. This A–B–A comparison distinguishes a reproducible startup-path
   effect from a one-off recovery. If reboot is needed, apply equivalent startup
   conditions to each arm instead of comparing a dirty boot with a clean one.

The local read-only helper `dist/gameguard-history-2026-09-23/Capture-LaunchContext.ps1`
records job membership and token flags. Run it with a distinct `-Label` while each
Steam process still exists. It does not launch, stop, inject into, read process
memory from, or alter any process. Access failures remain explicit, not false values.
If a reproducible context difference emerges, investigate that configuration; if
both contexts fail, pursue background/service startup evidence. If 114 persists
without a readable internal reason, paired GameGuard logs are available for vendor
support. No logs were uploaded or sent to anyone.

Only after stable baseline launches should the Lua probe be reintroduced as its own
single-variable trial. FOV-only, asset mesh edits and a Lua loader are different
changes and should never share a generic “camera test” failure label.

## Evidence locations

- Original September 7 Codex session:
  `C:/Users/goode/.codex/sessions/2026/09/06/rollout-2026-09-06T23-57-47-01a07a3a-c831-7f50-ab9f-425508441727.jsonl`;
  lines 1049, 1151–1165, 1218, 1267–1297, 1353–1480.
- Original main Codex session:
  `C:/Users/goode/.codex/sessions/2026/09/12/rollout-2026-09-12T18-00-46-01a097da-12f9-7ce3-b4a5-1012214eac32.jsonl`;
  FOV change 386; launch 1170 and 1298; baseline comparisons 1396–1628;
  FOV success 3754 and user confirmation 3889; error 110/process evidence 7284–7341;
  automation startup 12853, 13068, 13186; manual success 13222–13234.
- Supporting contemporaneous notes: `docs/JOHNSONMODE.md`,
  `docs/archive/2026-09-12-session/RC-UPGRADE.md`, `docs/AT-TE-CAMERA.md`.
- Service experiment transcripts: `dist/gameguard-path-repair.log` and
  `dist/gameguard-path-restore.log`.
- Current isolation/exit receipts: `dist/vanilla-startup-2026-09-23/result.json`
  and `successful-clonedivers-stale-steam.json` in that directory.
- This review's evidence: `dist/gameguard-history-2026-09-23/` contains session
  catalogs, selected-event index, Steam timeline with source line numbers,
  current context snapshots, ancestry, current state and saved successful-session
  GameGuard logs. Failed-run GameGuard logs remain in the earlier isolation folder.
  The event index is a search aid; original records/receipts are authoritative.
