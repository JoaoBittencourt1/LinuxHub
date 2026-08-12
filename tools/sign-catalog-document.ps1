#requires -Version 7.0
<#
.SYNOPSIS
    Assina um documento de catálogo do LinuxHub (task 7.7).

.DESCRIPTION
    Roda SÓ no pipeline de release (CI) — nunca numa máquina de desenvolvedor. A chave privada
    tem que existir apenas como secret do pipeline (D8/task 2.2); passá-la como arquivo em
    disco de dev anularia a garantia inteira da assinatura.

    Produz a assinatura destacada, em base64, no mesmo esquema que
    Common/Data/CatalogSignatureVerifier.cs verifica: RSASSA-PKCS1-v1_5 sobre SHA-256.
    Common/Data/CatalogClient.cs espera exatamente esse formato na URL de assinatura (task 2.1).

    Requer PowerShell 7+: RSA.ImportFromPem() não existe no Windows PowerShell 5.1.

.PARAMETER DocumentPath
    Caminho do JSON do catálogo, gerado por tools/CatalogPublisher.

.PARAMETER PrivateKeyPem
    Conteúdo PEM (PKCS8) da chave privada RSA. Vem de um secret do pipeline — nunca de um
    arquivo versionado.

.PARAMETER SignaturePath
    Onde gravar a assinatura em base64.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DocumentPath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPem,

    [Parameter(Mandatory = $true)]
    [string]$SignaturePath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $DocumentPath)) {
    throw "Document not found: $DocumentPath"
}

$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($PrivateKeyPem)

    $documentBytes = [System.IO.File]::ReadAllBytes($DocumentPath)
    $signatureBytes = $rsa.SignData(
        $documentBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

    [System.IO.File]::WriteAllText($SignaturePath, [Convert]::ToBase64String($signatureBytes))
}
finally {
    # A chave só vive na memória deste processo; nunca é gravada em disco por este script.
    $rsa.Dispose()
}

Write-Output "Assinado: $SignaturePath"
