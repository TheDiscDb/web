#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string[]] $RuntimeIdentifier,

    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$config = Get-Content (Join-Path $PSScriptRoot 'scan-cli-release.json') -Raw | ConvertFrom-Json
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'code\TheDiscDb.DiscScan.Cli\TheDiscDb.DiscScan.Cli.csproj'
if (!$RuntimeIdentifier) { $RuntimeIdentifier = $config.runtimeIdentifiers }
foreach ($rid in $RuntimeIdentifier) {
    if ($rid -notin $config.runtimeIdentifiers) { throw "Unsupported runtime: $rid" }
}
if (@($RuntimeIdentifier | Select-Object -Unique).Count -ne $RuntimeIdentifier.Count) {
    throw 'Runtime identifiers must be unique.'
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot "artifacts\scan-cli\$Version" }
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path $OutputDirectory) { throw "Output directory already exists: $OutputDirectory. Choose a new directory." }
Get-Command dotnet -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$assets = @()

foreach ($rid in $RuntimeIdentifier) {
    $publish = Join-Path $OutputDirectory "publish\$rid"
    & dotnet publish $project -c $config.configuration -r $rid --self-contained true `
        '-p:PublishSingleFile=true' '-p:PublishTrimmed=false' '-p:NuGetAudit=false' `
        "-p:Version=$Version" '-p:IncludeSourceRevisionInInformationalVersion=false' `
        '-p:DebugType=None' '-p:DebugSymbols=false' -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid (exit $LASTEXITCODE)." }

    $windowsTarget = $rid.StartsWith('win-')
    $binaryName = $config.artifactName + $(if ($windowsTarget) { '.exe' } else { '' })
    $binary = Join-Path $publish $binaryName
    if (!(Test-Path $binary -PathType Leaf)) { throw "Missing published executable: $binary" }
    $stageName = "$($config.artifactName)-$Version-$rid"
    $stage = Join-Path $OutputDirectory "stage\$stageName"
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item (Join-Path $publish '*') $stage -Recurse
    Copy-Item (Join-Path $repoRoot 'code\TheDiscDb.DiscScan.Cli\README.md') $stage
    Copy-Item (Join-Path $repoRoot 'LICENSE') $stage

    if ($windowsTarget) {
        $archive = Join-Path $OutputDirectory "$stageName.zip"
        Compress-Archive -Path $stage -DestinationPath $archive
    } else {
        $archive = Join-Path $OutputDirectory "$stageName.tar.gz"
        $fileStream = [IO.File]::Create($archive)
        $gzip = [IO.Compression.GZipStream]::new($fileStream, [IO.Compression.CompressionLevel]::Optimal)
        $tar = [System.Formats.Tar.TarWriter]::new($gzip, $true)
        try {
            foreach ($file in Get-ChildItem $stage -File -Recurse | Sort-Object FullName) {
                $relative = [IO.Path]::GetRelativePath($stage, $file.FullName).Replace('\', '/')
                $entry = [System.Formats.Tar.PaxTarEntry]::new(
                    [System.Formats.Tar.TarEntryType]::RegularFile, "$stageName/$relative")
                $entry.Mode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite `
                    -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead
                if ($relative -eq $binaryName) {
                    $entry.Mode = $entry.Mode -bor [IO.UnixFileMode]::UserExecute `
                        -bor [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherExecute
                }
                $inputStream = [IO.File]::OpenRead($file.FullName)
                try {
                    $entry.DataStream = $inputStream
                    $tar.WriteEntry($entry)
                } finally { $inputStream.Dispose() }
            }
        } finally {
            $tar.Dispose()
            $gzip.Dispose()
            $fileStream.Dispose()
        }
    }
    $assets += [ordered]@{
        runtimeIdentifier = $rid
        file = [IO.Path]::GetFileName($archive)
        sha256 = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$assets | ForEach-Object { "$($_.sha256)  $($_.file)" } |
    Set-Content (Join-Path $OutputDirectory 'sha256.txt') -Encoding utf8
[ordered]@{
    version = $Version
    repository = $config.repository
    tag = "$($config.tagPrefix)$Version"
    assets = @($assets)
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'release.json') -Encoding utf8
Write-Host "Release packages: $OutputDirectory"
