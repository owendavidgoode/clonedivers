# Stage two subtitle-confirmed original lines per commando for an armor-routing proof.
# This does not create a voice-slot replacement or an installable game patch.
param(
    [string]$Catalog = (Join-Path $PSScriptRoot '..\dist\rc-upgrade\rc-delta-candidates.json'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist\rc-upgrade\rc-voice-routing-proof'),
    [string]$EventMap = (Join-Path $PSScriptRoot '..\dist\rc-upgrade\rc-voice-current-event-map.json')
)
$ErrorActionPreference = 'Stop'
$clips = @(Get-Content -LiteralPath $Catalog -Raw | ConvertFrom-Json)
$currentEvents = if (Test-Path -LiteralPath $EventMap) { Get-Content -LiteralPath $EventMap -Raw | ConvertFrom-Json } else { $null }
if (Test-Path -LiteralPath $OutDir) { throw 'Choose a new staging directory; existing work is never overwritten.' }
$rules = @(
    @{ armor = 'SC-30'; character = 'Sev'; prefix = 'D07' },
    @{ armor = 'CM-10'; character = 'Fixer'; prefix = 'D40' },
    @{ armor = 'CE-35'; character = 'Scorch'; prefix = 'D62' },
    @{ armor = 'DP-11'; character = 'Boss'; prefix = 'D38' }
)
$planned = foreach ($rule in $rules) {
    foreach ($line in @(@{ semantic = 'affirmative'; suffix = 'ZZ012'; text = 'Affirmative.' },
                        @{ semantic = 'negative'; suffix = 'ZZ013'; text = 'Negative.' })) {
        $stem = $rule.prefix + $line.suffix
        $matches = @($clips | Where-Object {
            $_.package -eq 'mp_voice.uax' -and $_.character -eq $rule.character -and
            [IO.Path]::GetFileNameWithoutExtension($_.file) -eq $stem
        })
        if ($matches.Count -ne 1) { throw "Expected one original clip for $stem; found $($matches.Count)." }
        $clip = $matches[0]
        $expected = 'Delta ' + $rule.prefix.Substring(1) + ': ' + $line.text
        if ($clip.subtitles.text -notcontains $expected) { throw "Original subtitle mismatch for $stem." }
        $hash = (Get-FileHash -LiteralPath $clip.file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $clip.sha256) { throw "Source WAV changed: $($clip.file)" }
        $hd2Targets = @(
            foreach ($bank in $currentEvents.banks) {
                foreach ($row in $bank.rows | Where-Object semantic -EQ $line.semantic) {
                    if (-not $row.source_present_in_current_bank) { continue }
                    foreach ($sound in $row.sounds) {
                        foreach ($eventPath in $sound.event_paths) {
                            [pscustomobject]@{
                                native_voice_type = $bank.native_voice_type; bank = $bank.bank
                                bank_sha256 = $bank.bank_sha256; event_id = $eventPath.event_id
                                action_id = $eventPath.action_id; action_type = $eventPath.action_type
                                sound_id = $sound.sound_id; media_id = $row.media_id
                                archive_resource_hash = $row.archive_resource_hash
                                transcript_source = $currentEvents.semantic_label_source
                            }
                        }
                    }
                }
            }
        )
        [pscustomobject]@{
            armor = $rule.armor; character = $rule.character; semantic = $line.semantic
            original_object = $clip.object; source_wav = $clip.file; sha256 = $hash
            wav = ($rule.character + '/' + $line.semantic + '.wav')
            subtitle = $expected; subtitle_sources = $clip.subtitles
            hd2_targets = $hd2Targets
        }
    }
}
New-Item -ItemType Directory -Path $OutDir | Out-Null
foreach ($entry in $planned) {
    $target = Join-Path $OutDir $entry.wav
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $entry.source_wav -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        throw "Staged WAV verification failed: $target"
    }
}
[pscustomobject]@{
    status = 'Source staging only. Per-speaker armor routing unresolved; HD2 targets populated when current event map is supplied.'
    activation = 'Republic Commando mode enabled AND speaking diver armor matches a listed rule.'
    fallback = 'Preserve existing generic clone audio for other armor and mode disabled.'
    validation = 'Original subtitle text and WAV SHA256 verified; in-game playback not tested.'
    target_scope = 'Each character supports every native voice type; these targets do not bind armor to a voice slot.'
    clips = @($planned)
} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $OutDir 'manifest.json') -Encoding UTF8
Write-Host "Staged $($planned.Count) verified original WAVs and manifest to $OutDir"
