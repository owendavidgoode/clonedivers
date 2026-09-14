# Pure helpers; dot-source in fixture tests without starting capture.
function Get-FrameSummary {
    param([object[]]$Frames)
    if (-not $Frames -or $Frames.Count -lt 100) { throw 'Insufficient frame data (100 rows required).' }
    $columns=@($Frames[0].PSObject.Properties.Name)
    $field=@('MsBetweenPresents','CPUFrameTime','MsBetweenAppStart') | Where-Object { $columns -contains $_ } | Select-Object -First 1
    if (-not $field) { throw 'Unsupported PresentMon frame-time schema; retain raw CSV.' }
    $selected=@($Frames); $swapchain=$null
    if ($columns -contains 'SwapChainAddress') {
        $largest=$Frames | Group-Object SwapChainAddress | Sort-Object Count -Descending | Select-Object -First 1
        $selected=@($largest.Group); $swapchain=$largest.Name
    }
    $values=[Collections.Generic.List[double]]::new()
    foreach ($row in $selected) {
        $value=0.0
        if ([double]::TryParse([string]$row.$field,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$value) -and $value -gt 0 -and -not [double]::IsInfinity($value)) { $values.Add($value) }
    }
    if ($values.Count -lt 100) { throw 'Insufficient valid frame intervals on primary swapchain.' }
    $sorted=@($values | Sort-Object); $sum=($values | Measure-Object -Sum).Sum
    $gpuField=@('MsGPUBusy','GPUBusy') | Where-Object { $columns -contains $_ } | Select-Object -First 1
    $gpuValues=@(foreach ($row in $selected) {
        $busy=0.0
        if ($gpuField -and [double]::TryParse([string]$row.$gpuField,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$busy) -and $busy -ge 0 -and -not [double]::IsInfinity($busy)) { $busy }
    })
    [pscustomobject][ordered]@{
        rawRows=$Frames.Count;selectedSwapchain=$swapchain;intervalField=$field;validIntervals=$values.Count
        invalidIntervals=$selected.Count-$values.Count;meanFps=1000*$values.Count/$sum
        intervalSeconds=$sum/1000;over50MsPerMinute=60000*@($values | Where-Object { $_ -gt 50 }).Count/$sum;over100MsPerMinute=60000*@($values | Where-Object { $_ -gt 100 }).Count/$sum
        medianMs=$sorted[[math]::Ceiling(.5*$sorted.Count)-1];p95Ms=$sorted[[math]::Ceiling(.95*$sorted.Count)-1];p99Ms=$sorted[[math]::Ceiling(.99*$sorted.Count)-1]
        over50Ms=@($values | Where-Object { $_ -gt 50 }).Count;over100Ms=@($values | Where-Object { $_ -gt 100 }).Count
        gpuBusyField=$gpuField;meanGpuBusyMs=if ($gpuValues.Count) { ($gpuValues | Measure-Object -Average).Average } else { $null }
        method='Nearest-rank interval percentiles; FPS=1000/mean interval. Largest swapchain only. Raw dropped/generated rows retained; not displayed FPS.'
    }
}
function Get-SafeBenchmarkSettings {
    param([string]$ConfigPath=(Join-Path $env:APPDATA 'Clonedivers\config.json'),[string]$GameSettingsPath=(Join-Path $env:APPDATA 'Arrowhead\Helldivers2\user_settings.config'))
    $result=[ordered]@{launcherSettings='unavailable';gameSettings='unavailable'}
    try {
        $config=Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json; $safe=[ordered]@{}
        foreach ($name in @('InstalledPackVersion','InstalledGameBuild','TextureProfile','InstalledTextureProfile')) {
            $value=[string]$config.$name; $safe[$name]=if ($value -match '^[a-zA-Z0-9._-]{1,64}$') { $value } else { 'unavailable' }
        }
        $safe.commandos=if ($null -ne $config.Options.commandos) { [bool]$config.Options.commandos } else { $null }
        $safe.skinny=if ($null -ne $config.Options.skinny) { [bool]$config.Options.skinny } else { $null }
        $result.launcherSettings=$safe
    } catch { }
    try {
        $raw=Get-Content -LiteralPath $GameSettingsPath -Raw; $safe=[ordered]@{}
        foreach ($key in @('screen_resolution','render_resolution','render_scale','render_resolution_factor_index','upscaling_method','upscaling_quality','texture_quality','vertical_fov','vsync','max_fps','framerate_limit','framerate_limit_enabled','fullscreen','object_lod_quality','terrain_quality','particle_quality','shadows','volumetric_clouds_quality','volumetric_fog_quality','reflection_quality','lighting_and_material_quality')) {
            $match=[regex]::Match($raw,'(?m)^\s*'+[regex]::Escape($key)+'\s*=\s*([0-9.\s\[\],-]+|true|false)\s*$')
            $safe[$key]=if ($match.Success) { ($match.Groups[1].Value -replace '\s+',' ').Trim() } else { 'unavailable' }
        }
        $result.gameSettings=$safe
    } catch { }
    [pscustomobject]$result
}
