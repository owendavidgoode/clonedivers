# Faster Corpse Cleanup 1.0.6

Shortens how long enemy corpses remain. The Mod Options Menu provides an enable switch, a cleanup delay from 0 to 120 seconds, and Extra corpse cleanup. The default delay is 5 seconds; 120 selects original game timers. Zero requests the earliest cleanup after the game confirms a corpse. Death animations and wreck effects can last longer than the selected delay.

The menu provider owns saved choices. Existing option IDs, defaults, read-only INI defaults and legacy UMM selection priority are preserved. Turning Extra corpse cleanup off keeps timer changes and skips the extra corpse checks and removal requests. Bodies already removed cannot be restored.

## Targets and dependencies

The reviewed target is HellForge 0.3.0-beta, engine 0.1.41, SDK 15, commit 4a5373b88b8d001b58927edc27aa1b998578fe9f; game build 25480438. The native menu target is the separately supplied Bingus Vanilla Plus Megapack 0.2.0 image, DLL SHA256 6205b8c5699b9f07118b334cc4ee3e053a3023c9e973f94de1db18c68b31f02f. No provider is bundled. CLIENT_INTERFACE.md records the interface guards, provenance and limits.

The client uses Shared Callback Manager through client SDK 0.2.0. When the complete startup roster lists the manager, it uses bounded shared admission to the separate Shared Callback Manager 0.1.0 provider, ABI 1.0. A listed but unusable provider leaves cleanup inactive until restart. Proven provider absence permits one direct registration during initialization. There is no late direct fallback or second business callback.

Callbacks remain closed until event setup and startup/ready logging finish. The final atomic publication cannot overwrite a terminal stop. Pending readiness is accepted, and failed admission or cancellation retains callback storage for the session.

## Validation and known limits

The 1.0.6 preparation checks the emitted version banner, a fresh default eight-C Windows build, static DLL metadata, the complete package and a matching rebuild from its actual ZIP source. Only the two version strings and release metadata/prose change from accepted alpha.5. Comparison-proven unchanged cleanup, menu and startup evidence is reused; it is not represented as a fresh full-suite run. Later documentation-only revisions reuse the build and source-rebuild results after verifying that every source, header, build input and DLL byte is unchanged.

Historical alpha.5 evidence includes 30 owned Windows startup cases, the original eight cleanup test groups and menu transport51/binding34/mapping40 checks. Loader/provider/timer boundaries in the startup fixtures are owned substitutes; native menu acquisition is disabled in the owned startup/cleanup fixtures. Three later alpha.5 session logs record successful loading, menu registration, cleanup setup and natural exit0, using shared admission in two sessions and DIRECT in the third. Those observations belong to alpha.5, not this new binary. They include no frame-time or complete corpse-removal measurement, and each session reports sandboxed:false.

No 1.0.6 game, provider DLL or product DLL is executed during this preparation. In-game behavior, complete provider interoperability, persistence across changes, shutdown robustness, sandbox behavior, frame times and co-op are not qualified by these checks. This release preparation claims no confirmed lag or shutdown fix.

## Logs

The menu diagnostic banner now reports 1.0.6. Repeated unchanged diagnostic events are deliberately suppressed; dropped counts alone are not failures. The existing capture limits remain unchanged. DSP entries record startup/ready results and business callback milestones1/120. Numeric thread IDs are sampled observations, can be reused and do not measure live thread counts or frame time. The client adds no HF logging from SDK recovery timers.

## Installation and removal

With the game closed, preserve the current package and menu preferences, then import the complete ZIP through HellForge's mod manager. Enable one Faster Corpse Cleanup version. Keep separately supplied providers unchanged. Installation needs no shell commands or external compilation.

To remove the mod or roll back, close the game and use HellForge's mod manager to disable/remove this package or restore the earlier package. Provider-owned saved values are separate from the client package. Never swap DLLs during play. Do not use official game servers or players.

Complete corresponding source, build configuration and required legal notices are included. BUILDING.md and build.ps1 are developer-only reproduction material. Earlier packages and guides remain historical; no new guide/PDF is included. This preparation performs no installation or game launch and does not publish the package.
