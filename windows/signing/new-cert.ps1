# Creates a self-signed CODE SIGNING certificate for internal distribution of Blitztext,
# exports its PUBLIC part (.cer) for GPO/Intune deployment, and trusts it on THIS machine.
#
# Run once. The private key stays in the current user's certificate store (Cert:\CurrentUser\My)
# and is used by sign.ps1. To sign on another build machine, export a password-protected .pfx
# (see the comment at the bottom) and import it there.

$ErrorActionPreference = "Stop"
$subject = "CN=Blitztext Code Signing (Sebastian Schutzbach)"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cerPath = Join-Path $here "Blitztext-CodeSigning.cer"

$existing = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } |
    Select-Object -First 1

if ($existing) {
    Write-Host "Vorhandenes Zertifikat: $($existing.Thumbprint)" -ForegroundColor Yellow
    $cert = $existing
} else {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $subject `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -KeyAlgorithm RSA -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(10)
    Write-Host "Neues Zertifikat erstellt: $($cert.Thumbprint)" -ForegroundColor Green
}

# Export public certificate (.cer) for distribution via GPO / Intune.
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "Oeffentliches Zertifikat exportiert: $cerPath"

# Trust it on THIS machine (so signed binaries verify locally). On fleet machines this is done
# centrally via GPO/Intune into Trusted Root + Trusted Publishers.
foreach ($store in @("Root", "TrustedPublisher")) {
    $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($store, "CurrentUser")
    $s.Open("ReadWrite")
    $pub = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
    $s.Add($pub); $s.Close()
}
Write-Host "Lokal vertraut (CurrentUser\Root + TrustedPublisher)." -ForegroundColor Green
Write-Host "Thumbprint: $($cert.Thumbprint)"

# Optional: password-protected PFX (private key) for use on another build machine:
#   $pw = Read-Host -AsSecureString
#   Export-PfxCertificate -Cert $cert -FilePath (Join-Path $here 'Blitztext-CodeSigning.pfx') -Password $pw
