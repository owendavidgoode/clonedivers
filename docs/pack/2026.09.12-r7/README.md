# Pack 2026.09.12-r7

Paired with launcher 1.4.0, for installed Steam build **24826606**.
The tested RC configuration was promoted to public content-addressed release URLs.
The pack contains 1,010 manifest entries across all options; the default
Commandodivers configuration uses 788 files. Nine new assets total 419,594,304 bytes.
Existing hosted content, including the lighter-texture variants, is reused.

The five new patch sets precede/follow Delta Squad in the order recorded by the
recipe: opening picture, opening dialogue, character voice banks, English labels,
then the existing eight Delta armor sets and the new starter helmet override.
All five additions require `commandos`. With that option disabled, all file names,
sizes and hashes equal the prior pack's corresponding option configuration.

Validation:

- 303 native launcher tests passed.
- All eight option combinations match the tested manifest with unique, gap-free patch numbering.
- Every one of the 788 active default-mode files matched the release by name, size and SHA256.
- All 769 nonempty manifest references were verified against published release metadata;
  all nine new assets matched GitHub's SHA256 digests. Empty companions are created locally.
- The opening picture and dialogue were confirmed in game. Voice slots were visible.
  Voice audio decoding and payload checks passed; full multiplayer audio and starter
  helmet appearance still need team gameplay confirmation.

FOV is not part of this pack. No camera-distance modification ships.
See [team setup](../../TEAM-SETUP.md) for character choices and required unlocks.

Reproduction: build and validate the RC patch sources, then use
`tools/prepare-tested-pack.ps1` with a saved tested manifest and its hash-named assets.
Run `tools/Release.Check` against that manifest, the preceding release manifest and
the deployed game directory. `tools/publish-tested-assets.ps1` uploads the staged plan
and verifies hosted digests before publishing the pack release. Never promote a
loopback test URL into the shared manifest.
