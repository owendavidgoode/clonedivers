# Development

Current state: [STATUS.md](STATUS.md). Team instructions: [TEAM-SETUP.md](TEAM-SETUP.md).
Release workflow: [PUBLISHING.md](PUBLISHING.md). Older preflight details are
[archived](archive/2026-09-12-session/DEVELOPING.md).

The launcher targets **.NET 8** with a shared **Avalonia 11** dark Star Wars UI across
Windows (`Clonedivers`, `net8.0-windows` with `--classic` WinForms and `--steam` recovery
fallbacks retained) and Linux (`Clonedivers.Linux` + `Clonedivers.ForgeBridge` native
controller `HellForge.Launcher.ClonediversBridge`). Embedded catalogs live under
`Clonedivers/Resources/` (`hellforge-empire-r53.json`, `hellforge-clones-r24.json`, and
`forge-gameplay-build25480438-r13.json`), and project-owned HellForge SDK-16 C11 mods live
under `forge-mods/`. See [Linux setup and checks](LINUX.md) and [mod coverage](FORGE-MOD-COVERAGE.md).

```powershell
dotnet run --project Clonedivers.Tests -c Release
dotnet run --project Clonedivers.Linux.Tests -c Release
dotnet run --project Clonedivers.ForgeBridge.Tests -c Release
dotnet publish Clonedivers/Clonedivers.csproj -c Release -p:PublishDir=<separate-build-folder>
```

Preview and UIA smoke workflows render and exercise the actual launcher controls in isolated
temp directories without touching real `%APPDATA%`, `D:\HellForge`, or launching `helldivers2.exe`.
Both Clones and Empire offer the 5-tier texture matrix (`Full` down to `256px + Decimated Meshes`),
3 Enemy Skins (`All CIS Droids`, `Mixed Bots + CIS`, `Native Automatons`), and 3-way Covenant + Squids.

Before shipping, check version metadata, preview layout, keyboard navigation, mode
isolation, mode changes from parked installs, interrupted-install recovery and download
hashes. Retain the game-running refusal and existing path/file safety tests. Use the
playtest checklist for behavior the native suite cannot establish.

## Investigation discipline

- Establish a working baseline and record game build, mode, options and FOV.
- Change one variable per gameplay test. Distinguish initialization failures from
  decoder crashes using the observed module/logs.
- Inspect the known-working format and smallest viable sample before expensive encoding.
- State whether a claim is source-supported, structurally verified, or confirmed in game.
- Treat FOV and physical camera distance as separate controls.
- Time-box unsupported engine investigations; record the missing capability before
  proposing an alternate design. Respect accepted decisions instead of reopening them.
- Use normal launch/close actions. Stop UI control when the user interrupts it; do not
  retry identical stale UI targets. Continue independent file work where possible.
- Keep player-facing help concise and free of spoilers. Technical notes belong here.
