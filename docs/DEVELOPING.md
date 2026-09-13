# Development

Current state: [STATUS.md](STATUS.md). Team instructions: [TEAM-SETUP.md](TEAM-SETUP.md).
Release workflow: [PUBLISHING.md](PUBLISHING.md). Older preflight details are
[archived](archive/2026-09-12-session/DEVELOPING.md).

The Windows launcher is .NET 8 WinForms. `Clonedivers/Program.cs` implements pack
verification, recovery and the main UI. `Clonedivers/LauncherModes.cs` provides the
three mode controls. The native console test project compiles those same sources.

```powershell
./dist/dotnet-sdk/dotnet.exe run --project Clonedivers.Tests
./dist/dotnet-sdk/dotnet.exe publish Clonedivers -c Release -p:PublishDir=<separate-build-folder>
./dist/dotnet-sdk/dotnet.exe run --project tools/Launcher.Preview -- --render <preview.png>
```

The preview uses the actual controls without writing settings, installing mods or
launching the game. Current UI: three helmet cards, one launch button, and the optional
Lighter textures toggle. Scope removal is an unconditional part of the pack.

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
