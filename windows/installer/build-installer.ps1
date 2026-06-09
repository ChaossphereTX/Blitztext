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

Write-Host "1/2  Publish (self-contained win-x64) ..." -ForegroundColor Cyan
$pubArgs = @('publish', $proj, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false')
if ($Version) { $pubArgs += "-p:Version=$Version" }
dotnet @pubArgs | Out-Null

# Locate the Inno Setup compiler.
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe" |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) nicht gefunden. Installieren: winget install JRSoftware.InnoSetup" }

Write-Host "2/2  Compile Inno Setup ..." -ForegroundColor Cyan
$isccArgs = @("/DPublishDir=$publish")
if ($Version) { $isccArgs += "/DMyAppVersion=$Version" }
& $iscc @isccArgs (Join-Path $here "Blitztext.iss")

$setup = Join-Path $here "Output\BlitztextSetup.exe"
Write-Host "Fertig: $setup" -ForegroundColor Green
