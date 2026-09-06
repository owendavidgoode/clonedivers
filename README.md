<p align="center"><img src="docs/icon.png" width="96" alt="Clonedivers helmet icon"></p>

# Clonedivers

**One button turns your Helldivers 2 Star Wars mods on or off. One button launches the game.**
That's the whole app. No installer, no accounts, no background service, nothing touches the game itself.

<p align="center">
  <a href="https://github.com/owendavidgoode/clonedivers/releases/latest/download/Clonedivers.exe"><b>⬇ Download Clonedivers.exe</b></a>
  &nbsp;·&nbsp; Windows 10/11 64-bit, Steam copy of Helldivers 2. Nothing else to install.
</p>

<p align="center">
  <img src="docs/screenshots/1-clones-on.png" width="380" alt="Clones on">
  <img src="docs/screenshots/2-clones-off.png" width="380" alt="Clones off">
</p>

## Quick start (friends, this is the part to read)

1. **Download** `Clonedivers.exe` from the link above. Put it anywhere — Desktop is fine.
2. **Run it.** Windows shows *"Windows protected your PC"* because the exe isn't code-signed.
   Click **More info → Run anyway**. You see this once. (It's a plain .NET app; source is right here.)
3. It finds Helldivers 2 through Steam by itself. If it can't, it asks you to pick the game folder.
   Not sure where that is? In Steam: right-click Helldivers 2 → **Manage → Browse local files**. That window is the folder.
4. **Install the pack** (next section). It ends with mod files sitting in the game's `data\` folder.
5. The big button now says **CLONES: ON**. Hit **LAUNCH HELLDIVERS 2**. Steam starts the game as normal.
6. Fancy a vanilla night? Close the game, click the big button so it reads **CLONES: OFF**, launch.
   Nothing is deleted — the files are parked in `Helldivers 2\mods_off\` until you flip it back.

**Golden rule: only flip the switch while the game is closed.** Helldivers 2 reads mods once, at startup.
Clonedivers refuses to toggle while `helldivers2.exe` is running and tells you why.

## The pack: Helldivers 2 → the Clone Wars

Clonedivers is only the switch. The conversion is a stack of community mods from Nexus Mods, all client-side
asset swaps. Every entry below was checked against its live Nexus page on **2026-09-05** (game patch 7.0.2).
The full per-mod audit, with dates and evidence, is in [docs/MODS.md](docs/MODS.md).

### Install once with HD2 Arsenal, then switch with Clonedivers

Almost every good mod here ships as an options package: dozens of files that all share the same name and must be
renumbered `patch_0, patch_1, patch_2…` with no gaps. Doing that by hand is miserable and one slip breaks the load.
So:

1. Install **[HD2 Arsenal](https://www.nexusmods.com/helldivers2/mods/4664)**, the mod manager these mods are built for
   (several of them no longer work with the older HD2ModManager). It handles numbering, options and conflicts.
2. Download each mod below from Nexus, add it to Arsenal, pick the **Republic** option wherever offered, and press deploy.
   The files land in `Helldivers 2\data\`. Let Arsenal be the only thing that installs or removes mods.
3. From then on, **Clonedivers** is the everyday switch: off for vanilla nights, on for the Republic. It moves exactly the
   files Arsenal deployed and nothing else. After you change your Arsenal setup, make sure Clonedivers says ON before playing.
4. If Clone Armory tanks your framerate, add `--use-d3d11` to the game's launch options
   (Steam → right-click Helldivers 2 → Properties → Launch Options). The mod's author and users recommend it.

Budget about **5 GB** of downloads, most of it Clone Armory and the droid army.

### Core stack (everyone installs these)

| Layer | Mod | What it does | Status on 7.0.2 |
|---|---|---|---|
| Armor | [Star Wars – Clone Armory](https://www.nexusmods.com/helldivers2/mods/13956) | **Every** armor → clone armor, plus backpacks, accessories, Republic decals. Install its Main, Backpacks, Accessories and Decal modules; **skip its SEAF and DC-15 modules** (better ones below). Pick Phase 1 or Phase 2 helmets and one legion. | Updated for 7.0 on 16 Aug. Beta, 2.8 GB, 1500+ files: wants 16 GB RAM and `--use-d3d11`. |
| Weapons | [Star Wars Clone Blasters](https://www.nexusmods.com/helldivers2/mods/6633) | Nearly every weapon model → DC-15A/S, DC-17, Z-6, bowcaster… | Last fixed 4 May, no breakage reports since 7.0. |
| Tracers | [Blue Overhaul – Laser Bolts](https://www.nexusmods.com/helldivers2/mods/4835) | Bullets → blaster bolts, plus turbolaser orbitals. **Without this the blasters still fire bullets.** | Updated 21 Aug. 846 endorsements. |
| Weapon sounds | [Star Wars PEW-PEW sounds](https://www.nexusmods.com/helldivers2/mods/4891) | All weapon fire/reload → blaster sounds. Use the *Republic* file. | Author active (replies 29 Aug); working per posts 2 Sep. |
| Your voice | [Full Clone Voice Conversion](https://www.nexusmods.com/helldivers2/mods/11826) | All four Helldiver voices → clone trooper lines. | Confirmed working 2 and 4 Sep. |
| Radio voices | [RiqCrow's sound & voice library](https://www.nexusmods.com/helldivers2/mods/633) | Pick the *Star Wars* modules: Mission Control, Democracy Officer, clone officers, SEAF squad audio. | Star Wars modules added 12–28 Aug. |
| Allies | [SEAF Clone NPCs 2.0](https://www.nexusmods.com/helldivers2/mods/5251) | The SEAF soldiers you rescue → clone troopers (choose legion). | Rebuilt 1 Sep for the 7.0 SEAF rework. Arsenal only. |
| Enemies | [Automaton → CIS Overhaul](https://www.nexusmods.com/helldivers2/mods/9115) | The whole Automaton faction → B1/B2 droids, MagnaGuards, AATs, MTTs, Vulture droids. | **At risk.** Crash-on-mission-load reports since 7.0. Use the 1.6 GB *May Update 2* file, turn off its *CIS Props* and *Dead Droids* toggles first. If it still crashes, disable just this one; the rest of the pack is unaffected. |
| Ship | [Venator over Super Destroyer](https://www.nexusmods.com/helldivers2/mods/6490) | Your Super Destroyer and the fleet → Venator-class. Republic livery. | Updated 4 Jun, 263 endorsements, no post-7.0 complaints. |
| Extraction | [LAAT Gunship over Pelican-1](https://www.nexusmods.com/helldivers2/mods/5778) | Pelican-1 → LAAT/i with Republic livery and Battlefront audio. | Updated 14 Aug. |
| Eagle | [Y-Wing over Eagle-1](https://www.nexusmods.com/helldivers2/mods/12381) | Eagle-1 → Clone Wars Y-Wing (or the [ARC-170](https://www.nexusmods.com/helldivers2/mods/1433) if you prefer). | Updated 4 Jun. |
| Vehicles | [AT-TE exosuits + LAAT/c](https://www.nexusmods.com/helldivers2/mods/6396) · [TX-130 FRV](https://www.nexusmods.com/helldivers2/mods/6015) | Exosuits → AT-TE, vehicle drops → LAAT/c carrier, the car → TX-130 Saber tank. | Updated 13 and 7 May. |
| Map and text | [Galactic Map Overhaul](https://www.nexusmods.com/helldivers2/mods/1489) | Planet names, faction icons, "Super Earth" → "The Republic", Automatons → Separatists. **Load this last** (highest number). Needs US English text. | Working after 7.0 per users 20 Aug, a few glitched icons. |

### Polish (optional, all small)

[Clone Trooper Ranks](https://www.nexusmods.com/helldivers2/mods/14612) (levels → GAR ranks) ·
[Clone Pilot Audio](https://www.nexusmods.com/helldivers2/mods/10667) (Eagle and Pelican pilots) ·
[Republic Commando stratagem beeps](https://www.nexusmods.com/helldivers2/mods/13942) ·
[All Invisible Capes](https://www.nexusmods.com/helldivers2/mods/11657) (clean clone look) ·
[GNK Hellbomb](https://www.nexusmods.com/helldivers2/mods/11333) ·
[All music replacement with Star Wars](https://www.nexusmods.com/helldivers2/mods/15612) (nearly every track; new on 29 Aug, so try it and drop it if anything goes silent).

### Lite pack (under 16 GB RAM, or if Clone Armory hurts your framerate)

Swap Clone Armory for **[Clone Trooper Phase II and I – Blood and Dirt](https://www.nexusmods.com/helldivers2/mods/14774)**
(one armor set, all legions, low RAM, updated 2 Sep) or the **[Shiny Clone Trooper Pack](https://www.nexusmods.com/helldivers2/mods/1248)**
(B-01 Tactical only, 150 MB, 458 endorsements). Skip the CIS Overhaul. Keep everything else. You still look and sound like a clone.

### Pick one per slot

These replace the same game files, so installing both wastes space at best and crashes at worst:

| Slot | Choose one | Why |
|---|---|---|
| Player armor | Clone Armory **or** Blood and Dirt **or** Shiny pack | same armor archives |
| Weapon models | Clone Blasters **or** Armory's *Movie Accurate DC-15s* module | same weapons; Blasters covers more of them |
| SEAF troopers | SEAF Clone NPCs 2.0 **or** Armory's *SEAF* module | same NPCs; Armory's module has open crash reports |
| Backpacks | Armory's *Backpacks* module **or** [Cody's Jetpack](https://www.nexusmods.com/helldivers2/mods/6735) | same packs |
| Super Destroyer | Venator 6490 only | see *Dropped* below |
| Voices | Full Clone Voice + RiqCrow **or** [Temuera Morrison Voice Overhaul](https://www.nexusmods.com/helldivers2/mods/12524) | 12524 covers player, Democracy Officer, Mission Control and Eagle-1 in one mod, from the actor's real lines |

### Dropped from the earlier list, and why

- **Ship Overhaul – Venator (12082):** broken by the 7.0.0 patch (ship rotated, invisible, missing front), reports 13 Aug, 27 Aug, 2 Sep, no author since May.
- **Arvis' Custom Venator (1261):** July crash-on-drop report, documented framerate halving, no post-7.0 confirmation. Venator 6490 does the same job.
- **Clone Trooper Music (7057):** untouched since July 2025, three cues only. The music replacement above covers everything.
- **NPC Voice Replacements (2205):** its author says the Republic Democracy Officer still speaks Imperial placeholder lines. RiqCrow's modules replace it.

Reference: the community's [Clone Wars Overhaul collection](https://www.nexusmods.com/games/helldivers2/collections/h2juj4)
(42 mods, 4.9 GB) is the closest thing to a one-click pack. It predates 7.0 and still lists the broken Ship Overhaul, so use
it as a checklist, not a button.

Mods are client-side only: you see your clones, your squad sees theirs. Everyone installs the pack to be in the same movie.

## What it does

- **ON** = your `*.patch_*` files sit in `Helldivers 2\data\`. **OFF** = the same files sit in `Helldivers 2\mods_off\`.
  The state is read from disk every time — nothing is stored, so it can't get out of sync.
- Moving is a rename on the same drive, so a multi-gigabyte pack flips instantly. Nothing is ever copied or deleted.
- **Launch** opens `steam://rungameid/553850`, exactly like pressing Play in Steam. The game needs Steam running anyway
  (Steamworks + GameGuard), so this is the reliable way in.
- Finds the game via Steam's registry key and `libraryfolders.vdf`, so second-drive libraries work.
  A manually chosen folder is remembered in `%APPDATA%\Clonedivers\config.json` and forgotten if it stops being valid.

## What it deliberately does not do

- It never injects into, hooks, reads, or otherwise touches the game process. It notices whether `helldivers2.exe`
  is running (to refuse a toggle) and that is all.
- No mod downloader, updater, Nexus login, per-mod toggles, patch-number management, or settings screen.
  All `*.patch_*` files move together. Want one mod off? Do that in Arsenal and redeploy.
- It doesn't edit any game file. Vanilla Helldivers 2 ships zero `*.patch_*` files, which is exactly why that pattern
  identifies mods safely.

## Gotchas

- **After a Helldivers 2 update, turn clones OFF until the mod pages say the mods work again.** Asset mods are tied to the
  game's archives. Stale ones crash on launch (error `0x44415441`) or show broken gear. Steam's own updater leaves your
  patch files alone, so Clonedivers will still happily say ON. Clone Armory and the CIS Overhaul are the ones most likely
  to need an update; check their *Posts* tabs.
- **SmartScreen / antivirus.** The exe is unsigned and self-contained (the .NET runtime is packed inside), which trips
  Defender's "unrecognized app" screen and occasionally a false positive. Build it yourself if that bothers you.
- **One installer only.** Arsenal and the older HD2ModManager both manage `data\`; HD2ModManager's *Purge* deletes every
  `*.patch_*` file in there, including ones Arsenal placed. Pick Arsenal and stick with it.
- **Steam "Verify integrity of game files"** does not remove mods — it only repairs vanilla files. A full reinstall does
  wipe `data\`. `mods_off\` is a sibling folder, so parked mods survive both.
- **Anti-cheat.** Modding is technically against the EULA and "at your own risk." Arrowhead has not banned for
  cosmetic client-side mods; anything that changes gameplay is a different story. Clonedivers only moves files.
- **"Game not found."** Pick the folder containing `data\` and `bin\helldivers2.exe`. Clicked one level too deep
  (into `data` or `bin`)? It works that out.
- **"Couldn't move … file".** Something has the file open — antivirus mid-scan, a download still finishing, or the game.
  The other files still moved; once it's free, flip off and on again and the straggler catches up.
- **"Helldivers 2 is running" but no game window.** The game crashed and left `helldivers2.exe` behind.
  End it in Task Manager (Ctrl+Shift+Esc), then toggle.
- **Steam closed when you hit Launch.** Steam opens first, then the game. The button says *STARTING VIA STEAM…* meanwhile.

## Build it yourself

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). From the repo root:

```bash
dotnet publish -c Release Clonedivers
```

That drops a single self-contained `dist\Clonedivers.exe` (~63 MB, no runtime install needed on the target PC).

Native tests (plain console app, no test framework, exit code 0 = pass):

```bash
dotnet run --project Clonedivers.Tests
```

They prove the matcher moves exactly the patch files and their companions, leaves base archives and a decoy named
`game.patch` alone, overwrites a stale (even read-only) collision in `mods_off\`, round-trips cleanly, keeps going
past a locked file and names it, and that `libraryfolders.vdf` `"path"` values are unescaped (`\\` → `\`).

Layout: [`Clonedivers/Program.cs`](Clonedivers/Program.cs) is the whole app (C#, .NET 8 WinForms, one file).
[`tools/make-icon.ps1`](tools/make-icon.ps1) regenerates the helmet icon. [`docs/MODS.md`](docs/MODS.md) is the mod audit.

## Preflight checklist

Run before shipping a new build, on a PC with the game installed. Use a dummy `test.patch_0` in `data\`
(no real mods needed) and delete it afterwards.

- [ ] App launches, finds the game with no prompt, shows **CLONES: ON**
- [ ] Toggle → file lands in `mods_off\`, `data\` has no `*.patch_*`, shows **CLONES: OFF**; toggle back
- [ ] **Launch** hands off to Steam
- [ ] With the game running, toggle is refused with a clear message
- [ ] With the game folder temporarily renamed, app shows **GAME NOT FOUND** and the picker recovers it (restore the folder)
- [ ] Renders cleanly at 100% and 150% display scaling

## License

MIT. Helldivers 2 is Arrowhead Game Studios / Sony Interactive Entertainment. Star Wars is Lucasfilm.
Fan utility, not affiliated with any of them.
