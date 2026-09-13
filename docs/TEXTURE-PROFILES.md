# Texture profiles and build staging

Format 3 separates `PackFile.modes` (`clonedivers`, `commandos`) from
`PackFile.textureProfiles`. Missing conditions include every supported choice;
an empty list is invalid. The pack declares a `textureProfiles` catalog containing
`id`, `name`, and `description`; `full` is required. IDs are lower-case identifiers.
Files with identical names and content can share a list of allowed profiles.
Legacy `option`/`unlessOption` gates continue to apply as additional conditions.

`Pack.EffectiveFiles(pack, enabled, textureProfile)` validates an explicit profile,
selects both conditions, checks filename uniqueness, then renumbers patch sets.
Without an explicit profile, the legacy `skinny` option maps to `lighter`; otherwise
the choice is `full`. Formats 1 and 2 continue to parse. Unsupported manifest formats
are rejected. Format 3 validates duplicate overlap across all declared modes and
profiles, including compatible legacy optional gates.

## Builder

The existing `build-manifest.ps1` remains a format-2 builder. Use the independent
`build-profile-manifest.ps1` to stage format 3 from a verified format-2 base. It
requires canonical all-mod filenames, not an optionally renumbered live subset.

```powershell
./tools/build-profile-manifest.ps1 `
  -BaseManifest ./manifest.json `
  -BaseDirectory '<verified canonical full folder>' `
  -ProfilesPath ./dist/profile-candidates/profiles-public.json `
  -Version 2026.09.13-r9 `
  -OutPath ./dist/new-candidate/manifest.json `
  -UploadPlanPath ./dist/new-candidate/upload-plan.json
```

Profile specs are a JSON array. Each entry supplies `id`, `name`, `description`,
and `directory`. `full` comes from `BaseDirectory` and is not repeated in the spec.
An optional `receiptSourceDirectory` identifies the source of a chained variant;
its inventory must match the original full/skinny base or a previously verified
profile. Every candidate requires `variant-receipt.json`, must preserve canonical
patch bundles, and must keep every nonempty companion present. Original mode gates
are preserved for both transformed and additional companion files.

The builder writes a candidate, upload plan and provenance receipt. It does not
change game files or publish. Validate the result with the native `Profile.Check`
tool and the release checks before promotion. A candidate copied from an old base
retains that base's app metadata; the release workflow must set current app metadata.

## Optimizer wrapper

`optimize-verified-pack.ps1` accepts `-MaxSize`, `-StreamFloor`, and `-Restream`.
Outputs must be fresh. It verifies transformed bundles against their source,
checks that the source inventory stayed unchanged, and writes settings and tool
hashes into the variant receipt. Deduplication remains disabled. Use identical
stream floors when comparing resolution caps so the experiment isolates that cap.

## September 13 candidates

- Public candidate: `dist/profile-candidates/manifest-public-v3.json`, **full/lighter
  only**. Lighter includes previously omitted RC texture streaming.
- Trial candidate: `dist/profile-candidates/manifest-trial-v3.json`, adds 1024 and
  512 caps, both with a 128-pixel resident tail. The older `reduced-1024` directory
  uses a 256 tail and is retained only as earlier experiment evidence.
- The public candidate is separate from trial manifests. Reduced textures remain
  gated on gameplay benefit and visual acceptance on the weakest PC.

File-size reductions are not runtime memory or frame-time measurements. Native
selection/integrity tests prove mode isolation and bytes; they do not establish
visual correctness or performance.
