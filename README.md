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
4. **Put the mod files in.** Click **Open data folder**, then drop every mod file straight into it — no sub-folders.
   Mod files look like `9ba626afa44a3aa3.patch_0`, usually with matching `.patch_0.gpu_resources` and `.patch_0.stream` files.
   If Owen sent you a zip of the pack: unzip it and copy *everything inside* into `data\`.
5. The big button now says **CLONES: ON**. Hit **LAUNCH HELLDIVERS 2**. Steam starts the game as normal.
6. Fancy a vanilla night? Close the game, click the big button so it reads **CLONES: OFF**, launch.
   Nothing is deleted — the files are parked in `Helldivers 2\mods_off\` until you flip it back.

**Golden rule: only flip the switch while the game is closed.** Helldivers 2 reads mods once, at startup.
Clonedivers refuses to toggle while `helldivers2.exe` is running and tells you why.

## Building the Star Wars conversion

Clonedivers is only the switch. The conversion is a stack of community mods from Nexus Mods. These are the ones that
turn the game into the Clone Wars (all links checked on 2026-09-05; every one of these is a client-side asset swap):

| Layer | Mod | What it changes |
|---|---|---|
| Armor | [Star Wars – Clone Armory](https://www.nexusmods.com/helldivers2/mods/13956) | **Every** player armor set → clone trooper armor, plus backpacks, accessories and DC-15 weapon swaps. The backbone of the conversion. |
| Armor (alt) | [Shiny Clone Trooper Pack](https://www.nexusmods.com/helldivers2/mods/1248) | Just the B-01 Tactical set → Phase 1 / Phase 2 / ARF / BARC. Use if you want one clone set instead of all of them. |
| Weapons | [Star Wars Clone Blasters](https://www.nexusmods.com/helldivers2/mods/6633) | Nearly every weapon model → DC-15A/S, DC-17, Z-6 rotary, bowcaster and friends. Models only. |
| Weapon sounds | [Star Wars PEW-PEW sounds](https://www.nexusmods.com/helldivers2/mods/4891) | Firing and reload sounds for all weapons → blaster sounds (Republic or Imperial set). |
| Music | [Clone Trooper Music](https://www.nexusmods.com/helldivers2/mods/7057) | Hellpod drop, flag raise and extraction music → Republic Commando / Clone Wars tracks. |
| Your voice | [Full Clone Voice Conversion](https://www.nexusmods.com/helldivers2/mods/11826) | All four Helldiver voices → clone trooper lines. |
| Mission Control | [NPC Voice Replacements](https://www.nexusmods.com/helldivers2/mods/2205) | Mission Control and the Democracy Officer → Republic radio voice. |
| Ship | [Venator Super Destroyer](https://www.nexusmods.com/helldivers2/mods/1261) | Your Super Destroyer (and the ones in orbit) → Venator-class Star Destroyer. |
| Ship interior | [Ship Overhaul – Venator](https://www.nexusmods.com/helldivers2/mods/12082) | Bridge, screens, banners and thruster effects → Republic navy. Early release, expect rough edges. |
| Enemies | [Automaton → CIS Overhaul](https://www.nexusmods.com/helldivers2/mods/9115) | The whole Automaton faction → B1/B2 droids, MagnaGuards, AATs, MTTs, Vulture droids. |
| Allies | [SEAF Clone NPCs](https://www.nexusmods.com/helldivers2/mods/5251) | The SEAF soldiers you rescue → clone troopers. |
| Map and text | [Galactic Map Overhaul](https://www.nexusmods.com/helldivers2/mods/1489) | Planet names, faction icons and UI text → Republic vs Separatists. Must load *last* (highest patch number). |

**The one rule that trips everyone up: patch numbers.** Two mods that change the same game archive both ship a
`<hash>.patch_0`. Only one file can have that name, so the second mod's three files must be renamed to `.patch_1`,
the next to `.patch_2`, and so on with **no gaps** — the game stops loading at the first missing number. Renaming a
dozen triplets by hand is miserable, so:

- **Easiest:** install the pack once with a community mod manager such as
  [HD2 Mod Manager](https://github.com/teutinsa/Helldivers2ModManager) (it does the numbering and writes the files into `data\`),
  then use Clonedivers as the everyday on/off switch. They don't fight: both just move files in `data\`.
  One caveat — HD2MM's *Purge* deletes every `*.patch_*` in `data\`, including files you copied by hand.
- **Or:** have one person (Owen) build the numbered set once and share `data\`'s patch files as a zip. Everyone else
  just drops them in. Same files on every PC means the same game for everyone.
- Turn on **View → Show → File name extensions** in Explorer before renaming anything, or `x.patch_0` quietly becomes
  `x.patch_0.txt`.

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
  All `*.patch_*` files move together. Want one mod off? Take its files out of `data\` yourself.
- It doesn't edit any game file. Vanilla Helldivers 2 ships zero `*.patch_*` files, which is exactly why that pattern
  identifies mods safely.

## Gotchas

- **After a Helldivers 2 update, turn clones OFF until the mod authors post updated files.** Asset mods are tied to the
  game's archives. Stale ones crash on launch (error `0x44415441`) or show broken gear. Steam's own updater leaves your
  patch files alone, so Clonedivers will still happily say ON. Flip OFF, play, flip ON once the pack is updated.
- **SmartScreen / antivirus.** The exe is unsigned and self-contained (the .NET runtime is packed inside), which trips
  Defender's "unrecognized app" screen and occasionally a false positive. Build it yourself if that bothers you.
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
[`tools/make-icon.ps1`](tools/make-icon.ps1) regenerates the helmet icon.

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
