# Authenticode-signs the given files with the internal Blitztext code-signing certificate
# (created by new-cert.ps1) and an RFC3161 timestamp. Uses Set-AuthenticodeSignature, so no
# Windows SDK / signtool is required.
#
# Usage: .\sign.ps1 file1.exe [file2.exe ...]

param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$Files
)

$ErrorActionPreference = "Stop"
$subject = "CN=Blitztext Code Signing (Sebastian Schutzbach)"
$timestampUrl = "http://timestamp.digicert.com"

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
    Select-Object -First 1

if (-not $cert) {
    throw "Kein Signaturzertifikat gefunden. Zuerst new-cert.ps1 ausfuehren."
}

foreach ($f in $Files) {
    if (-not (Test-Path $f)) { Write-Warning "nicht gefunden: $f"; continue }
    $r = Set-AuthenticodeSignature -FilePath $f -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $timestampUrl
    Write-Host ("{0,-10} {1}" -f $r.Status, $f)
    if ($r.Status -ne "Valid") { Write-Warning $r.StatusMessage }
}
