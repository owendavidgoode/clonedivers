# Clonedivers Clean Exit (`clonedivers.clean-exit` `0.2.1`)

Client-only (`gameplay: false`, SDK 16, Helldivers 2 build `25480438`) shutdown helper that suppresses Stingray teardown faults and stalls so quitting exits cleanly with code `0` while keeping real in-play crashes visible to HellForge crash triage:

1. **Teardown Fault Suppression (`+0x626296`):** Ends the process with exit code `0` when build `25480438` faults in its render task pool (`helldivers2.exe+0x626296`) after native `stingray.Application.quit`, after accepted `WM_CLOSE` teardown (`idle >= 150 ms`), or after top-level window destruction (`!IsWindow`), passing every non-quit fault through to HellForge's crash handler.
2. **In-Game Esc Menu & `WM_CLOSE` Teardown Detection (`0.2.1`):** Tracks actual `HWND` destruction (`!IsWindow` across previously shown top-level unowned windows, so minimized or `SW_HIDE` hidden windows never trigger false exits) with a `1500 ms` loop-idle watchdog, and installs thread-local `WH_CALLWNDPROC` + `WH_GETMESSAGE` (`PM_REMOVE`) `WM_CLOSE` hooks gated on `enabled != 0` and top-level visible unowned game windows (`GetAncestor(hwnd, GA_ROOT) == hwnd`). A `WM_CLOSE` request requires the game loop to stop (or the window to be destroyed) and is automatically disarmed if gameplay `Tick()` continues (`>= 3` frames) or postponed while an owned confirmation popup is open. An `inplayFaultPending` latch cleared only on normal `Tick()` prevents real crashes from being masked as clean exits while ignoring caught SEH probes.
3. **Session Logging:** Appends each clean-exit event to `%LOCALAPPDATA%\Clonedivers\clean-exit.log` inside the session environment.

Build and package with the HellForge CLI from a workspace layout (`<workspace>\app\sdk`, `<workspace>\tools\zig`, `<workspace>\mods\clean-exit`):

```powershell
HellForge.Cli build-mod --mod <workspace>\mods\clean-exit
HellForge.Cli package-mod --mod <workspace>\mods\clean-exit --out <dist>
```
