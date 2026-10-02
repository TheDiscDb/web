#Requires -Version 7.4
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [Parameter(Mandatory)]
    [string] $TargetCommitish,

    [string] $NotesFile,

    [switch] $Publish,

    [switch] $Prerelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$config = Get-Content (Join-Path $PSScriptRoot 'scan-cli-release.json') -Raw | ConvertFrom-Json
$ArtifactDirectory = (Resolve-Path $ArtifactDirectory).Path
$manifest = Get-Content (Join-Path $ArtifactDirectory 'release.json') -Raw | ConvertFrom-Json
if ($manifest.repository -ne $config.repository -or
    $manifest.tag -ne "$($config.tagPrefix)$($manifest.version)") {
    throw 'Release manifest does not match the configured repository and tag prefix.'
}
if ($manifest.assets.Count -ne $config.runtimeIdentifiers.Count -or
    @($manifest.assets.runtimeIdentifier | Select-Object -Unique).Count -ne $config.runtimeIdentifiers.Count) {
    throw 'Release requires exactly one package for every configured runtime. Build all six runtimes first.'
}
$files = @()
foreach ($asset in $manifest.assets) {
    $extension = if ($asset.runtimeIdentifier.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
    $expectedName = "$($config.artifactName)-$($manifest.version)-$($asset.runtimeIdentifier).$extension"
    if ($asset.runtimeIdentifier -notin $config.runtimeIdentifiers -or $asset.file -ne $expectedName) {
        throw "Unexpected release asset: $($asset.file)"
    }
    $path = Join-Path $ArtifactDirectory $asset.file
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.sha256) {
        throw "Checksum mismatch: $path"
    }
    $files += $path
}
$checksumFile = Join-Path $ArtifactDirectory 'sha256.txt'
$expectedChecksums = @($manifest.assets | ForEach-Object { "$($_.sha256)  $($_.file)" })
if (@(Compare-Object $expectedChecksums @(Get-Content $checksumFile)).Count -ne 0) {
    throw 'sha256.txt does not match the release manifest.'
}
$files += $checksumFile
$files += Join-Path $ArtifactDirectory 'release.json'
$arguments = @('release', 'create', $manifest.tag, '--repo', $config.repository,
    '--target', $TargetCommitish, '--title', "TheDiscDb Scan $($manifest.version)")
if (!$Publish) { $arguments += '--draft' }
if ($Prerelease -or $manifest.version.Contains('-')) { $arguments += '--prerelease' }
if ($NotesFile) {
    $arguments += @('--notes-file', (Resolve-Path $NotesFile).Path)
} else {
    $arguments += @('--notes', "Cross-platform TheDiscDb optical disc scanner $($manifest.version). Download the package for your OS and architecture. Packages are self-contained; no .NET installation is required. See the included README for usage. Verify downloads using sha256.txt.")
}
$arguments += $files
if ($PSCmdlet.ShouldProcess("$($config.repository)/$($manifest.tag)", 'Create release and upload verified packages')) {
    Get-Command gh -ErrorAction Stop | Out-Null
    & gh auth status
    if ($LASTEXITCODE -ne 0) { throw 'Authenticate with gh auth login before publishing.' }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub release creation/upload failed (exit $LASTEXITCODE). Inspect the release before retrying; existing assets are never overwritten."
    }
}
