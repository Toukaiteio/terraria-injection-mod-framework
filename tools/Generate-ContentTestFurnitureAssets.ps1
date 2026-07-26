$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$out = Join-Path (Split-Path -Parent $PSScriptRoot) "examples\ContentTestKit\Content"

function New-Bitmap([int]$width, [int]$height) {
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    return $bitmap
}

function Save-Png($bitmap, [string]$name) {
    $path = Join-Path $out $name
    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "$name -> $path"
}

function Fill-Rect($bitmap, [int]$x, [int]$y, [int]$w, [int]$h, [Drawing.Color]$color) {
    for ($px = $x; $px -lt $x + $w; $px++) {
        for ($py = $y; $py -lt $y + $h; $py++) {
            if ($px -ge 0 -and $py -ge 0 -and $px -lt $bitmap.Width -and $py -lt $bitmap.Height) {
                $bitmap.SetPixel($px, $py, $color)
            }
        }
    }
}

$dark = [Drawing.Color]::FromArgb(255, 42, 27, 66)
$edge = [Drawing.Color]::FromArgb(255, 92, 48, 126)
$wood = [Drawing.Color]::FromArgb(255, 157, 84, 170)
$light = [Drawing.Color]::FromArgb(255, 239, 150, 220)
$cyan = [Drawing.Color]::FromArgb(255, 79, 226, 224)
$gold = [Drawing.Color]::FromArgb(255, 250, 192, 72)

# Workbench tile sheet: 2 cells, 16x16 source pixels, 18px frame stride.
$workbench = New-Bitmap 36 18
Fill-Rect $workbench 0 4 34 3 $dark
Fill-Rect $workbench 1 5 32 5 $wood
Fill-Rect $workbench 1 5 32 1 $light
Fill-Rect $workbench 3 10 4 6 $edge
Fill-Rect $workbench 27 10 4 6 $edge
Fill-Rect $workbench 8 8 18 2 $cyan
Save-Png $workbench "TestWorkbenchTile.png"

$workbenchItem = New-Bitmap 28 16
Fill-Rect $workbenchItem 0 2 28 3 $dark
Fill-Rect $workbenchItem 1 3 26 6 $wood
Fill-Rect $workbenchItem 2 3 24 1 $light
Fill-Rect $workbenchItem 3 9 4 7 $edge
Fill-Rect $workbenchItem 21 9 4 7 $edge
Fill-Rect $workbenchItem 8 7 12 2 $cyan
Save-Png $workbenchItem "TestWorkbenchItem.png"

# Chest tile sheet: BasicChest rendering selects states at Y +0, +38 and +76.
# Each state contains four 16x16 cells with an 18px frame stride.
$chest = New-Bitmap 36 112
foreach ($stateY in @(0, 38, 76)) {
    foreach ($originX in @(0, 18)) {
        foreach ($originY in @(0, 18)) {
            Fill-Rect $chest $originX ($stateY + $originY + 1) 16 15 $dark
            Fill-Rect $chest ($originX + 1) ($stateY + $originY + 2) 14 13 $wood
        }
    }
    Fill-Rect $chest 1 ($stateY + 3) 32 3 $light
    Fill-Rect $chest 1 ($stateY + 13) 32 3 $edge
    Fill-Rect $chest 1 ($stateY + 19) 32 3 $edge
    Fill-Rect $chest 1 ($stateY + 31) 32 3 $dark
    Fill-Rect $chest 14 ($stateY + 14) 6 8 $gold
    Fill-Rect $chest 16 ($stateY + 16) 2 4 $cyan
}
# Make the two open animation states visibly distinct while retaining valid source cells.
Fill-Rect $chest 3 40 28 3 $cyan
Fill-Rect $chest 5 78 24 3 $gold
Save-Png $chest "TestChestTile.png"

$chestItem = New-Bitmap 30 24
Fill-Rect $chestItem 0 1 30 23 $dark
Fill-Rect $chestItem 2 3 26 18 $wood
Fill-Rect $chestItem 2 3 26 3 $light
Fill-Rect $chestItem 2 17 26 4 $edge
Fill-Rect $chestItem 12 10 6 8 $gold
Fill-Rect $chestItem 14 12 2 4 $cyan
Save-Png $chestItem "TestChestItem.png"
