# Team setup

1. Download [Clonedivers.exe](https://github.com/owendavidgoode/clonedivers/releases/latest/download/Clonedivers.exe).
   Close the old launcher before replacing it. Use version **1.5.0** or newer. Scope removal is automatic; Lighter textures remains optional.
2. Keep Helldivers 2 closed, choose Full or Lighter textures, and download/update the pack to **2026.09.13-r9**.
   r9 reuses the existing r8 game assets; keeping the same mode/profile requires no new pack files.
3. Choose **Helldivers**, **Clonedivers**, or **Commandodivers**, then launch through Steam.
   Everyone installs the pack locally to see and hear the same replacements.

## Delta Squad loadouts

In Commandodivers, use the **Brawny** body type and select your character's voice
explicitly. Avoid Random. Armor does not automatically select a voice. Other
players' voice selections determine which character replacements you hear locally.
The new dialogue and labels target English audio/text; some calls and exertions
retain the existing clone voices.

| Character | Voice slot | Body armor to unlock | Helmet |
| --- | --- | --- | --- |
| Sev | 1 | SC-30 Trailblazer Scout — Helldivers Mobilize page 7, 50 Medals | B-01 variant 1 |
| Fixer | 2 | CM-10 Clinician — Superstore, 250 Super Credits | B-01 variant 2 |
| Scorch | 3 | CE-35 Trench Engineer — Helldivers Mobilize page 3, 10 Medals | B-01 variant 3 |
| Boss | 4 | DP-11 Champion of the People — Helldivers Mobilize page 10, 100 Medals | B-01 variant 4 |

Only your chosen body is required. All four cost **250 Super Credits + 160 Medals**
for the items themselves; reaching later Warbond pages requires additional spending.
The four B-01 helmet variants are starter items. Shared underlying game models mean
some other helmets can also show Sev, and some armor pieces can share Scorch parts.

The full opening has been confirmed in game. The named voice slots are visible;
the audio replacements passed extraction/decoding checks. A complete multiplayer
voice check and the new starter helmet mapping still need team gameplay confirmation.

## If a selected Commando body still looks like a clone

Check **Body Type: Brawny** first, then re-equip the character's body armor from
the table above. These Delta body replacements require Brawny; Lean uses different
model resources. Selecting a named voice does not select the matching armor.

The published pack contains the Commando assets. A file/resource audit found
identical Commando armor files and all 126 armor resource winners with Lighter
textures on and off. That rules out an omitted armor file in the texture variant;
it does not replace an in-game check. If Brawny still fails, close the game, select
Commandodivers, and click **Check and repair** to verify/repair the installed files.
Record the exact body armor and whether its helmet is also affected if it persists.

## Camera

The AT-TE camera distance is unchanged. FOV is a personal game setting and is not
forced by the launcher or downloaded pack. A value of 75 improved framing on the
host PC; later local combat trials used 85. This is a preference, not a recommended
performance setting for every machine.

## GameGuard error 110 after closing the game

Allow GameGuard to finish closing before relaunching. If repeated attempts fail,
restart Windows before trying again. During our local tests, an old GameGuard
process survived game exit; rebooting cleared the launch failure. Do not repeatedly
launch another copy or change game protection settings.
