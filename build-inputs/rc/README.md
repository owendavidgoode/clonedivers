# Republic Commando build inputs

`voice-mapping.json` preserves the exact 1,488 voice selections that shipped. Paths
are relative to the workspace. `inputs.lock.json` locks the 322 original recordings,
template, conversion dependencies and both output hashes. Original build reports and
the intro cut settings are historical provenance, not current installation status.

Rebuild with the existing Python 3.13 environment (no dependency downloads needed):

```powershell
./tools/rebuild-rc-voices.ps1 -Template dist/rc-source-templates/full-clone-voice.patch -OutDir dist/new-voice-rebuild
```

The wrapper verifies every source and dependency, builds into a fresh folder, and
requires byte-for-byte equality with the shipped outputs. It never installs them.
Supply `-Uv` / `-Python` for another installed runtime. Python 3.13 was used for the
verified reproduction; exact output hashes remain the acceptance criterion.

Verified runtime: Python **3.13.2**, uv **0.12.13**. The pinned `util.py` comes from
`RaidingForPants/hd2-audio-modder` revision `c408a44d14959d0adcbcbde84f7202e5333ea670`;
wwav comes from `adamXbot/wwav` revision `340aa3535109612cd0b9552e1355fbd836363735`.

The original game recordings and template are not source-controlled. Preserve them
with `tools/archive-rc-build-inputs.ps1`; its ZIP recreates the relative input layout
when extracted into a checkout. A verified local capsule and checksum receipt are
at `dist/maintenance-review/rc-voice-inputs.zip`. Keep that capsule with build backups.
The capsule includes the two pinned conversion modules and the wwav license. It is
for rebuilding, not an additional player download.

The semantic mapper and RC dialogue exporter remain in `tools/` for future authoring.
Re-running the mapper is an intentional content change; reproducing this release
uses the preserved mapping. Unmatched calls and shared exertions retain clone audio.

Intro: the selected cut is 2:43.766667 at 3840×2160/30 fps, from source 8.4s to
172.166667s. The successful Bink encode used **four slices**, arguments
`/V6344 /D95 /M10 /#`, plus a separate Wwise dialogue patch. Before any new full encode,
match the working header and test a two-second sample. Detailed cut/fade settings
and output hashes are in the adjacent JSON records.
