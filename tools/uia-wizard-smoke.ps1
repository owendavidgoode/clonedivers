<#
.SYNOPSIS
    Offline UIAutomation end-to-end smoke test for the unified Clonedivers Avalonia 11 Launcher & Embedded 4-Step Setup (F1-F16, v2.1 UI).
.NOTES
    Runs 100% isolated via CLONEDIVERS_FORGE_PREFS / CLONEDIVERS_FORGE_TEST_HOSTS / CLONEDIVERS_FORGE_TEST_STEAM / CLONEDIVERS_FORGE_TEST_FREE_BYTES / CLONEDIVERS_FORGE_SKIP_UPDATE_CHECK
    in D:\ClaudeWork\cache\tmp. Never modifies real AppData, real D:\HellForge\, or launches helldivers2.exe.
#>
param(
    [string]$Worktree = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'

if (Get-Process helldivers2 -ErrorAction SilentlyContinue) {
    throw 'REFUSED: helldivers2.exe is running.'
}

$dotnetCandidates = @(
    (Join-Path $Worktree 'dist\dotnet-sdk\dotnet.exe'),
    'C:\Users\goode\OneDrive\Desktop\clonedivers\dist\dotnet-sdk\dotnet.exe',
    'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\dotnet-10.0.403\dotnet.exe'
)
$dotnet = $dotnetCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $dotnet) { $dotnet = 'dotnet' }
if ($dotnet -like '*dotnet-10*' -or $dotnet -like '*dotnet-9*') {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $cliHome = 'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\dotnet-home'
    if (Test-Path -LiteralPath $cliHome) { $env:DOTNET_CLI_HOME = $cliHome }
    $env:DOTNET_ROLL_FORWARD = 'Major'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$tmpRoot = 'D:\ClaudeWork\cache\tmp'
New-Item -ItemType Directory -Force $tmpRoot | Out-Null
$sessionId = 'uia-smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$tmp = Join-Path $tmpRoot $sessionId
New-Item -ItemType Directory -Force $tmp | Out-Null

$procs = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Host "  PASS  $Message"
}

function Build-App {
    Write-Host 'Building Clonedivers & ForgeBridge...'
    $distPresentMon = Join-Path $Worktree 'dist\PresentMon.exe'
    $srcPresentMon = 'C:\Users\goode\OneDrive\Desktop\clonedivers\dist\PresentMon.exe'
    if (-not (Test-Path -LiteralPath $distPresentMon) -and (Test-Path -LiteralPath $srcPresentMon)) {
        New-Item -ItemType Directory -Force (Split-Path $distPresentMon) | Out-Null
        Copy-Item -LiteralPath $srcPresentMon -Destination $distPresentMon
    }
    & $dotnet build (Join-Path $Worktree 'Clonedivers.ForgeBridge\Clonedivers.ForgeBridge.csproj') -c Release -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build Clonedivers.ForgeBridge' }
    & $dotnet build (Join-Path $Worktree 'Clonedivers\Clonedivers.csproj') -c Release -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build Clonedivers' }
    $exe = Join-Path $Worktree 'Clonedivers\bin\Release\net8.0-windows\win-x64\Clonedivers.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        $exe = (Get-ChildItem (Join-Path $Worktree 'Clonedivers\bin\Release') -Recurse -Filter Clonedivers.exe | Select-Object -First 1).FullName
    }
    if (-not $exe -or -not (Test-Path -LiteralPath $exe)) { throw 'Clonedivers.exe not found under Clonedivers\bin\Release' }
    return [string]$exe
}

function Build-FixtureBinaries([string]$DestDir) {
    $fxProjDir = Join-Path $tmp 'fx-src'
    New-Item -ItemType Directory -Force $fxProjDir | Out-Null
    $bridgeDll = Join-Path $Worktree 'Clonedivers.ForgeBridge\bin\Release\net8.0\HellForge.Launcher.ClonediversBridge.dll'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>FakeForge</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="HellForge.Launcher.ClonediversBridge">
      <HintPath>$bridgeDll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $fxProjDir 'FakeForge.csproj') -Encoding UTF8

    @'
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clonedivers;

var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
var role = Path.GetFileNameWithoutExtension(exePath);
var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exePath)!, "..", ".."));

if (role.Equals("HellForge", StringComparison.OrdinalIgnoreCase))
{
    return 0;
}

if (role.Equals("HellForge.LocalBackend", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length >= 2 && args[1] == "import-preview")
    {
        Console.WriteLine("""{"categories":{"level":{"vanilla":"1,000 XP"}}}""");
        return 0;
    }
    if (args.Length >= 3 && args[1] == "import")
    {
        var profilePath = Path.Combine(Path.GetFullPath(args[2]), "profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        if (!File.Exists(profilePath))
            File.WriteAllText(profilePath, """{"schemaVersion":1,"progression":{"experience":1000},"customization":{"items":[]},"inventory":{"items":[]}}""");
        Console.WriteLine("fixture imported");
        return 0;
    }
    Console.Error.WriteLine("Unknown backend args");
    return 2;
}

// HellForge.Cli mode
var statePath = Path.Combine(root, "fixture-state.json");
var modsPath = Path.Combine(root, "fixture-mods.json");
var appBuildPath = Path.Combine(root, "app", "app-build.json");

JsonObject LoadState() => File.Exists(statePath)
    ? JsonNode.Parse(File.ReadAllText(statePath))!.AsObject()
    : new JsonObject { ["gameCopy"] = false, ["runner"] = true, ["running"] = false, ["lastRun"] = null, ["lastStatus"] = null, ["lastFailure"] = null };

void SaveState(JsonObject obj) => File.WriteAllText(statePath, obj.ToJsonString());

(string version, string engine, int modSdk) ReadAppBuild()
{
    if (!File.Exists(appBuildPath)) return ("0.4.0-beta", "0.1.47", 16);
    using var doc = JsonDocument.Parse(File.ReadAllText(appBuildPath));
    var el = doc.RootElement;
    return (
        el.TryGetProperty("version", out var v) ? v.GetString() ?? "0.4.0-beta" : "0.4.0-beta",
        el.TryGetProperty("engine", out var e) ? e.GetString() ?? "0.1.47" : "0.1.47",
        el.TryGetProperty("modSdk", out var s) && s.TryGetInt32(out var sdk) ? sdk : 16
    );
}

List<JsonObject> LoadMods()
{
    if (File.Exists(modsPath))
        return JsonNode.Parse(File.ReadAllText(modsPath))!.AsArray().Select(n => n!.AsObject()).ToList();

    var list = new List<JsonObject>();
    foreach (var id in HellForgeGameplayCatalog.Pinned.Order)
    {
        var art = HellForgeGameplayCatalog.Pinned.Artifacts.SingleOrDefault(a => a.Id == id);
        var req = HellForgePreset.Required.SingleOrDefault(r => r.Id == id);
        var name = art?.Name ?? (string.IsNullOrEmpty(req.Name) ? "Vanilla Plus" : req.Name);
        var ver = art?.Version ?? req.Version ?? "1.0.0";
        var folder = Path.Combine(root, "mods", id);
        Directory.CreateDirectory(folder);
        JsonArray? opts = null;
        if (id == HellForgePreset.GuardDogs)
        {
            opts = new JsonArray(
                new JsonObject { ["name"] = "Targeting Marker", ["on"] = false },
                new JsonObject { ["name"] = "Smarter Guard Dogs and Sentries", ["on"] = true },
                new JsonObject { ["name"] = "Guard Dogs", ["on"] = true },
                new JsonObject { ["name"] = "Sentries", ["on"] = true },
                new JsonObject { ["name"] = "Intelligence", ["on"] = true },
                new JsonObject { ["name"] = "Safety", ["on"] = true },
                new JsonObject { ["name"] = "Sentry Safety", ["on"] = true },
                new JsonObject { ["name"] = "SEAF Safety", ["on"] = true }
            );
        }
        var modObj = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["enabled"] = true,
            ["folder"] = folder,
            ["tags"] = new JsonArray("gameplay"),
            ["version"] = ver
        };
        if (opts != null) modObj["options"] = opts;
        list.Add(modObj);
    }
    return list;
}

void SaveMods(List<JsonObject> list)
{
    var arr = new JsonArray(list.Select(x => JsonNode.Parse(x.ToJsonString())).ToArray());
    File.WriteAllText(modsPath, arr.ToJsonString());
}

if (args.Length == 0) return 0;
switch (args[0])
{
    case "status":
    {
        var st = LoadState();
        var (ver, eng, sdk) = ReadAppBuild();
        var isRun = st["running"]!.GetValue<bool>();
        var resp = new
        {
            install = root,
            hellforge = ver,
            engine = eng,
            modSdk = sdk,
            networkBlock = "wfp",
            gameBuild = HellForgePack.SupportedBuild,
            ready = new { gameCopy = st["gameCopy"]!.GetValue<bool>(), sessionRunner = st["runner"]!.GetValue<bool>(), linuxNamespace = false },
            session = new
            {
                running = isRun,
                gameLaunched = isRun ? (bool?)true : null,
                lastRun = st["lastRun"]?.GetValue<string?>(),
                lastStatus = st["lastStatus"]?.GetValue<string?>(),
                lastFailure = st["lastFailure"]?.GetValue<string?>()
            },
            settings = new { multiplayer = true, localPlayFab = true, sandbox = true }
        };
        Console.WriteLine(JsonSerializer.Serialize(resp));
        return 0;
    }
    case "discover":
        Console.WriteLine("[]");
        return 0;
    case "steam-download":
    {
        var qrRow = "\u001b[97;40m" + new string('\u2588', 29) + "\u001b[0m";
        for (int i = 0; i < 15; i++) Console.Error.WriteLine(qrRow);
        Console.Error.WriteLine("45%: Downloading HD2 depot");
        Console.Error.Flush();
        Thread.Sleep(700);
        var st = LoadState();
        st["gameCopy"] = true;
        SaveState(st);
        var gameDir = Path.Combine(root, "game");
        Directory.CreateDirectory(gameDir);
        Console.WriteLine(JsonSerializer.Serialize(new { buildId = HellForgePack.SupportedBuild, destination = gameDir }));
        return 0;
    }
    case "settings":
        Console.WriteLine("""{"ok":true}""");
        return 0;
    case "profiles":
        Console.WriteLine("""{"profiles":[{"name":"default","template":"starter"}]}""");
        return 0;
    case "mods":
    {
        var mods = LoadMods();
        Console.WriteLine(new JsonObject
        {
            ["mods"] = new JsonArray(mods.Select(m => JsonNode.Parse(m.ToJsonString())).ToArray()),
            ["problems"] = new JsonArray(),
            ["modlist"] = "uia-smoke"
        }.ToJsonString());
        return 0;
    }
    case "mod-hash":
    {
        var folder = args[2];
        var mods = LoadMods();
        var match = mods.FirstOrDefault(m => string.Equals(m["folder"]?.GetValue<string>(), folder, StringComparison.OrdinalIgnoreCase));
        var id = match?["id"]?.GetValue<string>() ?? Path.GetFileName(folder);
        var art = HellForgeGameplayCatalog.Pinned.Artifacts.FirstOrDefault(a => a.Id == id);
        Console.WriteLine(JsonSerializer.Serialize(new { hash = art?.ContentHash ?? new string('0', 64) }));
        return 0;
    }
    case "mod-switch":
    {
        var mods = LoadMods();
        var idx = mods.FindIndex(m => m["id"]!.GetValue<string>() == args[2]);
        if (idx >= 0) mods[idx]["enabled"] = args[4] == "yes";
        SaveMods(mods);
        Console.WriteLine("""{"ok":true}""");
        return 0;
    }
    case "mod-order":
    {
        var order = args[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
        var mods = LoadMods();
        var reordered = order.Select(id => mods.First(m => m["id"]!.GetValue<string>() == id)).ToList();
        SaveMods(reordered);
        Console.WriteLine("""{"ok":true}""");
        return 0;
    }
    case "mod-pack":
    {
        var mods = LoadMods();
        if (args.Length > 3 && args[3] != "--all")
        {
            var idx = mods.FindIndex(m => m["id"]!.GetValue<string>() == args[2]);
            if (idx >= 0 && mods[idx]["options"] is JsonArray opts)
                foreach (var o in opts.OfType<JsonObject>())
                    if (o["name"]!.GetValue<string>() == args[4]) o["on"] = args[6] == "yes";
        }
        SaveMods(mods);
        Console.WriteLine("""{"ok":true}""");
        return 0;
    }
    case "play":
    {
        var st = LoadState();
        st["running"] = true;
        st["lastStatus"] = null;
        st["lastFailure"] = null;
        SaveState(st);
        Console.WriteLine("""{"event":"started"}""");
        return 0;
    }
    case "stop":
    {
        var st = LoadState();
        st["running"] = false;
        st["lastRun"] = "2026-11-26T00:00:00Z";
        SaveState(st);
        Console.WriteLine("""{"event":"stopping"}""");
        return 0;
    }
    default:
        Console.WriteLine("""{"ok":true}""");
        return 0;
}
'@ | Set-Content (Join-Path $fxProjDir 'Program.cs') -Encoding UTF8

    & $dotnet publish (Join-Path $fxProjDir 'FakeForge.csproj') -c Release -r win-x64 --self-contained -o $DestDir -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to publish FakeForge fixture binaries' }
}

function Seed-ForgeRoot([string]$Root, [string]$FxBin, [bool]$GameCopy = $false) {
    $cliDir = Join-Path $Root 'app\cli'
    $backendDir = Join-Path $Root 'app\backend'
    $modsDir = Join-Path $Root 'mods'
    $profileDir = Join-Path $Root 'profiles\default'
    New-Item -ItemType Directory -Force $cliDir, $backendDir, $modsDir, $profileDir | Out-Null

    Copy-Item (Join-Path $FxBin '*') $cliDir -Force
    Rename-Item (Join-Path $cliDir 'FakeForge.exe') 'HellForge.Cli.exe' -Force

    Copy-Item (Join-Path $FxBin '*') $backendDir -Force
    Rename-Item (Join-Path $backendDir 'FakeForge.exe') 'HellForge.LocalBackend.exe' -Force

    Copy-Item (Join-Path $FxBin '*') $Root -Force
    Rename-Item (Join-Path $Root 'FakeForge.exe') 'HellForge.exe' -Force

    '{"schemaVersion":1,"buildId":"25480438"}' | Set-Content (Join-Path $Root 'hellforge-install.json') -Encoding UTF8
    '{"version":"0.4.0-beta","engine":"0.1.47","modSdk":16}' | Set-Content (Join-Path $Root 'app\app-build.json') -Encoding UTF8
    '{"share":false}' | Set-Content (Join-Path $modsDir 'hellforge-mods.json') -Encoding UTF8
    '{"schemaVersion":1,"progression":{"experience":1000},"customization":{"items":[]},"inventory":{"items":[]}}' | Set-Content (Join-Path $profileDir 'profile.json') -Encoding UTF8

    @{
        gameCopy = $GameCopy
        runner = $true
        running = $false
        lastRun = $null
        lastStatus = $null
        lastFailure = $null
    } | ConvertTo-Json | Set-Content (Join-Path $Root 'fixture-state.json') -Encoding UTF8
}

function Write-SealedCapture([string]$Root) {
    $snapDir = Join-Path $Root 'account-snapshot\default'
    New-Item -ItemType Directory -Force $snapDir | Out-Null
    '{"items":[{"id":1}]}' | Set-Content (Join-Path $snapDir 'real-inventory.json') -Encoding UTF8
    '{"experience":12345}' | Set-Content (Join-Path $snapDir 'progression.json') -Encoding UTF8
    '{"steamAccount":"1","capturedUtc":"2026-11-26T00:00:00Z"}' | Set-Content (Join-Path $snapDir 'seal.json') -Encoding UTF8
    '{"progression":true,"items":1}' | Set-Content (Join-Path $snapDir 'snapshot.json') -Encoding UTF8
}

function Start-IsolatedLauncher([string]$Exe, [string]$PrefsPath, [string]$HostsPath) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    $psi.UseShellExecute = $false
    $psi.Environment['CLONEDIVERS_FORGE_PREFS'] = $PrefsPath
    $psi.Environment['CLONEDIVERS_FORGE_TEST_HOSTS'] = $HostsPath
    $psi.Environment['CLONEDIVERS_FORGE_TEST_STEAM'] = '1'
    $psi.Environment['CLONEDIVERS_FORGE_TEST_FREE_BYTES'] = '120000000000'
    $psi.Environment['CLONEDIVERS_FORGE_SKIP_UPDATE_CHECK'] = '1'
    $p = [System.Diagnostics.Process]::Start($psi)
    $procs.Add($p)
    return $p
}

function Find-Window([int]$ProcessId, [string]$TitleSubstr, [int]$TimeoutMs = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $winCond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    )
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $winCond)
            foreach ($w in $wins) {
                try { if ($w.Current.Name -like "*$TitleSubstr*") { return $w } } catch [System.Windows.Automation.ElementNotAvailableException] {}
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] {}
        Start-Sleep -Milliseconds 120
    }
    throw "Timed out waiting for window '$TitleSubstr' on PID $ProcessId"
}

function Describe-Elements([System.Windows.Automation.AutomationElement]$Scope) {
    $lines = [System.Collections.Generic.List[string]]::new()
    try {
        $all = $Scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($el in $all) {
            try {
                $name = $el.Current.Name
                $aid = $el.Current.AutomationId
                if (-not [string]::IsNullOrWhiteSpace($name) -or -not [string]::IsNullOrWhiteSpace($aid)) {
                    $lines.Add("  $($el.Current.ControlType.ProgrammaticName) id='$aid': '$name' enabled=$($el.Current.IsEnabled) off=$($el.Current.IsOffscreen)")
                }
            } catch [System.Windows.Automation.ElementNotAvailableException] {}
        }
    } catch [System.Windows.Automation.ElementNotAvailableException] {}
    return ($lines -join "`n")
}

function Find-ByName([System.Windows.Automation.AutomationElement]$Scope, [string]$NameSubstr, [int]$TimeoutMs = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $all = $Scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($el in $all) {
                try {
                    if ($el.Current.Name -like "*$NameSubstr*") { return $el }
                } catch [System.Windows.Automation.ElementNotAvailableException] {}
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] {}
        Start-Sleep -Milliseconds 120
    }
    $dump = Describe-Elements $Scope
    throw "Timed out waiting for element matching '$NameSubstr'. Visible:`n$dump"
}

function Find-ByAutomationId([System.Windows.Automation.AutomationElement]$Scope, [string]$AutomationId, [int]$TimeoutMs = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $el = $Scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
            if ($null -ne $el -and $el.Current.IsEnabled) { return $el }
        } catch [System.Windows.Automation.ElementNotAvailableException] {}
        Start-Sleep -Milliseconds 120
    }
    $dump = Describe-Elements $Scope
    throw "Timed out waiting for enabled AutomationId '$AutomationId'. Visible:`n$dump"
}

function Find-Button([System.Windows.Automation.AutomationElement]$Scope, [string]$Text, [int]$TimeoutMs = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $btns = $Scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
            foreach ($b in $btns) {
                try {
                    if ($b.Current.Name -eq $Text -and $b.Current.IsEnabled) { return $b }
                } catch [System.Windows.Automation.ElementNotAvailableException] {}
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] {}
        Start-Sleep -Milliseconds 120
    }
    $dump = Describe-Elements $Scope
    throw "Timed out waiting for enabled button '$Text'. Visible:`n$dump"
}

function Invoke-Element([System.Windows.Automation.AutomationElement]$El) {
    $pat = $null
    if ($El.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) {
        ([System.Windows.Automation.InvokePattern]$pat).Invoke()
        return
    }
    if ($El.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) {
        ([System.Windows.Automation.TogglePattern]$pat).Toggle()
        return
    }
    if ($El.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) {
        ([System.Windows.Automation.SelectionItemPattern]$pat).Select()
        return
    }
    throw "Element '$($El.Current.Name)' (id='$($El.Current.AutomationId)') does not expose Invoke/Toggle/SelectionItem pattern"
}

function Stop-LauncherCleanly([System.Diagnostics.Process]$Proc) {
    if ($null -eq $Proc) { return }
    try {
        if (-not $Proc.HasExited) {
            $null = $Proc.CloseMainWindow()
            $null = $Proc.WaitForExit(5000)
        }
        if (-not $Proc.HasExited) { $Proc.Kill() }
    } catch {}
}

try {
    $exe = Build-App
    $fxBin = Join-Path $tmp 'fx-bin'
    Build-FixtureBinaries $fxBin

    $hosts = Join-Path $tmp 'hosts.txt'
    '' | Set-Content $hosts -Encoding UTF8

    Write-Host '--- Pass A: Fresh launch, Avalonia v2.1 Customize drawer (5 tiers + Advanced), F15 single-instance, Step 1 checklist ---'
    $prefsA = Join-Path $tmp 'prefsA\forge.json'
    $p1 = Start-IsolatedLauncher $exe $prefsA $hosts
    $mainA = Find-Window $p1.Id 'Clonedivers'
    $setupBtn = Find-Button $mainA 'START GUIDED SETUP'
    $useExistingBtn = Find-Button $mainA 'Use Existing Forge…'
    Assert-True ($null -ne $setupBtn -and $null -ne $useExistingBtn) 'Fresh launch offers START GUIDED SETUP and Use Existing Forge…'

    # Open Customize slide-over drawer -> select Ultra-Potato (potato256) + expand Advanced -> All CIS Droids
    Invoke-Element (Find-ByAutomationId $mainA 'CustomizeOpenButton')
    Invoke-Element (Find-ByAutomationId $mainA 'TierRow-ultra-potato')
    Invoke-Element (Find-ByAutomationId $mainA 'CustomizeAdvancedToggle')
    Invoke-Element (Find-ByAutomationId $mainA 'EnemyPill-all-droids')
    Invoke-Element (Find-ByAutomationId $mainA 'CustomizeApplyButton')

    $null = Find-ByName $mainA 'All CIS Droids'
    $savedA = Get-Content $prefsA -Raw | ConvertFrom-Json
    Assert-True ($savedA.EnemySkin -eq 'all-droids' -and $savedA.Texture -eq 'potato256' -and $savedA.PerformanceTier -eq 'ultra-potato' -and -not $savedA.AutoPerformanceTier) `
        'Avalonia Customize drawer persists All CIS Droids + Ultra-Potato (potato256) to isolated forge.json'

    # F15: second instance signals running instance and exits immediately
    $pDup = Start-IsolatedLauncher $exe $prefsA $hosts
    Assert-True ($pDup.WaitForExit(8000) -and -not $p1.HasExited) 'F15 second instance exits cleanly without spawning a duplicate window'

    Invoke-Element (Find-Button $mainA 'START GUIDED SETUP')
    $null = Find-ByName $mainA 'Step 1 of 4 · Space & Steam'
    $null = Find-ByName $mainA '~70 GB'
    $null = Find-ByName $mainA 'Steam App'
    $btnsA = $mainA.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
    $hasSkipA = $false
    foreach ($b in $btnsA) { try { if ($b.Current.Name -match 'Skip|Fresh') { $hasSkipA = $true } } catch {} }
    Assert-True (-not $hasSkipA) 'Embedded Setup Step 1 never exposes Skip or Fresh-character bypass'
    Invoke-Element (Find-ByAutomationId $mainA 'SetupBackButton')
    $null = Find-Button $mainA 'START GUIDED SETUP'
    Stop-LauncherCleanly $p1

    Write-Host '--- Pass B: End-to-End 4-Step Embedded Setup (Step 2 QR -> Step 3 Mandatory Sync & F10 -> Step 4 -> F13 -> Launch & Stop) ---'
    $forgeRoot = Join-Path $tmp 'forge-root'
    Seed-ForgeRoot $forgeRoot $fxBin $false
    $prefsB = Join-Path $tmp 'prefsB\forge.json'
    New-Item -ItemType Directory -Force (Split-Path $prefsB) | Out-Null
    @{ Format = 1; InstallRoot = $forgeRoot; Mode = 'regular'; DeltaSquad = $true; Texture = 'full'; AutoPerformanceTier = $false; PerformanceTier = 'ultra'; Droids = $true; EnemySkin = 'mixed-droids'; Covenant = $true; SquidSkin = 'mixed-covenant' } |
        ConvertTo-Json | Set-Content $prefsB -Encoding UTF8

    $p2 = Start-IsolatedLauncher $exe $prefsB $hosts
    $mainB = Find-Window $p2.Id 'Clonedivers'
    Invoke-Element (Find-Button $mainB 'CONTINUE GUIDED SETUP')

    # Embedded setup opens directly at Step 2 when Forge is present without gameCopy, or Step 1 CONTINUE GAME COPY
    $step2Or1 = $null
    $swStep = [Diagnostics.Stopwatch]::StartNew()
    while ($swStep.ElapsedMilliseconds -lt 10000 -and $null -eq $step2Or1) {
        $condPrimary = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SetupPrimaryButton')
        $cand = $mainB.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condPrimary)
        if ($null -ne $cand -and $cand.Current.IsEnabled -and ($cand.Current.Name -eq 'RETRY GAME SETUP' -or $cand.Current.Name -eq 'CONTINUE GAME COPY')) {
            $step2Or1 = $cand
        } else {
            Start-Sleep -Milliseconds 120
        }
    }
    Assert-True ($null -ne $step2Or1) 'Embedded Setup offers game-copy action for incomplete Forge install'
    $null = Find-ByName $mainB 'Having trouble with QR? Open official HellForge setup'
    Invoke-Element $step2Or1

    # Step 2 runs steam-download (emits QR + flips gameCopy=true) and auto-advances to Step 3
    $null = Find-ByName $mainB 'Step 3 of 4' 20000
    Assert-True ($mainB.Current.BoundingRectangle.Height -le [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Height) 'Window height fits within WorkingArea during QR & Step 3 flow'
    $syncBtn = Find-Button $mainB 'OPEN HELLFORGE TO SYNC'
    $btnsB3 = $mainB.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
    $hasSkipB = $false
    foreach ($b in $btnsB3) { try { if ($b.Current.Name -match 'Skip|Fresh') { $hasSkipB = $true } } catch {} }
    Assert-True ($null -ne $syncBtn -and -not $hasSkipB) 'Step 3 arrives automatically after Step 2 and enforces mandatory Steam Account Sync'

    # Trigger sync request (opens FakeForge HellForge.exe -> exits 0 -> syncRequested=true)
    Invoke-Element $syncBtn
    $null = Find-ByName $mainB 'HellForge opened.' 10000

    # F10 cleanup detection inside Step 3
    '127.0.0.1 api.helldivers2.com # HellForge-CaptureProxy' | Set-Content $hosts -Encoding UTF8
    $cleanBtn = Find-Button $mainB 'OPEN HELLFORGE TO CLEAN UP' 10000
    Assert-True ($null -ne $cleanBtn) 'F10 hosts cleanup lock switches Step 3 button to OPEN HELLFORGE TO CLEAN UP'

    # Clear hosts & provide sealed HD2 capture -> auto-import -> Step 4
    '' | Set-Content $hosts -Encoding UTF8
    Write-SealedCapture $forgeRoot
    $installModsBtn = Find-Button $mainB 'INSTALL MODS & FINISH' 20000
    $null = Find-ByName $mainB 'Step 4 of 4'
    Assert-True ($null -ne $installModsBtn) 'Sealed capture auto-imports into Step 4 and offers INSTALL MODS & FINISH'
    Invoke-Element $installModsBtn

    $finishBtn = Find-Button $mainB 'FINISH & GO TO LAUNCHER' 20000
    $null = Find-ByName $mainB 'Step 4 of 4 · Ready to Play!'
    Assert-True ($null -ne $finishBtn) 'Step 4 installs 18-mod preset & Regular configuration and unlocks FINISH & GO TO LAUNCHER'
    Invoke-Element $finishBtn

    # Main window ready to launch + F13 first-launch firewall notice
    $launchBtn = Find-Button $mainB 'LAUNCH REGULAR HELLFORGE' 15000
    $null = Find-ByName $mainB 'Windows may ask for network approval once'
    Assert-True ($null -ne $launchBtn) 'Returning from Setup updates Home immediately with F13 first-launch firewall notice'

    # Launch -> END SESSION -> F13 cleared
    Invoke-Element $launchBtn
    $endBtn = Find-Button $mainB 'END SESSION' 15000
    Assert-True ($null -ne $endBtn) 'Launching transitions cleanly to HellForge session running (END SESSION)'
    Invoke-Element $endBtn
    $relaunchBtn = Find-Button $mainB 'LAUNCH REGULAR HELLFORGE' 15000
    $null = Find-ByName $mainB 'Session ended and HellForge cleanup is complete.'
    Assert-True ($null -ne $relaunchBtn -and (Describe-Elements $mainB) -notlike '*Windows may ask for network approval once*') 'END SESSION completes cleanup and clears F13 first-launch firewall notice once HasRun=true'
    Stop-LauncherCleanly $p2

    Write-Host '--- Pass C: F6 Fast Crash, F2 Incompatible Forge, F1 Missing Forge ---'
    # F6: Fast crash status never masked
    $st = Get-Content (Join-Path $forgeRoot 'fixture-state.json') -Raw | ConvertFrom-Json
    $st.lastStatus = 'failed'; $st.lastFailure = 'Process exited 42'
    $st | ConvertTo-Json | Set-Content (Join-Path $forgeRoot 'fixture-state.json') -Encoding UTF8
    $p3 = Start-IsolatedLauncher $exe $prefsB $hosts
    $mainC = Find-Window $p3.Id 'Clonedivers'
    $null = Find-ByName $mainC 'Last session ended unexpectedly'
    Assert-True ($true) 'F6 failed last session status surfaces Last session ended unexpectedly cleanly'
    Stop-LauncherCleanly $p3

    # F2: Unsupported Forge version offers CONTINUE GUIDED SETUP -> Step 1 INSTALL FORGE & COPY GAME (separate root)
    '{"version":"0.5.0-alpha","engine":"0.2.0","modSdk":16}' | Set-Content (Join-Path $forgeRoot 'app\app-build.json') -Encoding UTF8
    $p4 = Start-IsolatedLauncher $exe $prefsB $hosts
    $mainD = Find-Window $p4.Id 'Clonedivers'
    $null = Find-ByName $mainD 'HellForge setup needs attention'
    $contIncompatBtn = Find-Button $mainD 'CONTINUE GUIDED SETUP'
    Invoke-Element $contIncompatBtn
    $installSepBtn = Find-Button $mainD 'INSTALL FORGE & COPY GAME'
    Assert-True ($null -ne $installSepBtn) 'F2 unsupported Forge version routes to Step 1 INSTALL FORGE & COPY GAME on a separate root'
    Stop-LauncherCleanly $p4

    # F1: Deleted install root offers RUN GUIDED SETUP + Locate Folder…
    $deleted = $false
    for ($i = 0; $i -lt 20 -and -not $deleted; $i++) {
        try {
            Remove-Item $forgeRoot -Recurse -Force -ErrorAction Stop
            $deleted = $true
        } catch {
            Start-Sleep -Milliseconds 150
        }
    }
    if (-not $deleted) { Remove-Item $forgeRoot -Recurse -Force -ErrorAction Stop }
    $p5 = Start-IsolatedLauncher $exe $prefsB $hosts
    $mainE = Find-Window $p5.Id 'Clonedivers'
    $null = Find-ByName $mainE 'HellForge folder not found'
    $againBtn = Find-Button $mainE 'RUN GUIDED SETUP'
    $locateBtn = Find-Button $mainE 'Locate Folder…'
    Assert-True ($null -ne $againBtn -and $null -ne $locateBtn) 'F1 missing saved Forge folder offers RUN GUIDED SETUP and Locate Folder…'
    Stop-LauncherCleanly $p5

    Write-Host 'ALL UIA SINGLE-WIZARD SMOKE CHECKS PASSED.'
}
finally {
    foreach ($p in $procs) {
        try { if (-not $p.HasExited) { $p.Kill() } } catch {}
        $p.Dispose()
    }
    if (Test-Path -LiteralPath $tmp) {
        try { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }
}
