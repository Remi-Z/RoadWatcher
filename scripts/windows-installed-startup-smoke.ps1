[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstalledExecutable,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion,

    [ValidateRange(3, 60)]
    [int]$StartupSeconds = 8,

    [string]$EvidencePath = (Join-Path (Get-Location) 'roadwatcher-installed-smoke.json')
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $InstalledExecutable).Path
$resolvedEvidence = [System.IO.Path]::GetFullPath($EvidencePath)

if ([System.IO.Path]::GetExtension($resolvedExecutable) -ne '.exe') {
    throw 'InstalledExecutable must resolve to a Windows executable.'
}

$versionInfo = (Get-Item -LiteralPath $resolvedExecutable).VersionInfo
$observedVersion = $versionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($observedVersion) -or -not $observedVersion.StartsWith($ExpectedVersion)) {
    throw "Installed executable version '$observedVersion' does not match '$ExpectedVersion'."
}

$process = $null
try {
    $process = Start-Process -FilePath $resolvedExecutable -PassThru
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) {
            throw "RoadWatcher exited during startup with code $($process.ExitCode)."
        }
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        executable = $resolvedExecutable
        expectedVersion = $ExpectedVersion
        productVersion = $observedVersion
        sha256 = (Get-FileHash -LiteralPath $resolvedExecutable -Algorithm SHA256).Hash
        startupSeconds = $StartupSeconds
        processStayedRunning = $true
        operatingSystem = [System.Environment]::OSVersion.VersionString
        powershellVersion = $PSVersionTable.PSVersion.ToString()
    }
    $parent = Split-Path -Parent $resolvedEvidence
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $evidence | ConvertTo-Json | Set-Content -LiteralPath $resolvedEvidence -Encoding utf8
    Write-Output "Installed startup smoke passed. Evidence: $resolvedEvidence"
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
            $process.WaitForExit()
        }
    }
}
