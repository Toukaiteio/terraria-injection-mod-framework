# Build every example under .\examples\ and deploy into dist\Mods\<ModId>\.
# Mirrors build-mods.ps1 but uses the tracked examples/ tree (for CI / clean clones).
# Requires .\build.ps1 first so TIMF.Abstractions is available.
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Dist = Join-Path $Root "dist"
$ExamplesSrc = Join-Path $Root "examples"
$Configuration = if ($args.Count -gt 0) { $args[0] } else { "Release" }

if (-not (Test-Path $ExamplesSrc)) {
  Write-Warning "No examples\ directory found."
  return
}

$absDll = Join-Path $Root "src\TIMF.Abstractions\bin\$Configuration\net48\TIMF.Abstractions.dll"
if (-not (Test-Path $absDll)) {
  throw "Framework not built. Run .\build.ps1 first (missing $absDll)."
}

if (-not $env:TIMF_TERRARIA -and -not (Test-Path (Join-Path $Root "lib\Terraria.exe"))) {
  # Soft check — msbuild will fail with a clearer missing-ref error if truly absent.
  Write-Host "Note: TIMF_TERRARIA not set and lib\Terraria.exe missing; example builds need a Terraria.exe reference."
}

New-Item -ItemType Directory -Force -Path (Join-Path $Dist "Mods") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "config") | Out-Null

$modDirs = Get-ChildItem $ExamplesSrc -Directory | Sort-Object Name
if ($modDirs.Count -eq 0) {
  Write-Warning "examples\ is empty."
  return
}

foreach ($dir in $modDirs) {
  $proj = Get-ChildItem $dir.FullName -Filter *.csproj | Select-Object -First 1
  if (-not $proj) {
    Write-Warning "Skipping $($dir.Name): no .csproj"
    continue
  }

  $id = [IO.Path]::GetFileNameWithoutExtension($proj.Name)
  Write-Host "==> Building example: $id"
  dotnet build $proj.FullName -c $Configuration --nologo
  if ($LASTEXITCODE -ne 0) { throw "build failed: $id" }

  $dll = Join-Path $dir.FullName "bin\$Configuration\net48\$id.dll"
  if (-not (Test-Path $dll)) {
    Write-Warning "  build produced no dll: $dll"
    continue
  }

  $outDir = Join-Path $Dist ("Mods\" + $id)
  New-Item -ItemType Directory -Force -Path $outDir | Out-Null
  Copy-Item $dll (Join-Path $outDir "$id.dll") -Force

  Get-ChildItem $dir.FullName -Filter *.png -File -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $outDir $_.Name) -Force
  }

  $locSrc = Join-Path $dir.FullName "Localization"
  if (Test-Path $locSrc) {
    $locDst = Join-Path $outDir "Localization"
    New-Item -ItemType Directory -Force -Path $locDst | Out-Null
    Get-ChildItem $locSrc -Filter *.json -File -ErrorAction SilentlyContinue | ForEach-Object {
      Copy-Item $_.FullName (Join-Path $locDst $_.Name) -Force
    }
  }

  Get-ChildItem $dir.FullName -Filter *.default.json -File -ErrorAction SilentlyContinue | ForEach-Object {
    $cfgName = $_.Name -replace '\.default\.json$', '.json'
    $dst = Join-Path $Dist ("config\" + $cfgName)
    if (-not (Test-Path $dst)) {
      Copy-Item $_.FullName $dst -Force
    }
  }

  Write-Host "    -> $outDir\$id.dll"
}

Write-Host ""
Write-Host "Examples deployed to: $Dist\Mods"
