# Asset conflict report

Data folder: `C:\Program Files (x86)\Steam\steamapps\common\Helldivers 2\data`  Â·  patch files parsed: 248 of 248  Â·  distinct assets: 12660  Â·  assets touched by more than one patch set: 531

## Cross-mod overrides (loser ==> winner, winner loads later)

| Loser ==> Winner | Assets | By type |
|---|---|---|
| Clone Armory - Main Mod  ==>  Clone Armory - Armor Accessories | 49 | unit 41, bones 8 |
| LAAT over Pelican-1  ==>  AT-TE exosuits + LAAT/c | 24 | particles 19, unit 2, animation 2, bones 1 |
| Clone Blasters  ==>  AT-TE exosuits + LAAT/c | 18 | unit 18 |
| Blue Overhaul  ==>  Custom Projectiles (blue bolts) | 15 | particles 15 |
| PEW-PEW Republic  ==>  RiqCrow - Republic cruiser + LAAT audio | 7 | wwise_stream 5, wwise_bank 1, AF32095C82F2B070 1 |
| Clone Blasters  ==>  TX-130 FRV | 6 | unit 6 |
| RiqCrow - Republic cruiser + LAAT audio  ==>  AT-TE exosuits + LAAT/c | 6 | wwise_bank 2, wwise_stream 2, AF32095C82F2B070 2 |
| Clone Armory - Backpacks  ==>  Clone Blasters | 5 | bones 3, unit 1, geometry_group 1 |
| Republic Commando stratagem beeps  ==>  RiqCrow - Republic cruiser + LAAT audio | 4 | wwise_bank 2, AF32095C82F2B070 2 |
| PEW-PEW Republic  ==>  AT-TE exosuits + LAAT/c | 2 | wwise_bank 1, AF32095C82F2B070 1 |
| Clone Armory - Main Mod  ==>  AT-TE exosuits + LAAT/c | 2 | wwise_bank 1, AF32095C82F2B070 1 |
| PEW-PEW Republic  ==>  RiqCrow - Probe droid Guard Dog | 2 | wwise_bank 1, AF32095C82F2B070 1 |
| Blue Overhaul  ==>  No Bullet Casings | 2 | particles 2 |
| Venator over Super Destroyer  ==>  AT-TE exosuits + LAAT/c | 2 | particles 2 |
| Clone Armory - Decal Sheets  ==>  Galactic Map Overhaul (last) | 2 | texture 2 |
| Blue Overhaul  ==>  Y-Wing over Eagle-1 | 2 | particles 2 |
| Clone Armory - Main Mod  ==>  Clone Armory - Backpacks | 1 | material 1 |
| Blue Overhaul  ==>  Automaton to CIS Overhaul | 1 | particles 1 |
| SEAF Clone NPCs 2.0  ==>  Automaton to CIS Overhaul | 1 | unit 1 |
| Clone Armory - Main Mod  ==>  SEAF Clone NPCs 2.0 | 1 | bones 1 |
| Clone Armory - Backpacks  ==>  SEAF Clone NPCs 2.0 | 1 | bones 1 |

## Within-mod layering (a mod's later folder overriding its earlier one; normally intended)

- AT-TE exosuits + LAAT/c : 26 asset(s)
- Automaton to CIS Overhaul : 7 asset(s)
- Blue Overhaul : 13 asset(s)
- Clone Armory - Armor Accessories : 2 asset(s)
- Clone Armory - Backpacks : 8 asset(s)
- Clone Armory - Main Mod : 48 asset(s)
- Clone Blasters : 246 asset(s)
- Custom Projectiles (blue bolts) : 2 asset(s)
- LAAT over Pelican-1 : 1 asset(s)
- PEW-PEW Republic : 4 asset(s)
- SEAF Clone NPCs 2.0 : 3 asset(s)
- Venator over Super Destroyer : 157 asset(s)
- Y-Wing over Eagle-1 : 3 asset(s)

## Every cross-mod conflict

| Asset | Type | Winner (index) | Overridden (index) |
|---|---|---|---|
| 0914072DBEC9D227 | AF32095C82F2B070 | RiqCrow - Republic cruiser + LAAT audio  [] (163) | PEW-PEW Republic  [AC-8 Autocannon/AT-ST Cannon] (144) |
| 2248B2D9A8433820 | AF32095C82F2B070 | RiqCrow - Republic cruiser + LAAT audio  [] (163) | Republic Commando stratagem beeps  [Republic Commando Stratagem Input Sounds] (160) |
| 4D19345D84E25BEB | AF32095C82F2B070 | RiqCrow - Republic cruiser + LAAT audio  [] (163) | Republic Commando stratagem beeps  [Republic Commando Stratagem Input Sounds] (160) |
| 55C14C4B194BCE0F | AF32095C82F2B070 | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | Clone Armory - Main Mod  [Clone Armory Opening Videos] (4); RiqCrow - Republic cruiser + LAAT audio  [] (163) |
| 9F67023D6191941C | AF32095C82F2B070 | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | PEW-PEW Republic  [Exosuits] (148) |
| D1549E61C0BA6864 | AF32095C82F2B070 | RiqCrow - Probe droid Guard Dog  [] (176) | PEW-PEW Republic  [AX-AR-23 Guard Dog/Droid Blaster Pistol] (147) |
| FF2B3DD55574455D | AF32095C82F2B070 | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | RiqCrow - Republic cruiser + LAAT audio  [] (163) |
| 511F84F9FB852D8D | animation | AT-TE exosuits + LAAT/c  [LAAT-C Mesh] (227) | LAAT over Pelican-1  [LAAT Mesh] (219) |
| A5BF83EF95CFD2DC | animation | AT-TE exosuits + LAAT/c  [LAAT-C Mesh] (227) | LAAT over Pelican-1  [LAAT Mesh] (219) |
| 293D8135EB95873B | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 4DBD74F49C8FFC13 | bones | Clone Blasters  [Primaries/Assault Rifles/MA5C] (28) | Clone Armory - Backpacks  [Backpack File] (11) |
| 57B443803961AD12 | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 75BE82ED8592A6B3 | bones | AT-TE exosuits + LAAT/c  [LAAT-C Mesh] (227) | LAAT over Pelican-1  [LAAT Mesh] (219) |
| 7A3637CD33A54E4C | bones | SEAF Clone NPCs 2.0  [Textures/Phase 2 (501st)] (179) | Clone Armory - Main Mod  [Extract Injured Clone Troopers] (3) |
| 7BE1E04A7738E673 | bones | Clone Blasters  [Invisible Mags] (83) | Clone Armory - Backpacks  [Backpack File] (11) |
| 82595B5D94C57343 | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 968211C0033DCE64 | bones | Clone Blasters  [Textures] (82) | Clone Armory - Backpacks  [Backpack File] (11) |
| 99A130858B3D8FF1 | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| A486599AB9B206EB | bones | SEAF Clone NPCs 2.0  [Textures/Phase 2 (501st)] (179) | Clone Armory - Backpacks  [Backpack File] (11) |
| AC712E206F7CB6C2 | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| B12095BE82FBA9F7 | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| EFFDFD2386AEF629 | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| F7D3407BA392B39A | bones | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 4DBD74F49C8FFC13 | geometry_group | Clone Blasters  [Primaries/Assault Rifles/MA5C] (28) | Clone Armory - Backpacks  [Backpack File] (11) |
| F4126BC5291167A0 | material | Clone Armory - Backpacks  [Clone Trooper Commander Hover Pack] (14) | Clone Armory - Main Mod  [Body Expansion] (7) |
| 00614B03CE8DB622 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 02F409B3CEB55AB3 | particles | Y-Wing over Eagle-1  [Pink Engines - No Flares] (224) | Blue Overhaul  [Gatling and Eagle strafe] (94) |
| 0534FF3653CC0CEE | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Kinetics] (86) |
| 18E78DC4E25D8382 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 19F9B09906F71EA8 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [PLAS Weapons] (90) |
| 19FF373A8283FC98 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 2206E95E7D8CFA92 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Kinetics] (86) |
| 22DF69CA7B5F14F2 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [R40K Hot-Shot] (85) |
| 2C4CE8AE256C865C | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 2D1F9CB560E84EAE | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Gatling and Eagle strafe] (94) |
| 3A7BD8823A3A0B08 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Suppressed Kinetics] (87) |
| 3F3FF771BE9ED5CB | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Kinetics] (86) |
| 492ADF796F7E19BE | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Kinetics] (86) |
| 55D2FD8EA3D8D263 | particles | Y-Wing over Eagle-1  [Pink Engines - No Flares] (224) | Blue Overhaul  [Gatling and Eagle strafe] (94) |
| 5747E23201659524 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [PLAS Weapons] (90) |
| 598D2DD2808A6073 | particles | No Bullet Casings  [] (107) | Blue Overhaul  [Bolts for Kinetics] (86) |
| 5D745B3ADC42D1CF | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 5FF27A19D57895DC | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 62E478ACF4347B9C | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Kinetics] (86) |
| 652212F970EED7B2 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 6A88D0876D7351F3 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 93057E24FDED6CEF | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | Venator over Super Destroyer  [Super Destroyer Thruster Remover] (217); LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| 96BC16BF5F0495A2 | particles | No Bullet Casings  [] (107) | Blue Overhaul  [Grenade Launcher VFX] (88) |
| AA3E9927A36A96FB | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Laser weapons] (89) |
| BB996C8A5CA8B459 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| BBC47A247F20C526 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | Venator over Super Destroyer  [Super Destroyer Thruster Remover] (217); LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| C377D16DB142D308 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Suppressed Kinetics] (87) |
| C4D817D0AE00BF8A | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| C612539E2A7E1B76 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Kinetics] (86) |
| C780484D81B8557C | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| C98696EC4DCE1B24 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| D00B347D173BCE78 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| D1B5AA1D16396CB6 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Bolts for Kinetics] (86) |
| D921BDA341D90105 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| DA67F2A6FC498861 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| F771E04EA56E75FF | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| F9591D5D721D46D2 | particles | AT-TE exosuits + LAAT/c  [Pelican 1 Thruster Remover] (236) | LAAT over Pelican-1  [Pelican 1 Thruster Remover] (222) |
| FB9E11B32C6C4496 | particles | Automaton to CIS Overhaul  [Bot Tank and FS Gatling Muzzle Flash Correction] (207) | Blue Overhaul  [VFX Corrections] (84) |
| FE7E1204782AB2D4 | particles | Custom Projectiles (blue bolts)  [Custom Projectile Colors/Blue] (105) | Blue Overhaul  [Laser weapons] (89) |
| 89411A48980460F3 | texture | Galactic Map Overhaul (last)  [icon-rep] (245) | Clone Armory - Decal Sheets  [Republic Decal Sheet - Base] (19) |
| 8AD0D88073B27AF0 | texture | Galactic Map Overhaul (last)  [icon-rep] (245) | Clone Armory - Decal Sheets  [Republic Decal Sheet - Base] (19) |
| 00BEF1DB5625F45F | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 0277FAD1282423A6 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 07D297C39E0E01F1 | unit | Automaton to CIS Overhaul  [No Bot Glows] (202) | SEAF Clone NPCs 2.0  [Light Remover] (177) |
| 1631FCB4875886A9 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 17C6E9BAFCD646D2 | unit | TX-130 FRV  [Invisible HMG Mag and Bullets] (239) | Clone Blasters  [Support Weapons/HMG] (77); Clone Blasters  [Support Weapons/AMR] (81) |
| 1935361D31ABD418 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 1C619ADF05C4532B | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 218C3935A336CCB1 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 293D8135EB95873B | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 3555958BE8D854B8 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 3A91F5A56DA3201E | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 416B2A1649C6CC83 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 5017A92B755E2E66 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Expansion] (7) |
| 55009B55BA812BEB | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 5A0ECF345ED340FC | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 5A1151257D1AA2C7 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 5CB6BDEF8EA0D927 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 6533A416930570A5 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 66CB402846EAAA31 | unit | TX-130 FRV  [Invisible HMG Mag and Bullets] (239) | Clone Blasters  [Support Weapons/MG43/DC15A] (76); Clone Blasters  [Support Weapons/HMG] (77); Clone Blasters  [Support Weapons/Maxigun] (79) |
| 66EA63F540E5C234 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 78D8A09A40C5F83C | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 7A9FA6630F086586 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 7FDD884ECD57231B | unit | AT-TE exosuits + LAAT/c  [AT-TE Patriot Mesh] (229) | Clone Blasters  [Primaries/Assault Rifles/Liberator/DC15A] (20); Clone Blasters  [Primaries/Assault Rifles/Liberator Penetrator/DC15A] (21); Clone Blasters  [Primaries/Assault Rifles/Liberator Concussive/DC15A] (22); Clone Blasters  [Primaries/Assault Rifles/Liberator Carbine/DC15A] (23); Clone Blasters  [Primaries/Assault Rifles/Tenderizer/DC15S] (24); Clone Blasters  [Primaries/Assault Rifles/Adjudicator] (25); Clone Blasters  [Primaries/Assault Rifles/Pacifier] (26); Clone Blasters  [Primaries/Assault Rifles/STA-52] (27); Clone Blasters  [Primaries/Assault Rifles/MA5C] (28); Clone Blasters  [Primaries/Assault Rifles/Supressor/DC15S Unfolded] (30); Clone Blasters  [Primaries/Marksman Rifles/Amendment] (31); Clone Blasters  [Primaries/Marksman Rifles/Constitution] (32); Clone Blasters  [Primaries/Marksman Rifles/Diligence] (34); Clone Blasters  [Primaries/Marksman Rifles/Diligence CS] (35); Clone Blasters  [Primaries/Marksman Rifles/Censor] (36); Clone Blasters  [Support Weapons/MG43/DC15A] (76); Clone Blasters  [Support Weapons/Stalwart/DC15A] (78); Clone Blasters  [Support Weapons/Maxigun] (79) |
| 82595B5D94C57343 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 8365609B35EF6672 | unit | AT-TE exosuits + LAAT/c  [Invisible Pelican Autocannon and Shells] (235) | LAAT over Pelican-1  [Invisible Pelican Autocannon and Shells] (221) |
| 848298B00B18A98B | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 8D45B0B3E445A954 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 9BA9F700137C6325 | unit | Clone Blasters  [Support Weapons/Maxigun] (79) | Clone Armory - Backpacks  [Backpack File] (11) |
| A0653148928FF59F | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Expansion] (7) |
| ACB12C0D0900FB34 | unit | AT-TE exosuits + LAAT/c  [Invisible Pelican Autocannon and Shells] (235) | LAAT over Pelican-1  [Invisible Pelican Autocannon and Shells] (221) |
| ADB23DA22E0F133B | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0); Clone Armory - Main Mod  [Body Expansion] (7) |
| B12095BE82FBA9F7 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| B78A3141317C5E5C | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Expansion] (7) |
| B7CD35E0EDC15CD7 | unit | TX-130 FRV  [Invisible HMG Mag and Bullets] (239) | Clone Blasters  [Support Weapons/HMG] (77) |
| B88E528020C56E92 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| B8BC4C84664DE2F9 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| B997DB242C78703E | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Expansion] (7) |
| BFE80D74C165D202 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| C80B17CF0ADC14A8 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Expansion] (7) |
| C8444826A3D8F7EE | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| C9E3D78911BA0586 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| CD8024BA25431EAA | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| D0BC7D17DAD302D9 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| E6099EFB8BE2C4E1 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| E7D8F74F4A647A38 | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| EFDAC0B6A7226FCD | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Expansion] (7) |
| F01CE988CA1138DC | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| F7D3407BA392B39A | unit | Clone Armory - Armor Accessories  [Accessories] (15) | Clone Armory - Main Mod  [Body Main] (0) |
| 0914072DBEC9D227 | wwise_bank | RiqCrow - Republic cruiser + LAAT audio  [] (163) | PEW-PEW Republic  [AC-8 Autocannon/AT-ST Cannon] (144) |
| 2248B2D9A8433820 | wwise_bank | RiqCrow - Republic cruiser + LAAT audio  [] (163) | Republic Commando stratagem beeps  [Republic Commando Stratagem Input Sounds] (160) |
| 4D19345D84E25BEB | wwise_bank | RiqCrow - Republic cruiser + LAAT audio  [] (163) | Republic Commando stratagem beeps  [Republic Commando Stratagem Input Sounds] (160) |
| 55C14C4B194BCE0F | wwise_bank | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | Clone Armory - Main Mod  [Clone Armory Opening Videos] (4); RiqCrow - Republic cruiser + LAAT audio  [] (163) |
| 9F67023D6191941C | wwise_bank | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | PEW-PEW Republic  [Exosuits] (148) |
| D1549E61C0BA6864 | wwise_bank | RiqCrow - Probe droid Guard Dog  [] (176) | PEW-PEW Republic  [AX-AR-23 Guard Dog/Droid Blaster Pistol] (147) |
| FF2B3DD55574455D | wwise_bank | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | RiqCrow - Republic cruiser + LAAT audio  [] (163) |
| 3DCD860C91C16A8A | wwise_stream | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | RiqCrow - Republic cruiser + LAAT audio  [] (163) |
| 5C5B7785A5D4CCC0 | wwise_stream | RiqCrow - Republic cruiser + LAAT audio  [] (163) | PEW-PEW Republic  [AC-8 Autocannon/AT-ST Cannon] (144) |
| 61B882CD7D300499 | wwise_stream | RiqCrow - Republic cruiser + LAAT audio  [] (163) | PEW-PEW Republic  [AC-8 Autocannon/AT-ST Cannon] (144) |
| 6C402F52D738DCF9 | wwise_stream | RiqCrow - Republic cruiser + LAAT audio  [] (163) | PEW-PEW Republic  [AC-8 Autocannon/AT-ST Cannon] (144) |
| 916ECDEF59212D38 | wwise_stream | RiqCrow - Republic cruiser + LAAT audio  [] (163) | PEW-PEW Republic  [AC-8 Autocannon/AT-ST Cannon] (144) |
| 97051915275CDC47 | wwise_stream | AT-TE exosuits + LAAT/c  [LAAT and AT-TE EXO Audio] (226) | RiqCrow - Republic cruiser + LAAT audio  [] (163) |
| B46902688F0C679D | wwise_stream | RiqCrow - Republic cruiser + LAAT audio  [] (163) | PEW-PEW Republic  [AC-8 Autocannon/AT-ST Cannon] (144) |
