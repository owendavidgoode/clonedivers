# Private squad acceptance — November4,2026

**Multiplayer is not yet tested on separate PCs.** Solo startup and the owner's
D11 gameplay report do not establish a two-, three- or four-player pass.

Use HellForge 0.3.0-beta, engine 0.1.41, game 25480438 on every PC. Each player owns
HD2, signs into their own Steam account and imports their own account locally.
Keep the WFP block and sandbox enabled. Do not share account captures, seal keys,
profiles or save files. Progress imports only from HD2 into Forge; Forge rewards
stay local. The official Steam game remains a separate launch path.

Before joining, compare the complete12-mod preset, exact private Heavy/Squad/mine
fix versions, load order and options, including Targeting Marker OFF. Begin with
Republic/Delta/full+CIS on each PC. The gameplay code is75401ed0; that short code
alone is insufficient to establish matching cosmetic/data-pack options. Keep
Vanilla Plus enabled and the separate Beacon Fix, alternative Heavy spawner,
roster replacements and Goliath disabled. Use reviewed matching local installs;
automatic distribution/trust of these private code mods has not been qualified.

The owner should run these checks with their intended players; no invitations or
friend messages were sent as part of overnight QA.

| Step | Evidence to record | Pass condition |
| --- | --- | --- |
| Two PCs | Versions, preset/options, host/guest run IDs | Steam friends/in-game invite reaches the same ship with both sandboxed |
| Third and fourth joins | Join order, each client's co-op log and disconnect messages | Existing players remain connected and every intended player joins |
| Mission drop | Planet, mission, difficulty, each loadout | All players finish loading the same mission without a crash |
| SEAF and AI | Call-in type/count, dog/sentry behavior | One delivery per Squad call-in, passengers function, Heavy waves continue, no unwanted yellow targeting selector |
| Extraction | Objective result and every client's debrief | Intended players extract and see a coherent mission result |
| Local persistence | XP/currency before and after, legitimate spend, next launch | Earned rewards and a chosen spend persist on each player's own Forge character without another account import |
| Rejoin and host change | Disconnect/rejoin order, second host's run IDs | Rejoin works; repeat a short mission with a different host |
| Shutdown | End session and result.json on each PC | Runner/backend/broker stop and copy cleanup completes; next launch succeeds |

At **09:16 UTC November4**, upstream [issue 202](https://github.com/SkyeShade/HellForge/issues/202)
is still open: the report describes a third player failing to join and the second
player being disconnected. An [upstream comment](https://github.com/SkyeShade/HellForge/issues/202#issuecomment-6564764916)
points to a future0.4.0 fix. The [published release](https://github.com/SkyeShade/HellForge/releases/tag/v0.3.0-beta)
is still0.3.0-beta (October26), engine 0.1.41/SDK15. No update or isolation change
was applied. Do not use the comment's sandbox-off workaround. If the third join
fails, retain the logs and record that acceptance failure; do not call the squad
build certified. Two-player success would qualify only two players.

Keep qualification explicit: **downloaded → imported → initialized →
behavior-tested → mission-completed → multiplayer-tested**. Current evidence
reaches owner-reported solo D11 behavior; multiplayer, extraction and local
mission reward persistence remain pending.
