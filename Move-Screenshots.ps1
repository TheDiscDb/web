<#
.SYNOPSIS
    Moves iPhone screenshots from a source folder to a 'screenshots' subfolder.

.DESCRIPTION
    Identifies iPhone screenshots by checking PNG files against all known iPhone
    screen resolutions (portrait, landscape, and scrolling screenshots).
    
    HEIC/JPG files are skipped because iPhone always saves screenshots as PNG.
    
    Detection is based on exact pixel dimension matching against every iPhone model
    from iPhone 4 through iPhone 16 Pro Max.

.PARAMETER SourcePath
    Path to the folder containing photos.

.PARAMETER WhatIf
    Preview mode — lists what would be moved without moving anything. This is the default.

.PARAMETER Force
    Actually move files. Without this flag, the script only previews.

.EXAMPLE
    # Preview what would be moved (default)
    .\Move-Screenshots.ps1

.EXAMPLE
    # Actually move files
    .\Move-Screenshots.ps1 -Force
#>

[CmdletBinding()]
param(
    [string]$SourcePath = "\\192.168.1.226\Media\Photos\Sync\Sabrina's iPhone\Recents",
    [switch]$Force
)

Add-Type -AssemblyName System.Drawing

# ── Known iPhone screenshot resolutions (width x height) ──────────────────────
# Every iPhone model, portrait and landscape, from iPhone 4 to iPhone 16 Pro Max.
$iPhoneScreenDimensions = @{
    # iPhone 4 / 4S
    '640x960'    = $true; '960x640'    = $true
    # iPhone 5 / 5C / 5S / SE (1st gen)
    '640x1136'   = $true; '1136x640'   = $true
    # iPhone 6 / 6S / 7 / 8 / SE (2nd & 3rd gen)
    '750x1334'   = $true; '1334x750'   = $true
    # iPhone 6 Plus / 6S Plus / 7 Plus / 8 Plus
    '1242x2208'  = $true; '2208x1242'  = $true
    # iPhone X / XS / 11 Pro
    '1125x2436'  = $true; '2436x1125'  = $true
    # iPhone XR / 11
    '828x1792'   = $true; '1792x828'   = $true
    # iPhone XS Max / 11 Pro Max
    '1242x2688'  = $true; '2688x1242'  = $true
    # iPhone 12 mini / 13 mini
    '1080x2340'  = $true; '2340x1080'  = $true
    # iPhone 12 / 12 Pro / 13 / 13 Pro / 14 / 15
    '1170x2532'  = $true; '2532x1170'  = $true
    # iPhone 12 Pro Max / 13 Pro Max / 14 Plus / 15 Plus
    '1284x2778'  = $true; '2778x1284'  = $true
    # iPhone 14 Pro / 15 Pro / 16
    '1179x2556'  = $true; '2556x1179'  = $true
    # iPhone 14 Pro Max / 15 Pro Max / 16 Plus
    '1290x2796'  = $true; '2796x1290'  = $true
    # iPhone 16 Pro
    '1206x2622'  = $true; '2622x1206'  = $true
    # iPhone 16 Pro Max
    '1320x2868'  = $true; '2868x1320'  = $true
}

# Known iPhone screen widths and their max portrait heights — for scrolling screenshot detection.
# A scrolling screenshot has a known iPhone width but a height taller than the standard screen.
$iPhoneWidths = @{
    640  = 1136
    750  = 1334
    828  = 1792
    1080 = 2340
    1125 = 2436
    1170 = 2532
    1179 = 2556
    1206 = 2622
    1242 = 2688
    1284 = 2778
    1290 = 2796
    1320 = 2868
}

function Test-IsScreenshot {
    param([int]$Width, [int]$Height)

    $key = "${Width}x${Height}"

    # Exact match to a known iPhone screen resolution (portrait or landscape)
    if ($iPhoneScreenDimensions.ContainsKey($key)) {
        return @{ IsScreenshot = $true; Reason = "Exact iPhone resolution match ($key)" }
    }

    # Scrolling screenshot: width matches a known iPhone width, height exceeds standard
    if ($iPhoneWidths.ContainsKey($Width) -and $Height -gt $iPhoneWidths[$Width]) {
        return @{ IsScreenshot = $true; Reason = "Scrolling screenshot (width $Width, height $Height > $($iPhoneWidths[$Width]))" }
    }

    return @{ IsScreenshot = $false; Reason = $null }
}

# ── Main ──────────────────────────────────────────────────────────────────────

if (-not (Test-Path $SourcePath)) {
    Write-Error "Source path not found: $SourcePath"
    exit 1
}

$destPath = Join-Path $SourcePath "screenshots"

$pngFiles = Get-ChildItem -Path $SourcePath -File -Filter "*.PNG"
$total     = $pngFiles.Count
$matched   = 0
$skipped   = 0
$errors    = 0
$processed = 0

Write-Host ""
Write-Host "Source:       $SourcePath"
Write-Host "Destination:  $destPath"
Write-Host "PNG files:    $total"
Write-Host "Mode:         $(if ($Force) { 'MOVE FILES' } else { 'PREVIEW (use -Force to move)' })"
Write-Host ""

if ($Force -and -not (Test-Path $destPath)) {
    New-Item -ItemType Directory -Path $destPath -Force | Out-Null
    Write-Host "Created destination folder: $destPath"
}

$screenshotFiles = [System.Collections.Generic.List[string]]::new()
$shellFolder = $null

foreach ($file in $pngFiles) {
    $processed++
    if ($processed % 500 -eq 0) {
        Write-Host "  Scanned $processed / $total ..."
    }

    try {
        $img    = [System.Drawing.Image]::FromFile($file.FullName)
        $width  = $img.Width
        $height = $img.Height
        $img.Dispose()
    }
    catch {
        # Fallback: use Windows Shell COM to read dimensions
        try {
            if (-not $shellFolder) {
                $shellObj    = New-Object -ComObject Shell.Application
                $shellFolder = $shellObj.Namespace($SourcePath)
            }
            $shellItem = $shellFolder.ParseName($file.Name)
            $width  = [int]($shellFolder.GetDetailsOf($shellItem, 176) -replace '[^\d]','')
            $height = [int]($shellFolder.GetDetailsOf($shellItem, 178) -replace '[^\d]','')
            if (-not $width -or -not $height) { throw "No dimensions" }
        }
        catch {
            $errors++
            Write-Warning "Could not read: $($file.Name)"
            continue
        }
    }

    $result = Test-IsScreenshot -Width $width -Height $height

    if ($result.IsScreenshot) {
        $matched++
        $screenshotFiles.Add($file.Name)

        if ($Force) {
            Move-Item -LiteralPath $file.FullName -Destination (Join-Path $destPath $file.Name) -Force
        }
    }
    else {
        $skipped++
    }
}

Write-Host ""
Write-Host "══════════════════════════════════════════"
Write-Host "  Results"
Write-Host "══════════════════════════════════════════"
Write-Host "  Screenshots found:  $matched"
Write-Host "  Non-screenshots:    $skipped"
Write-Host "  Read errors:        $errors"
Write-Host "  Total scanned:      $processed"
Write-Host ""

if (-not $Force) {
    Write-Host "Preview — files that WOULD be moved:"
    Write-Host ""
    foreach ($name in $screenshotFiles) {
        Write-Host "  $name"
    }
    Write-Host ""
    Write-Host "Run with -Force to actually move these files."
}
else {
    Write-Host "Moved $matched files to: $destPath"
}
