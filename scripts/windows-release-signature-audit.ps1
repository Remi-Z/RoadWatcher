[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [string]$InstalledExecutable,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$ExpectedSignerThumbprint,

    [string]$EvidencePath = (Join-Path (Get-Location) 'roadwatcher-signature-evidence.json')
)

$ErrorActionPreference = 'Stop'
$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$resolvedExecutable = (Resolve-Path -LiteralPath $InstalledExecutable).Path
$resolvedEvidence = [System.IO.Path]::GetFullPath($EvidencePath)
$expectedThumbprint = $ExpectedSignerThumbprint.ToUpperInvariant()

if ([System.IO.Path]::GetExtension($resolvedInstaller) -ne '.exe' -or
    [System.IO.Path]::GetExtension($resolvedExecutable) -ne '.exe') {
    throw 'InstallerPath and InstalledExecutable must both resolve to Windows executables.'
}

$versionInfo = (Get-Item -LiteralPath $resolvedExecutable).VersionInfo
if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion) -or
    -not $versionInfo.ProductVersion.StartsWith($ExpectedVersion)) {
    throw "Installed executable version '$($versionInfo.ProductVersion)' does not match '$ExpectedVersion'."
}

function Get-VerifiedSignature([string]$Path, [string]$ExpectedThumbprint) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode signature is not valid for '$Path': $($signature.Status) $($signature.StatusMessage)"
    }
    if ($null -eq $signature.SignerCertificate) {
        throw "Authenticode signature has no signer certificate for '$Path'."
    }
    $observedThumbprint = $signature.SignerCertificate.Thumbprint.ToUpperInvariant()
    if ($observedThumbprint -ne $ExpectedThumbprint) {
        throw "Signer thumbprint '$observedThumbprint' does not match '$ExpectedThumbprint' for '$Path'."
    }
    return [ordered]@{
        status = $signature.Status.ToString()
        signerSubject = $signature.SignerCertificate.Subject
        signerThumbprint = $observedThumbprint
        certificateNotBeforeUtc = $signature.SignerCertificate.NotBefore.ToUniversalTime().ToString('O')
        certificateNotAfterUtc = $signature.SignerCertificate.NotAfter.ToUniversalTime().ToString('O')
        timestampSubject = if ($null -eq $signature.TimeStamperCertificate) { $null } else { $signature.TimeStamperCertificate.Subject }
    }
}

$installerSignature = Get-VerifiedSignature -Path $resolvedInstaller -ExpectedThumbprint $expectedThumbprint
$executableSignature = Get-VerifiedSignature -Path $resolvedExecutable -ExpectedThumbprint $expectedThumbprint
$evidence = [ordered]@{
    schemaVersion = 1
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    expectedVersion = $ExpectedVersion
    expectedSignerThumbprint = $expectedThumbprint
    installer = [ordered]@{
        path = $resolvedInstaller
        bytes = (Get-Item -LiteralPath $resolvedInstaller).Length
        sha256 = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash
        signature = $installerSignature
    }
    installedExecutable = [ordered]@{
        path = $resolvedExecutable
        bytes = (Get-Item -LiteralPath $resolvedExecutable).Length
        productVersion = $versionInfo.ProductVersion
        sha256 = (Get-FileHash -LiteralPath $resolvedExecutable -Algorithm SHA256).Hash
        signature = $executableSignature
    }
    operatingSystem = [System.Environment]::OSVersion.VersionString
    powershellVersion = $PSVersionTable.PSVersion.ToString()
}

$parent = Split-Path -Parent $resolvedEvidence
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}
$evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedEvidence -Encoding utf8
Write-Output "Release signature audit passed. Evidence: $resolvedEvidence"
