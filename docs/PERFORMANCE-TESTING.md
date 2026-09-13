# Performance support capture

Use this workflow on the affected PC before choosing a reduced-texture default.
File size reductions are not measured VRAM or FPS improvements. The historical r6
512/128 and 256/64 builds are not current release candidates.

## Prepare a comparison

1. In the launcher, open Support information and copy the previewed report. Record
   the player's game render resolution/scale, texture quality, FOV, cap and VSync,
   planet, mission difficulty, squad size and whether they host or join. Diagnostics
   reports desktop bounds and GPU memory capacity, not game resolution or live use.
2. First compare the current Lighter textures installation with Helldivers mode,
   then return to Lighter textures. Keep game settings fixed. This establishes
   whether the pack explains the entire slowdown or adds to a vanilla limitation.
3. For the candidate trial, use current-r8 Full/Lighter/Reduced content with the same
   selected visual mode. Compare 1024 and 512 caps at the **same resident floor**;
   changing both cap and floor does not isolate the cap's effect.
4. Close the game between pack conditions. Warm up a comparable gameplay route for
   2–3 minutes before capture. Do not clear shader caches. Use the same equipment,
   loadout and camera route. Capture traversal and combat separately; combat is less
   repeatable. Keep startup/load times separate from gameplay measurements.
5. Repeat important conditions three times in alternating order. Compare medians
   and run-to-run spread, p95/p99 frame times, >50 ms and >100 ms intervals, GPU busy
   time and correlated memory pressure. Require improvement larger than baseline
   variation and inspect helmet markings, armor, weapons and moving distant models.

## Capture only when gameplay is ready

The capture tool is optional support tooling, not a launcher startup requirement.
It uses the existing local PresentMon executable; nothing is downloaded or installed.
The operator must explicitly mark the warmed scene ready:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/measure-game.ps1 -Label lighter-A1 -Scene traversal -GameplayReady -Seconds 90
```

The five-second delay lets the player refocus the game. By default this records one
sample. `-Repeats 3` asks the operator to return to the same scene between captures.
`-ExtendedCounters` also attempts game-PID GPU memory and machine-wide disk throughput
and queue counters. Counters denied or unsupported by Windows remain null. Verify
that added collection overhead does not materially change results on the weakest PC.

PresentMon needs Windows ETW tracing access. If it exits with access denied, the
script stops and records an invalid capture; use documented tracing privileges or
an already available driver/game monitor. Do not change GameGuard or game protection.
The script uses a unique session name and attempts to stop only that session in its
cleanup path. Check cleanup output if the process had to be terminated.

## Read the results

Each capture saves a redacted settings/hardware context, raw frame CSV, memory CSV,
capture logs and either a summary or invalid-capture marker under `dist/benchmarks`.
No file uploads automatically. Review raw files before manually sharing them; native
tool stderr can contain information outside the launcher's redacted report.

Summary parsing accepts explicit PresentMon interval columns, chooses the swapchain
with the most rows, and rejects unknown or insufficient data. It does not identify
menus or alt-tab automatically. Review those portions before comparing; an explicit
gameplay flag is operator confirmation, not automatic scene validation.

FPS is 1000 divided by mean selected interval, not displayed or generated-frame FPS.
Percentiles use the nearest-rank rule. Raw dropped/generated rows remain available.
GPU busy timings, where available, help separate rendering cost from waiting but
are not a complete bottleneck diagnosis. Current VRAM process allocations are not
the same as capacity or adapter-wide physical residency. System memory counters and
disk counters are machine-wide; correlate them with frame spikes rather than
attributing all activity to the game.

Publish the reduced-texture choice only after current pack provenance and all mode
combinations pass, visuals are acceptable, and a valid affected-PC comparison shows
a useful result. Preserve Lighter textures as a rollback choice.
