#requires -Version 7.0
<#
.SYNOPSIS
    Generates the RSA-3072 keypair used to sign LinuxHub's distribution catalog.

.DESCRIPTION
    Run this once, on the machine/service that will own catalog signing going forward — never
    on a developer workstation that also holds the source tree. The private key file this
    produces must go straight into the signing pipeline's secret store and nowhere else:
    committing it, emailing it, or leaving it on disk after the signing service picks it up
    defeats the entire point of the catalog signature (D8).

    Requires PowerShell 7+: RSA.ExportSubjectPublicKeyInfoPem()/ExportPkcs8PrivateKeyPem() are
    not available on Windows PowerShell 5.1's older .NET Framework runtime.

.OUTPUTS
    catalog-signing-public.pem  — paste into Common/Data/CatalogPublicKey.cs (replaces the
                                  development placeholder there) and commit it; it is meant to
                                  be public.
    catalog-signing-private.pem — secret. Store only in the signing pipeline's secret manager.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Get-Location).Path
)

$ErrorActionPreference = "Stop"

$publicPath = Join-Path $OutputDirectory "catalog-signing-public.pem"
$privatePath = Join-Path $OutputDirectory "catalog-signing-private.pem"

if ((Test-Path $publicPath) -or (Test-Path $privatePath)) {
    throw "Refusing to overwrite an existing keypair at '$OutputDirectory'. Move or remove the existing files first — generating a new key silently would invalidate the catalog trust of every already-shipped executable."
}

$rsa = [System.Security.Cryptography.RSA]::Create(3072)
try {
    $publicPem = $rsa.ExportSubjectPublicKeyInfoPem()
    $privatePem = $rsa.ExportPkcs8PrivateKeyPem()
}
finally {
    $rsa.Dispose()
}

Set-Content -Path $publicPath -Value $publicPem -Encoding ascii -NoNewline
Set-Content -Path $privatePath -Value $privatePem -Encoding ascii -NoNewline

Write-Output "Generated:"
Write-Output "  $publicPath  (commit this — replaces the placeholder in CatalogPublicKey.cs)"
Write-Output "  $privatePath  (secret — move into the signing pipeline's secret store now, then delete this file)"
