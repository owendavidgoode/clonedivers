# Stale C: checkout review, 2026-11-09

The C: checkout was inspected read-only. Do not build, publish, reset, clean, or
fast-forward it until its uncommitted work is archived. The commands below are
an owner-run plan; none of them was executed during this review.

## Snapshot and preservation

`C:\Users\goode\OneDrive\Desktop\clonedivers` is on `main@150e58f`.
The locally cached `origin/main` is `568da7b`. The repository is shallow at
`6f755c2`: `merge-base --is-ancestor 150e58f origin/main` exits 1 and no merge
base is visible. The visible histories have 89/61 left/right commits; this does
not establish genuine divergence. Fetch full history before attempting a
fast-forward. Network access was unavailable, so remote state was not checked.
The review worktree is `D:\ClaudeWork\sw-housekeeping`.

There are **2,736** changed/nonignored untracked paths: 17 modified tracked
files and 2,719 untracked files, totaling 30,548,571 bytes. Git status does not
include the much larger ignored outputs. Full path, size, raw SHA-256,
CRLF-to-LF-normalized Git blob ID, preservation class, and discovered local copy
are recorded in [the changed-file inventory](C-CHECKOUT-CHANGED-FILES.csv).

| Content match | Paths | Meaning |
| --- | ---: | --- |
| Current main, same path | 86 | Already preserved in the current source tree |
| Current main, another path | 29 | Same content elsewhere; often small project boilerplate |
| Ordinary branch/tag history | 11 | Older versions preserved in main history, not current files |
| Codex internal refs only | 2,609 | Found under internal snapshots/checkpoints, absent from normal branch/tag history |
| No reachable content match | 1 | Latest readiness document; earlier revisions exist, but differ |

The **126** ordinary-history matches are all reachable from cached `origin/main`.
The other **2,610** paths need an owner-controlled archive branch. Internal
`refs/codex/snapshots/*` and `refs/codex/turn-diffs/checkpoints/*` are local
recovery aids, not evidence of a pushed archival branch or an independent backup.
For example, checkpoint tree `8f1f4b1` contains much of this work.

Eight of those 2,610 paths also have matching named, same-size files in other
local workspaces; their locations are in the CSV. Four are Steam-fallback
launcher/telemetry files under
`D:\CodexScratch\hellforge\steam-fallback-20261102-225652`; the others are
the SEAF integration document, crash reader, and two crash-probe sources.
That leaves **2,602** without an ordinary-history match or a discovered named
local duplicate. Most still have internal checkpoint copies.

The latest `docs/HELLFORGE-PLAYER-READINESS-2026-11-04.md` is the one changed
file without a matching reachable Git blob in the inventory. It is 20,326 bytes,
raw SHA-256 `e606ebe08a9703cadf03a2966f334b6302a56a5fc9bea19a95a37e344c277fec`.
Neither the older committed document nor checkpoint `8f1f4b1` has these bytes.
An object existing somewhere in the shared object database is not a durable
owner-controlled reference.

## Categories and what to retain

| Area | Findings | Treatment |
| --- | --- | --- |
| Launcher and tests | 13 changed/new launcher paths and five test paths. C: advertises 1.9.8; main is 1.14.0. The initial HellForge client/account/form/recovery work is preserved in main history and has newer implementations. The Steam fallback, Empire/Covenant session logging, telemetry and preview variants also have local-only differences. | Superseded as a launcher baseline. Archive exact files; review individual differences later rather than copying this launcher over main. |
| Empire and Forge documents | 101 changed/new documents, including 559 added lines in `docs/STATUS.md`, Empire r21-r44 authoring/QA/playtest records, crew66, admiral69, names72, torso75 and voice76 handoffs. 87 document variants are outside ordinary branch/tag history. | Preserve historical findings and acceptance limits; they are not current release instructions. |
| Tools | 2,613 changed/new paths; 2,513 lack ordinary-history matches. Includes `Hd2R44.Next64/65/66/66b/69/72/75/76`, corresponding installers/feeds, Empire audio/model/archive builders and independent readbacks. All changed areas together include 1,780 Python scripts, 193 C# files, 181 projects, 97 PowerShell scripts and 308 generated `.pyc` files. | Preserve source and specifications. Bytecode is reproducible, but small enough to include in a conservative archival commit. Do not run old installers/feeds during cleanup. |
| Logs | `log.txt`, `.1`, `.2` are empty. | No unique evidence; harmless to retain in the archive. |
| Ignored outputs | Pack candidates, native data views, derived components, source audio, validation reports, compiled tools, caches and downloaded mods under `dist/`. | Separate filesystem backup; a normal Git commit does not capture them. |

The shipped Forge r45-v4 pack and `D:\ClaudeWork\empire-forge` do not establish
preservation of the private r44 experiments. The older documents explicitly
leave runtime appearance, voice previews and mission playback unaccepted. For
example, voice76 is an offline eight-STREAM expansion over names72, not a shipped
replacement voice system. Retain those qualifications with its artifacts.

## Ignored outputs and large binaries

The accessible `dist` inventory contains **256,109 files**, whose apparent sizes
sum to **2,775,798,087,806 bytes (2.52 TiB)**. This is not physical disk usage:
hardlinks, sparse files and repeated data views can make the sum misleading.
Do not attempt a blind copy without capacity planning.

| Priority output directory under `dist/` | Files | Apparent size |
| --- | ---: | ---: |
| `hd2-update-2026-10-25` | 2,752 | 2.912 GiB |
| `empire-content-2026-11-01` | 245 | 0.268 GiB |
| `hd2-update-2026-10-29` | 877 | 0.275 GiB |
| `empire-voices-2026-11-01` | 22,562 | 1.925 GiB |

These contain candidate66/69/72/75/76 manifests, derived components, composition
and readback evidence, tooling seals, and the authored voice catalog/listening
library. Twelve selected manifests/catalogs/reports were SHA-256 compared with
named, same-size candidates in the search roots below; **no matching independent
copy was found**. Exact paths and hashes are in
[the selected-output inventory](C-CHECKOUT-SELECTED-DIST.csv).
This does not prove every asset payload is unique: earlier packs and r45 may reuse
payloads, and only these twelve ignored outputs received content comparisons.

The largest directory totals are `empire-next-2026-10-05` (470.78 GiB),
`empire-remaining-2026-10-05` (323.95 GiB),
`empire-transport-next-2026-10-04` (181.23 GiB),
`empire-next-2026-10-06` (175.35 GiB), and
`empire-finish-2026-10-08` (144.22 GiB). They mix research results and native
data views; size alone is not a reason to discard them.

Examples of large binaries: `pakchunk11-WindowsNoEditor.pak` (12.03 GiB) and
`pakchunk13-WindowsNoEditor.pak` (11.54 GiB) under
`empire-next-2026-10-06/armor/beach-source-v1/prepared-v2/pak-view/`;
the Whisper model cache's `model.bin` (2.88 GiB); recovered Clone Armory and
CIS ZIPs (2.63/2.54 GiB); repeated `bundles.26.nxa` views (1.82 GiB each).
Downloaded sources, model/runtime caches and build products are candidates for
later deduplication after their recipes, provenance and recoverability are checked.

## Owner-run archival and fast-forward plan

1. Stop work in the stale checkout and coordinate a quiet interval with other
   workers sharing its Git metadata. Close tools that write its outputs. Keep all
   ignored originals in place. The first backup below preserves raw source bytes
   and the four priority output trees; it deliberately does not archive all 2.52 TiB.
   Other ignored experiments still require backup before any later disk cleanup.
2. In Windows PowerShell 5.1, run this block. It reads the C: tree and writes a
   timestamped independent copy plus checksums. Any error stops the plan. Reparse
   points need a separate link-aware backup rather than silently following them.

```powershell
$ErrorActionPreference = 'Stop'
$stale = 'C:\Users\goode\OneDrive\Desktop\clonedivers'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$archive = "D:\ClaudeWork\backups\c-checkout-$stamp"
$archiveBranch = "archive/c-checkout-$stamp"
function Invoke-GitChecked {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "git failed: $args" }
}
if ((Invoke-GitChecked -C $stale branch --show-current) -ne 'main') { throw 'Review changed branch first' }
if (-not (Invoke-GitChecked -C $stale rev-parse HEAD).StartsWith('150e58f')) { throw 'Review changed HEAD first' }
Invoke-GitChecked -C $stale status --short --untracked-files=all
$files = @(Invoke-GitChecked -C $stale -c core.quotepath=false ls-files --cached --others --exclude-standard)
$priority = @('hd2-update-2026-10-25', 'empire-content-2026-11-01', 'hd2-update-2026-10-29', 'empire-voices-2026-11-01')
foreach ($name in $priority) {
    $folder = Join-Path $stale "dist\$name"
    $entries = @(Get-ChildItem -LiteralPath $folder -Recurse -Force)
    if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Link-aware backup needed: $folder" }
    $files += $entries | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $_.FullName.Substring($stale.Length + 1) }
}
$files = @($files | Sort-Object -Unique)
$bytes = ($files | ForEach-Object { (Get-Item -LiteralPath (Join-Path $stale $_)).Length } | Measure-Object -Sum).Sum
if ([IO.DriveInfo]::new('D:\').AvailableFreeSpace -lt ($bytes + 1GB)) { throw 'Choose a backup volume with enough space' }
if (Test-Path -LiteralPath $archive) { throw 'Choose a new archive directory' }
New-Item -ItemType Directory -Path "$archive\files" | Out-Null
$checksums = foreach ($relative in $files) {
    $source = Join-Path $stale $relative
    $target = Join-Path "$archive\files" $relative
    if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link-aware backup needed: $source" }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $source -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) { throw "Backup mismatch: $relative" }
    [pscustomobject]@{ Path = $relative; SHA256 = $hash }
}
$checksums | Export-Csv -LiteralPath "$archive\SHA256.csv" -NoTypeInformation -Encoding UTF8
```

3. Archive **all nonignored working changes**, not just the one most novel document.
   This preserves old source variants without merging them into the current launcher.
   Inspect the staged paths before committing; stop if the inventory changed
   unexpectedly. The largest file in the reviewed change set was only 644,535 bytes.

```powershell
Invoke-GitChecked -C $stale switch --no-overwrite-ignore -c $archiveBranch
Invoke-GitChecked -C $stale add -A -- .
Invoke-GitChecked -C $stale diff --cached --stat
Invoke-GitChecked -C $stale diff --cached --check
# After inspecting the staged source/archive paths:
Invoke-GitChecked -C $stale commit -m 'Archive stale C checkout before main fast-forward' -m 'Co-Authored-By: Codex (gpt-6.1-sol) <noreply@openai.com>'
Invoke-GitChecked -C $stale status --short --untracked-files=all
# Require empty status here; ignored files are intentionally still present.
if (@(Invoke-GitChecked -C $stale status --porcelain --untracked-files=all).Count -ne 0) { throw 'Working changes remain' }
if ((Invoke-GitChecked -C $stale rev-parse --is-shallow-repository) -eq 'true') {
    Invoke-GitChecked -C $stale fetch --unshallow origin
} else {
    Invoke-GitChecked -C $stale fetch origin
}
# If fetching full history fails, keep the archive branch and raw backup; stop here.
Invoke-GitChecked -C $stale bundle create "$archive\source.bundle" $archiveBranch origin/main
Invoke-GitChecked -C $stale bundle verify "$archive\source.bundle"
Invoke-GitChecked -C $stale bundle list-heads "$archive\source.bundle"
```

If `diff --cached --check` flags pre-existing whitespace, inspect those diagnostics;
do not rewrite historical source merely to make the archive pass a style check.
Resume only once that archival exception is understood. Copy the verified bundle
and checksum-backed output backup to a second disk or owner-controlled backup.
Do not assume the local archive directory alone protects against disk loss.

4. Only after the archive commit, full-history fetch, verified copies, and second
   backup exist, fast-forward main. The explicit ignore protection makes Git refuse
   an ignored-file collision. Stop and investigate any refusal; do not force it.

```powershell
Invoke-GitChecked -C $stale switch --no-overwrite-ignore main
Invoke-GitChecked -C $stale merge-base --is-ancestor HEAD origin/main
Invoke-GitChecked -C $stale -c merge.autostash=false merge --ff-only --no-overwrite-ignore origin/main
Invoke-GitChecked -C $stale status --short --untracked-files=all
Invoke-GitChecked -C $stale log -1 --oneline
Invoke-GitChecked -C $stale show-ref --verify "refs/heads/$archiveBranch"
```

If the ancestry check still refuses after unshallowing, return the divergence to
the owner for a revised plan; do not reset or rename main to make it appear updated.

No stash, reset, rebase, force-push, public feed change, release, or ignored-file
deletion is needed. The archive branch intentionally stays separate from main.
Afterward, keep building from a designated D: worktree until the owner explicitly
retires the stale-checkout restriction. Restore an old experiment into a new
worktree from the archive branch; do not overlay the old launcher onto main.

## Method and limits

Read-only Git commands: `status --porcelain=v1 -z --untracked-files=all`,
`diff --stat`, `ls-tree -rz origin/main`, `rev-list --objects --all`,
`rev-list --objects --branches --remotes --tags`, `for-each-ref`, and
`merge-base --is-ancestor`. `GIT_OPTIONAL_LOCKS=0` was set for checkout inspection.
Text comparisons normalize CRLF to LF; raw SHA-256 in the CSV preserves byte identity.

Named/size-matched content searches covered accessible files under
`D:\ClaudeWork`, `D:\CodexScratch`, and `C:\Users\goode\.codex\worktrees`,
excluding `.git`, virtual environments, `node_modules`, `bin`, `obj`, bytecode
caches and junction traversal. About 88,800 file entries were considered.
History beyond the shallow boundary, renamed copies, cloud/external backups,
remote release assets and excluded build
trees were not exhaustively searched. Absence here is not proof of global absence.
Large ignored binary payloads were inventoried by metadata, not fully hashed.

Access was denied to one Empire workspace temp directory, four temp directories
under `dist/live-audio-2026-10-03`, and three `dist/uv-runtime` subdirectories.
Totals exclude those contents. No checkout file, index, branch, or internal ref
was changed as part of this read-only inspection.
