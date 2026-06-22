# MasterSTI / VeraSign - Start all services for demo
# Usage:
#   .\start-all.ps1                              # run existing published build (localhost only)
#   .\start-all.ps1 -Publish                     # rebuild + publish + run
#   .\start-all.ps1 -Stop                        # stop running services
#   .\start-all.ps1 -Publish -Open               # rebuild, run, open browser
#   .\start-all.ps1 -Public https://10.0.2.2:7001  # bind 0.0.0.0 + set PublicBaseUrl for emulator QR

param(
    [switch]$Publish,
    [switch]$Stop,
    [switch]$Open,
    [string]$Public = ""
)

$root = $PSScriptRoot
$ErrorActionPreference = 'Stop'

function Stop-Services {
    Write-Host "Stopping VeraSign processes..." -ForegroundColor Cyan

    # Legacy apphost names (pre-dotnet-exec script). Harmless if absent.
    Get-Process MasterSTI.Api, MasterSTI.Web, MasterSTI.Mock.Qtsp -ErrorAction SilentlyContinue | Stop-Process -Force

    # New path: services run as `dotnet exec App.dll` (or `dotnet run --project` for Issuer).
    # Match dotnet processes whose command line references our DLLs/csprojs and kill those
    # (avoid killing unrelated dotnet processes).
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -match 'publish\\(api|web|qtsp)\\MasterSTI\.' -or
        $_.CommandLine -match 'MasterSTI\.Mock\.Issuer'
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    # Also close the leftover "VeraSign *" cmd.exe windows (which sit at `pause` after the process exits).
    Get-Process cmd -ErrorAction SilentlyContinue | Where-Object {
        $_.MainWindowTitle -like 'VeraSign *'
    } | Stop-Process -Force -ErrorAction SilentlyContinue

    # Sweep leftover temp .cmd files written by Start-Service-Window in prior runs.
    Get-ChildItem -Path $env:TEMP -Filter 'verasign-*.cmd' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Start-Sleep -Milliseconds 500
}

function Wait-Port {
    param([int]$Port, [string]$Name, [int]$TimeoutSec = 60)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $hit = Test-NetConnection -ComputerName localhost -Port $Port -WarningAction SilentlyContinue -InformationLevel Quiet
        if ($hit) { Write-Host "  [OK] $Name  :$Port" -ForegroundColor Green; return }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "  [FAIL] $Name  :$Port (timeout)" -ForegroundColor Red
}

function Start-Service-Window {
    param(
        [string]$Title,
        [string]$WorkDir,
        [string]$Dll,
        [string]$SourceProject,
        [string]$Urls,
        [hashtable]$ExtraEnv = @{}
    )
    # Launch via the signed `dotnet exec` host instead of the unsigned apphost .exe shim.
    # On Win11 with Smart App Control (or WDAC), the apphost .exe is reputation-blocked
    # (FileLoadException 0x800711C7) but dotnet.exe (Microsoft-signed) loads the DLL fine.
    # cmd /K keeps the window open after the process exits.
    #
    # If -SourceProject is passed, run via `dotnet run --project` instead. Reason: every
    # republish produces a new DLL hash; SAC reputation-blocks Mock.Issuer specifically
    # (it's the only published DLL that changes during normal dev work because it's
    # actively iterated as a mock). Source build via `dotnet run` JITs through Microsoft-
    # signed dotnet.exe against bin/Debug, which SAC treats more permissively.
    $envLines = @("@echo off", "title $Title", "set ASPNETCORE_ENVIRONMENT=Development")
    foreach ($k in $ExtraEnv.Keys) { $envLines += "set $k=$($ExtraEnv[$k])" }
    $envLines += "cd /d `"$WorkDir`""
    if ($SourceProject) {
        $envLines += "dotnet run --project `"$SourceProject`" --no-launch-profile --urls `"$Urls`""
    } else {
        $envLines += "dotnet exec `"$Dll`" --urls `"$Urls`""
    }
    $envLines += "echo."
    $envLines += "echo [process exited]"
    $envLines += "pause"

    $tmp = Join-Path $env:TEMP ("verasign-" + [Guid]::NewGuid().ToString("N").Substring(0,8) + ".cmd")
    Set-Content -Path $tmp -Value ($envLines -join "`r`n") -Encoding ASCII
    Start-Process cmd.exe -ArgumentList "/C", "`"$tmp`""
}

if ($Stop) { Stop-Services; return }

Stop-Services

if ($Publish) {
    Write-Host "Publishing all projects..." -ForegroundColor Cyan
    dotnet publish "$root\src\MasterSTI.Api"          -c Release -r win-x64 --self-contained false -o "$root\publish\api"  --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "API publish failed" }
    dotnet publish "$root\src\MasterSTI.Web"          -c Release -r win-x64 --self-contained false -o "$root\publish\web"  --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Web publish failed" }
    dotnet publish "$root\tests\MasterSTI.Mock.Qtsp"  -c Release -r win-x64 --self-contained false -o "$root\publish\qtsp" --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "QTSP publish failed" }
    # Mock.Issuer intentionally NOT published — runs from source via `dotnet run` (see
    # Start-Service-Window comment). Avoids the SAC reputation-block class of error.

    # Inject mock CSC creds into published API config (source appsettings.json stays clean per CLAUDE.md)
    $apiCfg = "$root\publish\api\appsettings.json"
    $cfg = Get-Content $apiCfg -Raw | ConvertFrom-Json
    if (-not $cfg.CscApi.Username)     { $cfg.CscApi | Add-Member -NotePropertyName Username     -NotePropertyValue 'mock-user'           -Force }
    if (-not $cfg.CscApi.Password)     { $cfg.CscApi | Add-Member -NotePropertyName Password     -NotePropertyValue 'mock-pass'           -Force }
    if (-not $cfg.CscApi.CredentialId) { $cfg.CscApi | Add-Member -NotePropertyName CredentialId -NotePropertyValue 'mock-credential-001' -Force }

    # Inject demo JWT signing key (kept out of git for the same reason as CscApi creds).
    # Real deployments override via env var Jwt__Signing__Key or user-secrets.
    if (-not $cfg.Jwt) { $cfg | Add-Member -NotePropertyName Jwt -NotePropertyValue ([pscustomobject]@{}) -Force }
    if (-not $cfg.Jwt.Signing) { $cfg.Jwt | Add-Member -NotePropertyName Signing -NotePropertyValue ([pscustomobject]@{}) -Force }
    if (-not $cfg.Jwt.Signing.Key) { $cfg.Jwt.Signing | Add-Member -NotePropertyName Key -NotePropertyValue 'demo-mastersti-signing-key-32+chars-rotate-in-prod' -Force }

    # ADR-0011: inject demo OID4VP request_object signing key (EC P-256, ES256).
    # Public half is pinned in mobile/MasterSTI.Wallet/Services/WalletConfig.cs.
    # Rotation = generate new pair (openssl ecparam ...) and ship a new wallet APK.
    if (-not $cfg.Eudiw) { $cfg | Add-Member -NotePropertyName Eudiw -NotePropertyValue ([pscustomobject]@{}) -Force }
    if (-not $cfg.Eudiw.RequestObjectSigning) {
        $cfg.Eudiw | Add-Member -NotePropertyName RequestObjectSigning -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    if (-not $cfg.Eudiw.RequestObjectSigning.PrivateKeyPem) {
        $demoRqoPriv = @'
-----BEGIN PRIVATE KEY-----
MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgS3FTgvncFIYV9dLP
3LWKUkkUgZCKtNJoAHtHgLSGMtmhRANCAAQ5B4L3ymu6juqhjnslNox1IKoJUzti
egrs0nmkjkkywJvgnQrtQn4uaPX2Vj5ZM0WX7UKRxCr1nkBEDlTgTglw
-----END PRIVATE KEY-----
'@
        $cfg.Eudiw.RequestObjectSigning | Add-Member -NotePropertyName PrivateKeyPem -NotePropertyValue $demoRqoPriv -Force
    }
    if (-not $cfg.Eudiw.RequestObjectSigning.Kid) {
        $cfg.Eudiw.RequestObjectSigning | Add-Member -NotePropertyName Kid -NotePropertyValue 'verasign-rqo-v1' -Force
    }
    if (-not $cfg.Eudiw.RequestObjectSigning.ExpiresInSeconds) {
        $cfg.Eudiw.RequestObjectSigning | Add-Member -NotePropertyName ExpiresInSeconds -NotePropertyValue 300 -Force
    }

    $cfg | ConvertTo-Json -Depth 10 | Set-Content $apiCfg -Encoding UTF8

    # Strip Mark-of-the-Web from all published artifacts so Smart App Control / SmartScreen
    # does not reputation-block any DLL or apphost .exe on first launch.
    Write-Host "Unblocking published binaries..." -ForegroundColor Cyan
    Get-ChildItem "$root\publish" -Recurse -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

    Write-Host "Publish complete." -ForegroundColor Green
}

$usePublic = -not [string]::IsNullOrWhiteSpace($Public)

if ($usePublic) {
    $qtspUrls   = "https://0.0.0.0:7111"
    $issuerUrls = "https://0.0.0.0:7112"
    $apiUrls    = "https://0.0.0.0:7001"
    $webUrls    = "https://0.0.0.0:7165"
    Write-Host ""
    Write-Host "Public mode - binding 0.0.0.0 (reachable from Android emulator via 10.0.2.2)" -ForegroundColor Yellow
    Write-Host "  PublicBaseUrl: $Public" -ForegroundColor Yellow
} else {
    $qtspUrls   = "https://localhost:7111"
    $issuerUrls = "https://localhost:7112"
    $apiUrls    = "https://localhost:7001"
    $webUrls    = "https://localhost:7165"
}

Write-Host ""
Write-Host "Starting VeraSign services..." -ForegroundColor Cyan
Write-Host "  Mock QTSP   : $qtspUrls"
Write-Host "  Mock Issuer : $issuerUrls"
Write-Host "  API         : $apiUrls  (Swagger: https://localhost:7001/swagger)"
Write-Host "  Web         : $webUrls"
Write-Host ""

$apiExtraEnv = @{}
if ($usePublic) { $apiExtraEnv["Eudiw__PublicBaseUrl"] = $Public }

# Mock QTSP — pure CSC v2 QTSP. No EUDIW simulator env vars (ADR-0005: simulators
# now live on Mock.Issuer). ASPNETCORE_URLS only when binding 0.0.0.0 for emulator.
$qtspExtraEnv = @{}
if ($usePublic) { $qtspExtraEnv["ASPNETCORE_URLS"] = $qtspUrls }

# Mock Issuer — standalone EUDIW Issuer (ADR-0005). Owns its own DB (MasterSTI_Issuer),
# applies migrations on startup. ASPNETCORE_ENVIRONMENT=Development (set by Start-Service-Window)
# selects the localdb connection string from appsettings.Development.json.
# Also hosts the browser-based EUDIW wallet simulators, which need:
#   ApiBaseUrl  = server-to-server URL the EU Wallet simulator uses to POST the VP response
#   VerifierId  = `aud` claim baked into the simulator KB-JWT; must match the API's Eudiw:VerifierId
$issuerExtraEnv = @{
    "ApiBaseUrl" = "https://localhost:7001"
    "VerifierId" = "https://localhost:7001"
}
if ($usePublic) { $issuerExtraEnv["ASPNETCORE_URLS"] = $issuerUrls }

Start-Service-Window -Title "VeraSign Mock QTSP"   -WorkDir "$root\publish\qtsp"   -Dll "$root\publish\qtsp\MasterSTI.Mock.Qtsp.dll"     -Urls $qtspUrls   -ExtraEnv $qtspExtraEnv
Start-Service-Window -Title "VeraSign Mock Issuer" -WorkDir "$root\tests\MasterSTI.Mock.Issuer" -SourceProject "$root\tests\MasterSTI.Mock.Issuer\MasterSTI.Mock.Issuer.csproj" -Urls $issuerUrls -ExtraEnv $issuerExtraEnv
Start-Service-Window -Title "VeraSign API"         -WorkDir "$root\publish\api"    -Dll "$root\publish\api\MasterSTI.Api.dll"             -Urls $apiUrls    -ExtraEnv $apiExtraEnv
Start-Service-Window -Title "VeraSign Web"         -WorkDir "$root\publish\web"    -Dll "$root\publish\web\MasterSTI.Web.dll"             -Urls $webUrls

Write-Host "Waiting for services..." -ForegroundColor Cyan
Wait-Port -Port 7111 -Name "QTSP  "
Wait-Port -Port 7112 -Name "Issuer"
Wait-Port -Port 7001 -Name "API   "
Wait-Port -Port 7165 -Name "Web   "

Write-Host ""
Write-Host "All services running." -ForegroundColor Green
Write-Host "Demo entry: https://localhost:7165/welcome" -ForegroundColor Yellow
Write-Host "Login    : admin@verasign.demo / Demo!2025" -ForegroundColor Yellow

if ($Open) { Start-Process "https://localhost:7165/welcome" }
