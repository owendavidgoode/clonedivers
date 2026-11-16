# HellForge Squad Acceptance Checklist

**Multiplayer is not yet tested on separate PCs.** Solo startup and the owner's
D11 gameplay report do not establish a two-, three- or four-player pass.

Use **HellForge `0.4.0-beta`**, **engine `0.1.47`**, **SDK `16`**, **game `25480438`**
on every PC. Each player owns HD2, signs into their own Steam account and imports
their own account locally. Keep the network block (`WFP`/`firewall` on Windows,
`linux-netns` via `pasta` from `passt` on Linux) and sandbox enabled. Do not share
account captures, seal keys, profiles or save files. Progress imports only from HD2
into Forge; Forge rewards stay local. The official Steam game remains a separate
launch path.

Before joining, compare the pinned `build25480438-r3` preset (3 bundled Forge mods
+ 7 required gameplay downloads; optional client-only `Clonedivers Clean Exit 0.2.0`
has `gameplay: false` and is excluded from HellForge co-op matching so friends may
keep or disable it), load order, options (including Guard Dogs `Targeting Marker`
OFF), and the **Squad modlist** code shown in **Session details**. Keep the
retired standalone `mine-boundary 0.1.1` fix disabled (`engine 0.1.47` includes
its bounds fix). Begin with matching appearances (`Clones r19` Full/Lighter and
options, or live `Empire r45-v4` Full) on each PC; the short HellForge modlist
code alone does not enforce matching cosmetic/data-pack options. Keep Vanilla Plus
enabled and Reinforcement Beacons Fixed, alternative Heavy spawners, conflicting
roster replacements and XS1 Goliath disabled. Each player acquires their own
artifacts through setup; automatic host mod sharing stays OFF.

The owner should run these checks with their intended players; no invitations or
friend messages are sent automatically during development QA.

| Step | Evidence to record | Pass condition |
| --- | --- | --- |
| Two PCs | Versions, preset/options, host/guest run IDs | Steam friends/in-game invite reaches the same ship with both sandboxed |
| Third and fourth joins | Join order, each client's co-op log and disconnect messages | Existing players remain connected and every intended player joins |
| Mission drop | Planet, mission, difficulty, each loadout | All players finish loading the same mission without a crash |
| SEAF and AI | Call-in type/count, dog/sentry behavior | One delivery per Squad call-in, passengers function, Heavy waves continue, no unwanted yellow targeting selector |
| Extraction | Objective result and every client's debrief | Intended players extract and see a coherent mission result |
| Local persistence | XP/currency before and after, legitimate spend, next launch | Earned rewards and a chosen spend persist on each player's own Forge character without another account import |
| Rejoin and host change | Disconnect/rejoin order, second host's run IDs | Rejoin works; repeat a short mission with a different host |
| Shutdown | End session and `result.json` on each PC | Runner/backend/broker stop and copy cleanup completes; next launch succeeds |

Upstream [issue 202](https://github.com/SkyeShade/HellForge/issues/202) (third-player
join failure / second-player disconnect on `0.3.0-beta`) was addressed upstream in
**HellForge `0.4.0-beta`** alongside Linux relay/namespace support. Never disable
the sandbox or network block as a workaround. Even with `0.4.0-beta`, actual 2-,
3-, and 4-player cross-PC Windows/Linux squad joins, mission completion, and
reward persistence remain unverified until recorded above; two-player success
qualifies only two players.

Keep qualification explicit: **downloaded → imported → initialized →
behavior-tested → mission-completed → multiplayer-tested**. Current evidence
reaches owner-reported solo D11 behavior on `r45-v4` and `r19`; multiplayer,
extraction and local mission reward persistence remain pending.
