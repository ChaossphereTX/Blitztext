# Builds the Blitztext per-user installer (BlitztextSetup.exe).
# Steps: publish self-contained -> compile Inno Setup script.
# Requires: .NET 8 SDK and Inno Setup 6 (ISCC.exe). Run from anywhere.
#
# -Version overrides the product/assembly version (CI passes it from the git tag), so the tag
# is the single source of truth and the installed version always matches the release.

param([string]$Version = "")

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..")   # windows/
$proj = Join-Path $repo "src\Blitztext.App\Blitztext.App.csproj"
$publish = Join-Path $repo "src\Blitztext.App\bin\Release\net8.0-windows\win-x64\publish"

$sign = Join-Path $repo "signing\sign.ps1"
$signCert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq "CN=Blitztext Code Signing (Sebastian Schutzbach)" -and $_.HasPrivateKey } |
    Select-Object -First 1

Write-Host "1/3  Publish (self-contained win-x64) ..." -ForegroundColor Cyan
$pubArgs = @('publish', $proj, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false')
if ($Version) { $pubArgs += "-p:Version=$Version" }
dotnet @pubArgs | Out-Null

if ($signCert) {
    Write-Host "2/3  Signiere Blitztext.exe ..." -ForegroundColor Cyan
    & $sign (Join-Path $publish "Blitztext.exe")
} else {
    Write-Host "2/3  (uebersprungen) kein Signaturzertifikat - signing/new-cert.ps1 ausfuehren" -ForegroundColor Yellow
}

# Locate the Inno Setup compiler.
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe" |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) nicht gefunden. Installieren: winget install JRSoftware.InnoSetup" }

Write-Host "3/3  Compile Inno Setup ..." -ForegroundColor Cyan
$isccArgs = @("/DPublishDir=$publish")
if ($Version) { $isccArgs += "/DMyAppVersion=$Version" }
& $iscc @isccArgs (Join-Path $here "Blitztext.iss")

$setup = Join-Path $here "Output\BlitztextSetup.exe"
if ($signCert) {
    Write-Host "     Signiere BlitztextSetup.exe ..." -ForegroundColor Cyan
    & $sign $setup
}

Write-Host "Fertig: $setup" -ForegroundColor Green
