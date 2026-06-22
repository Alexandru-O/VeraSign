# MasterSTI / VeraSign — Boot two Android emulators + deploy Thea + Toma wallets in parallel.
# Companion to start-all.ps1 (which runs API/Web/QTSP). Phase 6 of docs/two-wallet-demo-plan.md.
#
# Usage:
#   .\start-wallets.ps1                       # boot both emulators + build+deploy both APKs
#   .\start-wallets.ps1 -SkipBoot             # assume emulators already running on 5554+5556
#   .\start-wallets.ps1 -OnlyThea             # boot+deploy Thea only (emulator-5554, VeraSign_Pixel6_Thea)
#   .\start-wallets.ps1 -OnlyToma             # boot+deploy Toma only (emulator-5556, VeraSign_Pixel6_Toma)
#   .\start-wallets.ps1 -Stop                 # kill emulators + close wallet build windows
#   .\start-wallets.ps1 -AndroidHome C:\Sdk   # override SDK location (else env:ANDROID_HOME, else probe)

param(
    [switch]$SkipBoot,
    [switch]$OnlyThea,
    [switch]$OnlyToma,
    [switch]$Stop,
    [string]$AndroidHome
)

$root = $PSScriptRoot
$ErrorActionPreference = 'Stop'

# Persona → AVD + port + serial. AVD names match what's installed on this machine
# (see CLAUDE.md / docs/two-wallet-demo-plan.md). Adjust if you renamed AVDs locally.
$personas = @(
    @{ Name='Thea'; Avd='VeraSign_Pixel6_Thea'; Port=5554; Serial='emulator-5554' },
    @{ Name='Toma'; Avd='VeraSign_Pixel6_Toma'; Port=5556; Serial='emulator-5556' }
)

if ($OnlyThea) { $personas = $personas | Where-Object Name -EQ 'Thea' }
if ($OnlyToma) { $personas = $personas | Where-Object Name -EQ 'Toma' }

# Resolve Android SDK. Priority: -AndroidHome param > $env:ANDROID_HOME > probe known paths.
# Probe paths cover both author machines (C:\Android vs default LOCALAPPDATA install).
function Resolve-AndroidHome {
    param([string]$Override)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($Override)          { $candidates.Add($Override) }
    if ($env:ANDROID_HOME)  { $candidates.Add($env:ANDROID_HOME) }
    if ($env:ANDROID_SDK_ROOT) { $candidates.Add($env:ANDROID_SDK_ROOT) }
    $candidates.Add('C:\Android')
    if ($env:LOCALAPPDATA)  { $candidates.Add((Join-Path $env:LOCALAPPDATA 'Android\Sdk')) }
    $candidates.Add('C:\Program Files\Android\Android Studio\sdk')
    $candidates.Add('C:\Program Files (x86)\Android\android-sdk')

    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'emulator\emulator.exe'))) { return $c }
    }
    return $null
}

$androidHome = Resolve-AndroidHome -Override $AndroidHome
if (-not $androidHome) {
    throw "Android SDK not found. Pass -AndroidHome <path>, or set `$env:ANDROID_HOME. Probed: -AndroidHome, ANDROID_HOME, ANDROID_SDK_ROOT, C:\Android, %LOCALAPPDATA%\Android\Sdk."
}
Write-Host "Android SDK: $androidHome" -ForegroundColor DarkGray
$emulatorExe = Join-Path $androidHome 'emulator\emulator.exe'
$adbExe      = Join-Path $androidHome 'platform-tools\adb.exe'

function Stop-Wallets {
    Write-Host "Stopping VeraSign wallet windows + emulators..." -ForegroundColor Cyan

    # Close build-stream windows (titled "VeraSign Wallet — *")
    Get-Process cmd -ErrorAction SilentlyContinue | Where-Object {
        $_.MainWindowTitle -like 'VeraSign Wallet*'
    } | Stop-Process -Force -ErrorAction SilentlyContinue

    # Kill emulator processes (qemu-system-* spawned by emulator.exe)
    Get-Process emulator, qemu-system-x86_64, qemu-system-aarch64 -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    # Sweep leftover temp .cmd files written by Start-Wallet-Window in prior runs.
    Get-ChildItem -Path $env:TEMP -Filter 'verasign-wallet-*.cmd' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Start-Sleep -Milliseconds 500
}

function Start-Wallet-Window {
    param(
        [string]$Persona,    # Thea or Toma
        [string]$Serial      # emulator-5554 / 5556
    )
    # Mirror start-all.ps1: write temp .cmd that runs dotnet build + leaves window open
    # so build stream + Run output stay visible after process exits.
    $title  = "VeraSign Wallet - $Persona ($Serial)"
    $csproj = Join-Path $root 'mobile\MasterSTI.Wallet\MasterSTI.Wallet.csproj'

    # Per-persona obj/ + bin/ isolation lives in Directory.Build.props (scoped
    # to MSBuildProjectName=MasterSTI.Wallet). Passing the paths on the CLI here
    # would cascade into ProjectReference'd MasterSTI.Shared and break its
    # DefaultItemExcludes (duplicate AssemblyInfo from stale obj/Release).
    # --no-dependencies skips rebuilding MasterSTI.Shared — parent script does
    # that once serially before spawning these two windows, so both wallet
    # builds can run in parallel without racing on Shared.dll.
    $envLines = @(
        "@echo off",
        "title $title",
        "cd /d `"$root`"",
        "dotnet build `"$csproj`" -f net10.0-android -t:Run --no-dependencies -p:WalletPersona=$Persona -p:AdbTarget=`"-s $Serial`" -nodeReuse:false",
        "echo.",
        "echo [build/deploy finished — press any key to close]",
        "pause"
    )
    $tmp = Join-Path $env:TEMP ("verasign-wallet-" + [Guid]::NewGuid().ToString("N").Substring(0,8) + ".cmd")
    Set-Content -Path $tmp -Value ($envLines -join "`r`n") -Encoding ASCII
    Start-Process cmd.exe -ArgumentList "/C", "`"$tmp`""
}

function Remove-LegacyApk {
    param([string]$Serial)
    # Pre-persona builds shipped as com.mastersti.wallet with hardcoded "Alex Popescu" on
    # the onboarding PID card. New persona APKs use ro.verasign.wallet.{thea,toma} so the
    # old package coexists silently and stays openable from the launcher. Sweep it before
    # each deploy so demos never accidentally show the legacy card.
    $legacyId = 'com.mastersti.wallet'
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $installed = & $adbExe -s $Serial shell pm list packages $legacyId 2>$null
        if ($installed -match [regex]::Escape($legacyId)) {
            Write-Host "  Removing legacy $legacyId from $Serial..." -ForegroundColor DarkYellow
            & $adbExe -s $Serial uninstall $legacyId *> $null
        }
    } finally {
        $ErrorActionPreference = $prevEAP
    }
}

function Boot-Emulator {
    param([string]$Avd, [int]$Port, [string]$Serial)
    Write-Host "  Booting $Avd on port $Port ($Serial)..." -ForegroundColor Yellow
    # -no-snapshot disables BOTH load and save (vs -no-snapshot-save which still loads). True cold boot
    # every demo run: deterministic state, prevents re-poisoning if a snapshot is ever saved in a broken
    # render state (which is how the earlier "permanent black screen" trap formed — see git history).
    # -gpu host uses the Win11 host GPU via ANGLE. Required for cheap rendering of the OnboardingPage
    # shimmer animation (TranslateToAsync loop); software GL (swiftshader_indirect) saturates the UI
    # thread on Pixel6 + 1080×2400 and triggers an ANR. AVDs ship with hw.gpu.enabled=no in config.ini,
    # so this flag is the only thing keeping the Android compositor alive — without it the screen stays
    # black forever and boot_completed never flips.
    Start-Process -FilePath $emulatorExe `
        -ArgumentList @("-avd", $Avd, "-port", $Port, "-no-snapshot", "-gpu", "host") `
        -WindowStyle Minimized | Out-Null
}

function Wait-Emulator-Boot {
    param([string]$Serial, [int]$TimeoutSec = 180)
    Write-Host "  Waiting for $Serial to finish boot..." -ForegroundColor Yellow
    # PS 5.1 treats native-cmd stderr as ErrorRecord under ErrorActionPreference=Stop.
    # Soft-scope adb calls to Continue so daemon-start banners don't abort the script.
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $adbExe -s $Serial wait-for-device *> $null
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
            $boot = & $adbExe -s $Serial shell getprop sys.boot_completed 2>$null
            if ($boot -match '1') { Write-Host "  [OK] $Serial booted" -ForegroundColor Green; return $true }
            Start-Sleep -Seconds 2
        }
    } finally {
        $ErrorActionPreference = $prevEAP
    }
    Write-Host "  [FAIL] $Serial boot timeout ($TimeoutSec s)" -ForegroundColor Red
    return $false
}

if ($Stop) { Stop-Wallets; return }

# Sanity check toolchain
if (-not (Test-Path $emulatorExe)) { throw "emulator.exe not found at $emulatorExe (set ANDROID_HOME or edit script)" }
if (-not (Test-Path $adbExe))      { throw "adb.exe not found at $adbExe" }

if (-not $SkipBoot) {
    Stop-Wallets
    foreach ($p in $personas) { Boot-Emulator -Avd $p.Avd -Port $p.Port -Serial $p.Serial }
    Write-Host ""
    foreach ($p in $personas) {
        if (-not (Wait-Emulator-Boot -Serial $p.Serial)) {
            throw "Emulator $($p.Serial) failed to boot — abort before deploy"
        }
    }
}

Write-Host ""
Write-Host "Sweeping legacy com.mastersti.wallet APKs..." -ForegroundColor Cyan
foreach ($p in $personas) { Remove-LegacyApk -Serial $p.Serial }

# Pre-build MasterSTI.Shared once, serially, so the two parallel wallet builds
# don't race on src/MasterSTI.Shared/obj/Debug/net10.0/MasterSTI.Shared.dll.
# Wallet builds use --no-dependencies and re-reference this artifact.
Write-Host ""
Write-Host "Pre-building MasterSTI.Shared (serial, shared by both wallets)..." -ForegroundColor Cyan
$sharedCsproj = Join-Path $root 'src\MasterSTI.Shared\MasterSTI.Shared.csproj'
& dotnet build $sharedCsproj -nodeReuse:false
if ($LASTEXITCODE -ne 0) {
    throw "MasterSTI.Shared pre-build failed — abort before parallel wallet builds."
}

Write-Host ""
Write-Host "Starting parallel wallet builds + deploys..." -ForegroundColor Cyan
foreach ($p in $personas) {
    Write-Host "  $($p.Name)  -> $($p.Serial)"
    Start-Wallet-Window -Persona $p.Name -Serial $p.Serial
}

Write-Host ""
Write-Host "Two cmd windows opened — each runs:" -ForegroundColor Gray
Write-Host "  dotnet build mobile\MasterSTI.Wallet\MasterSTI.Wallet.csproj -f net10.0-android -t:Run -p:WalletPersona=<Persona> -p:AdbTarget=`"-s <serial>`" -nodeReuse:false" -ForegroundColor Gray
Write-Host ""
Write-Host "When Onboarding shows 'Bun venit, Thea' / 'Bun venit, Toma', tap Continua + enroll on each." -ForegroundColor Yellow
Write-Host "Make sure API + Web + QTSP are running (start-all.ps1 -Publish -Public https://10.0.2.2:7001)." -ForegroundColor Yellow
