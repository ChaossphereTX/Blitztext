# Stores the internal code-signing certificate in GitHub Actions secrets so the Release
# workflow can sign builds. RUN THIS YOURSELF (once) — it touches your private signing key.
#
# What it does:
#   1. exports the cert (private key) to a password-protected PFX in your TEMP folder,
#   2. uploads it base64-encoded as the secret CODESIGN_PFX_BASE64,
#   3. uploads a freshly generated random password as CODESIGN_PFX_PASSWORD,
#   4. deletes the local PFX again.
# The private key never leaves your machine except as an encrypted GitHub secret.
#
# Prerequisites: signing/new-cert.ps1 already run; `gh auth login` with repo scope.

param([string]$Repo = "ChaossphereTX/Blitztext")

$ErrorActionPreference = "Stop"
$subject = "CN=Blitztext Code Signing (Sebastian Schutzbach)"

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) { throw "Signaturzertifikat nicht gefunden. Zuerst signing\new-cert.ps1 ausfuehren." }

# strong random password
$buf = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buf)
$pw = [Convert]::ToBase64String($buf)

$pfx = Join-Path $env:TEMP ("blitztext-cs-" + [Guid]::NewGuid().ToString('N') + ".pfx")
try {
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $pw -AsPlainText -Force) | Out-Null
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx))

    $b64 | gh secret set CODESIGN_PFX_BASE64   -R $Repo
    $pw  | gh secret set CODESIGN_PFX_PASSWORD -R $Repo

    Write-Host "Secrets gesetzt fuer $Repo :" -ForegroundColor Green
    gh secret list -R $Repo
}
finally {
    if (Test-Path $pfx) { Remove-Item $pfx -Force }
}
