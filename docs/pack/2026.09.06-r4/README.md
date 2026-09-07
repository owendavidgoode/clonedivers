# Pack 2026.09.06-r4

The r3 base, unchanged (its deploy report and conflict report are in [../2026.09.05-r3/](../2026.09.05-r3/)), plus the
**Lighter textures** variant (option `skinny`) built from it by `tools\optimize-pack.ps1`: `optimize-log.txt` is that build's
log (resident GPU data 7.87 GB -> 3.63 GB, .stream 0.30 GB -> 4.72 GB, 74 bundles streamed, 175 copied through, six
pre-existing verifier notes). `pack-recipe.json` is the recipe with the variants block.
