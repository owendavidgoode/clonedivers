# Clonedivers Clean Exit (HellForge code mod, prototype)

HELLDIVERS 2 build 25480438 crashes in its own shutdown after play (`helldivers2.exe+0x626296`, render task
pool). This client-only mod ends the process with exit code 0 at exactly that fault while the game is quitting,
and passes every other fault to HellForge's crash handling. It records each use in
`%LOCALAPPDATA%\Clonedivers\clean-exit.log` inside the session's environment.

Build and package with the HellForge CLI from a folder that has the SDK above it (`<folder>\app\sdk`,
`<folder>\tools\zig`), for example junctions to an installation's `app\sdk` and `tools`:

```powershell
HellForge.Cli build-mod --mod <workspace>\mods\clean-exit
HellForge.Cli package-mod --mod <workspace>\mods\clean-exit --out <dist>
```
