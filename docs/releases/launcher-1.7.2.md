# EmpireDivers launcher icon and installation QA

Launcher **1.7.2** replaces the EmpireDivers mode-card helmet with an original
stormtrooper vector mark: a broad dome, continuous brow, angled lenses, recessed
frown and flared cheek tubes. It remains sharp at different display scales and
has a muted disabled state. The other mode icons are unchanged.

The pack remains **2026.09.27-r19**. Both Empire cape models have zero drawable
indices in their final overrides. A new archive audit checks actual resource
precedence, duplicate resource entries, companion bounds, every mesh draw group
and every layout's index counts across eight Empire selections (two texture
profiles, with droids and Covenant independently enabled/disabled).

Local installation QA upgraded r16 to r19 using the production installer with
Empire, full textures, droids and Covenant enabled. It downloaded 19 files
(392.4 MiB), retained the replaced files, and freshly hashed all 1,027 selected
files. A subsequent repair plan was a no-op. Settings and the previous installation
receipt were backed up before the update. The public feed override remains unset.

Validation artifacts:

- [Cape/load-order audit](launcher-1.7.2-capes.json)
- [Verified local installation](launcher-1.7.2-install.json)
- [Enabled/disabled icon previews](launcher-1.7.2-icons.png)
- [Published feeds, update discovery and executable verification](launcher-1.7.2-verification.json)
- `tools/Empire.Qa` uses native mode selection and the production installer.
- `tools/audit-empire-capes.py` independently reads the selected archives.
- `tools/Launcher.Preview --icons` renders enabled/disabled icons at 32–164 pixels.

The native launcher test suite and both full profile hash checks passed. All three
public feed URLs advertise 1.7.2, including successful update discovery using the
original 1.6.3 parser. The downloaded executable matches the published SHA-256;
the local launcher was replaced with that verified download after backing up 1.7.1.

These are installer, asset and visual-preview checks. Gameplay was not launched
for this QA pass; cape cloth behavior, ship presentation and voice timing still
need in-game observation. The r19 voice pack remains partial, and some donor
command lines do not preserve HD2's precise tactical wording. The requested LEGO
beach stormtrooper is not included; no compatible HD2 asset has been found.

The [r19 release notes](pack-2026.09.27-r19.md) retain source credits and the full
content/validation limitations. Clonedivers retains its own voices and Venator
opening. Empire retains the SAI sound and blaster bolts.
