> Integrated source status: Windows1.9.8, Linux0.6.0-preview.8 and bridge0.3.7
> have a published launcher/source prerelease. Linux gameplay/account capture and
> separate-PC multiplayer remain unqualified. The owner manually disabled host
> mod sharing and the Windows launcher now shows Ready to launch. See FORGE.md
> for the current setup and update policy. The dated sections below preserve the
> earlier implementation checkpoints; private/unpublished statements there do
> not describe the current launcher/source release.
# Linux Forge integration — private work in progress

The published Linux0.5.0 application is the existing Steam/Proton mod launcher.
This branch is preparing Forge support. No Forge Linux package is qualified or
published. Cross-compiling and fixture tests do not establish Proton gameplay,
account capture or Windows/Linux multiplayer compatibility.

## Keep one official namespace alive

The pinned HellForge0.3.0-beta Linux scripts are the supported isolation path.
An external launcher must invoke `app/linux/hellforge-linux.sh` with the Windows
program to run. The script establishes the Steam bridge and network namespace;
the Windows program and official CLI run inside that same namespace. Do not call
Wine/Proton directly or set `HELLFORGE_LINUX_NETNS` from a native launcher.

Starting a fresh script invocation for each status poll or Stop command is wrong:
`hellforge-offline.sh` refuses when that prefix's wineserver is already running.
Stopping the wrapper process or killing its tree would also bypass the session
runner's cleanup. Use a persistent coordinator inside the official namespace.

`Clonedivers.ForgeBridge` is that coordinator, named
`HellForge.Launcher.ClonediversBridge.exe` so the official `hf-inner.sh` process
lifetime pattern keeps its Steam forwarders alive. The Windows bridge checks the
namespace marker, validates a separate pinned installation, and proxies an
allowlisted JSON stdin/stdout protocol to its installed official CLI. It exposes
no network listener. The native Linux client will own these inherited pipes.
The bridge is outside HellForge's protected app/game folders in the prefix C:.

The native integration must preserve script/environment discovery, a dedicated
prefix, exact-case Linux paths, link rejection, architecture checks and bounded
diagnostics. Preflight output before the handshake is script diagnostics, not a
CLI JSON response. The handshake is `{"bridge":"clonedivers-forge","format":1}`.
Requests are `{"id":1,"args":["status"]}`; replies contain the same id, `ok`, and
either the official `result` or an `error`. Only one request runs at a time.

The bridge accepts account-default launches, normal mod/options commands and
read-only status/profiles/logs. It rejects template creation, profile resets,
sandbox/game-options changes, arbitrary executables, other install roots and
non-C: import paths. Mutations require the pinned engine0.1.41/SDK15/build25480438,
official `linux-netns` readiness and sandbox ON. A running session blocks changes.
The official CLI retains game-copy/ownership/build/mod/runner checks.

An acknowledged launch's run folder is tracked. Explicit Stop and frontend EOF
ask the official CLI to stop **that run** and wait. A preexisting session or a
replacement run is never stopped. The bridge does not kill game/Wine descendants.
The native frontend must display a close confirmation and await cleanup before
closing pipes. Unexpected bridge failure still needs real Proton recovery tests.

## Remaining work before a player trial

- Native desktop/terminal behavior tests and Linux runtime qualification. The
  `--forge --gui` Avalonia window now offers Regular/Clones/Empire, one-way import,
  verified appearance preparation, Launch/End session and an explicit Steam
  fallback. The old default Steam entry remains preserved until rollout review.
  `--forge --help` describes status/mods/account terminal diagnostics; terminal
  player mutations/launch are intentionally not exposed yet. Companion assets
  are required; standalone binaries do not imply a complete Forge installation.
- Package/build verification of the Windows helper; versioned, checked installation
  outside protected folders; no owner profiles/seal keys/captures in packages.
- Shared preset/appearance verification, exact options and author-only dependency
  acquisition. Proxying the CLI alone does not provision the full experience.
- Actual Linux qualification of account status and one-way import through the
  official backend. The bridge now reuses the exact Windows account/client/pack/
  preset sources, with shared models and the pinned r19 catalog. Its player
  dispatcher exposes account-status/import and selected-mode preparation/status.
  Launch always calls EnsureAccount then the fully verified account-only play
  path; no direct CLI shortcut skips those checks. Source tests cover missing
  imports, no profile creation/reset/lab fallback, incomplete presets and active
  staged sessions. Linux account capture is still not qualified; do not copy
  someone else's Windows sealed account, fabricate progression, weaken seal
  verification or offer an unlock-all fallback. No capture/security RPC exists.
- Actual Linux namespace/Steam ownership/session/End-session tests on a Linux PC,
  plus intended squad-size and Windows/Linux joins/rejoins on separate PCs.

### Additional wrapper lifetime blocker found in source review

Pinned `hf-inner.sh` uses `pgrep -u ... -f` with
`HellForge\.(Launcher|Session|Cli)|LocalBackend|helldivers2`. In external-launcher
mode, its own argv contains the Windows CLI/bridge path matching that pattern.
Its parent `hellforge-linux.sh` invocation can match too. `pgrep` excludes itself,
not all ancestor shell processes. Thus the loop can keep seeing the wrapper
after the Windows program/session has exited, retaining forwarders indefinitely.
This is a static reproduction from the exact pattern/argv, not an actual Linux
game test. A staged minimal fix now excludes the wrapper's ancestor chain and
retains real Windows children. Eight Bash stand-in assertions pass under Git
Bash; this is separate from Linux/Proton cleanup qualification. No installed
official scripts were changed.

`tools/stage-hellforge-linux.ps1` reproducibly stages source scripts plus the
lifetime-only adjustment outside installed Forge/Steam. It requires exact hashes
from `tools/hellforge-linux/upstream-pin.json`; all five originals were compared
against the installed0.3.0-beta scripts and match. Source checkout commit is
`b1aef771474710eab5461d360444cd80a809c70c`, distinct from the installed application's
release metadata4a5373b. Bundle source/COPYING retain the upstream GPL-3.0 license.
The network namespace, Steam bridge and security logic remain the original code.
The launcher-owned staging cache is separate from protected app/game folders.

`LinuxForgePlan` verifies bridge/script hashes, requires a dedicated C: prefix
outside Steam, maps paths to literal Windows CLI arguments, and atomically copies
only launcher-owned resources into a versioned cache. Existing mismatched cache
files are refused. `LinuxForgeTransport` owns one wrapper and stdin/stdout
connection, accepts the exact handshake, serializes correlated requests, bounds
diagnostics and output, waits for authoritative results after a request is sent,
and poisons an ambiguous stream to prevent blind mutation retries. Closing sends
EOF for bridge-side official cleanup and waits; it never kills game/Wine trees.
The frontend must confirm/await its pending commands and End session before close.

The pinned upstream `docs/linux.md` explicitly describes experimental support,
mission-card/ship-movement crashes and a reported unsuccessful Linux/Windows co-op
test. Flatpak/Snap/Steam Deck and the ownership probe are also listed as untested.
Keep these separate from the published Linux launcher's Steam-workflow evidence.
Do not silently upgrade HellForge or disable its isolation to hide these gaps.

## Local fixture verification

`dotnet run --project Clonedivers.ForgeBridge.Tests -c Release` checks command
restrictions, persistent launch/status/stop routing, owned-run cleanup on EOF,
preexisting-session preservation, pinned isolation, malformed request recovery
and input bounds, as well as native plan/hash/path/protocol behavior.40 assertions
pass. The player-dispatcher tests add five further checks (45 total so far).
These tests inject stand-ins and never run a game. The Linux Avalonia project
cross-build also passes with zero warnings/errors on the Windows build host.

The native controller adds ten checks (55 total): separate Forge preferences,
invalid-option preservation, retained End session after a post-launch poll error,
failed-stop recovery, owned-session cleanup, foreign-session preservation,
readiness gating and rejection of changed/outside-prefix appearance plans before
downloads. Windows-host Avalonia preview visually matches the three-card layout
and closes normally; it never loads/saves preferences or connects to Forge.
This is GUI layout evidence, not a Linux runtime or gameplay test.

Bridge0.3.0 adds the fixed `open-manager` action: the native Setup & mods button
opens the installed official manager inside the existing namespace when connected.
It accepts no program/path arguments and refuses active sessions. Source tests
also cover exact script-set metadata and link rejection;60 assertions pass on the
Windows host. Two actual symlink cases are explicitly skipped there because
Windows refused unprivileged fixture creation; the staged Ubuntu fixture workflow
must run them. No OS permission/security change was attempted.

`forge-readiness.yml` stages read-only CI for those fixtures, the shell stand-ins,
native compilation/help and Windows bridge cross-publish. It has not been pushed
or executed remotely and performs no game/account/multiplayer/publication test.
Compact running and error previews were inspected on Windows: End session stays
visible, setup/mode/account mutations lock during play, incomplete presets disable
Launch, accessible card names are meaningful, and the Forge palette keeps the
blue/orange colors independently of the OS accent. All previews closed normally.

## Preview2 review candidate — November4 17:45 heartbeat

Forge-edition0.6.0-preview.2 now rejects the legacy Steam release channel even
inside --steam fallback. The early policy returns a separate-channel notice before
reading/writing old release caches or sending HTTP, and release-page actions reject
that cohort. Ordinary0.5 Steam-edition update behavior is preserved. Six focused
Windows-host fixtures passed, including the actual compiled Forge-edition default,
forced checks/cache preservation, legacy behavior and blocked browser routing.
The staged Forge CI workflow includes these checks; it has not run remotely.

Bridge0.3.1 includes the shared read-only private-mod sharing guard from Windows1.9.2.
The native companion requires version0.3.1 and the package binds its exact SHA256.
The helper --help smoke and Linux x64 cross-publish passed. No actual Linux/Proton,
account capture/import, mission or multiplayer qualification is implied. Both
native/source guard files and pinned isolation remain in place; no installed
HellForge scripts, account data or networking settings were modified.

Current review-r3 stages Windows1.9.2, native0.6.0-preview.2 and bridge0.3.1 together,
with author receipts, required sharing-OFF metadata and unchanged pinned12-mod
cohort. Each archive was extracted and checked against its payload manifest. The
separate channel proposal is unpublished and manual-only; author acquisition/local
repair permissions and actual runtime acceptance remain separate unresolved gates.
