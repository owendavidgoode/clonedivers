# Launcher 1.7.3

Launcher **1.7.3** prepares missing saved defaults for the unchanged First Person
Perspective v2.9.14 archive included with pack r20. After a successful active pack
installation, and before Steam launch, it verifies the installed camera archive's
exact SHA-256, verifies the active ModOptions and Bingus loader archives, and seeds
`firstperson.enabled=false` and `firstperson.bridge2=1`
only when those choices are absent. Existing choices and other options are kept;
the options menu's valid backup is recovered when the primary file is missing or
empty. Unreadable or malformed primary files stop launch for review. Vanilla and
packs without this exact FPV archive leave these options alone.

The values file is `%LOCALAPPDATA%/CowboyBingus/Helldivers2/Logs/ModOptionsMenu.values`,
matching the mod author's path. If Steam was started with a different environment,
check the game's actual loader log directory before using FPV; start Steam and
the launcher normally from the same Windows account. Codex may provide a separate
package-local environment.

With the shipped loader and ModOptions addons loaded, FPV remains disabled for a
fresh player until they choose to enable it. **In each
new game session, before enabling FPV, set Engine camera bridge to On and APPLY,
then Off and APPLY while FPV is still disabled.** The author's startup code does
not apply the saved bridge choice to the engine. A saved `bridge2=1` alone does
not prove that the engine bridge is off. The FirstPerson archive and code are
unchanged; mission, ADS and AT-TE camera acceptance remain separate gameplay checks.

Ambiguous, malformed, linked, oversized, locked or concurrently edited options
stop launch with the file path and corrective action. Writes are bounded and
atomic. A late replacement race preserves the competing saved file in
`.clonedivers-seed-backup` and blocks further launch until those choices are
reconciled and the seed backup is removed.
