[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\$Runtime"))
$archivePath = Join-Path $artifactsRoot "RoadWatcher-$Runtime.zip"
$projectPath = Join-Path $repositoryRoot 'src\RoadWatcher.App\RoadWatcher.App.csproj'

if (-not $publishDirectory.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish directory outside '$artifactsRoot'."
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# VideoLAN.LibVLC.Windows carries native runtimes for every Windows CPU. Keep only
# the selected publish runtime so the portable archive does not triple in size.
$libVlcRoot = Join-Path $publishDirectory 'libvlc'
if (Test-Path -LiteralPath $libVlcRoot) {
    Get-ChildItem -LiteralPath $libVlcRoot -Directory |
        Where-Object { $_.Name -ne $Runtime } |
        ForEach-Object {
            $runtimeDirectory = [System.IO.Path]::GetFullPath($_.FullName)
            if (-not $runtimeDirectory.StartsWith($publishDirectory + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove native runtime outside '$publishDirectory'."
            }
            Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force
        }
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host "Portable application: $publishDirectory"
Write-Host "Portable archive:     $archivePath"

if ($SkipInstaller) {
    return
}

$innoCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if ($null -eq $innoCompiler) {
    Write-Warning 'Portable build succeeded. Install Inno Setup 6 and rerun without -SkipInstaller to create RoadWatcherSetup.exe.'
    return
}

$installerScript = Join-Path $repositoryRoot 'installer\RoadWatcher.iss'
& $innoCompiler $installerScript "/DAppPublishDir=$publishDirectory" "/DOutputDir=$artifactsRoot"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Host "Installer:            $(Join-Path $artifactsRoot 'RoadWatcherSetup.exe')"
