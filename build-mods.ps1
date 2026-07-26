# Build every mod under .\mods\ and deploy into dist\Mods\<ModId>\.
# Each mod folder must contain exactly one <Name>.csproj. Any *.png next to it is copied
# alongside the dll; any *.default.json is installed into dist\config\<Name>.json (once).
#
# Mods are gitignored and NOT part of TIMF.sln. Run build.ps1 first (mods reference the
# framework's built TIMF.Abstractions.dll).
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Dist = Join-Path $Root "dist"
$ModsSrc = Join-Path $Root "mods"
$Configuration = if ($args.Count -gt 0) { $args[0] } else { "Release" }

if (-not (Test-Path $ModsSrc)) {
  Write-Warning "No mods\ directory found."
  return
}

$absDll = Join-Path $Root "src\TIMF.Abstractions\bin\$Configuration\net48\TIMF.Abstractions.dll"
if (-not (Test-Path $absDll)) {
  throw "Framework not built. Run .\build.ps1 first (missing $absDll)."
}

New-Item -ItemType Directory -Force -Path (Join-Path $Dist "Mods") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "config") | Out-Null

# Discover mod folders (each has one .csproj).
$modDirs = Get-ChildItem $ModsSrc -Directory | Sort-Object Name
if ($modDirs.Count -eq 0) {
  Write-Warning "mods\ is empty."
  return
}

foreach ($dir in $modDirs) {
  $proj = Get-ChildItem $dir.FullName -Filter *.csproj | Select-Object -First 1
  if (-not $proj) {
    Write-Warning "Skipping $($dir.Name): no .csproj"
    continue
  }

  $id = [IO.Path]::GetFileNameWithoutExtension($proj.Name)
  Write-Host "==> Building mod: $id"
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

  # Dependency DLLs (NuGet packages like NPinyin) — copy non-framework dlls from build output.
  $binDir = Split-Path $dll
  $frameworkPrefixes = @("TIMF.", "Terraria", "Microsoft.Xna", "0Harmony", "ReLogic", "System.", "mscorlib")
  Get-ChildItem $binDir -Filter "*.dll" -File -ErrorAction SilentlyContinue | ForEach-Object {
    $n = $_.Name
    if ($n -eq "$id.dll") { return }
    $isFramework = $false
    foreach ($p in $frameworkPrefixes) {
      if ($n.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)) { $isFramework = $true; break }
    }
    if (-not $isFramework) {
      Copy-Item $_.FullName (Join-Path $outDir $n) -Force
      Write-Host "    dep: $n"
    }
  }

  # Assets: any png in the mod source folder.
  Get-ChildItem $dir.FullName -Filter *.png -File -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $outDir $_.Name) -Force
  }
  $contentSrc = Join-Path $dir.FullName "Content"
  if (Test-Path $contentSrc) {
    $contentDst = Join-Path $outDir "Content"
    New-Item -ItemType Directory -Force -Path $contentDst | Out-Null
    Copy-Item (Join-Path $contentSrc "*") $contentDst -Recurse -Force
  }

  # Localization catalogs: Localization/*.json
  $locSrc = Join-Path $dir.FullName "Localization"
  if (Test-Path $locSrc) {
    $locDst = Join-Path $outDir "Localization"
    New-Item -ItemType Directory -Force -Path $locDst | Out-Null
    Get-ChildItem $locSrc -Filter *.json -File -ErrorAction SilentlyContinue | ForEach-Object {
      Copy-Item $_.FullName (Join-Path $locDst $_.Name) -Force
    }
  }

  # Default config: <anything>.default.json -> dist\config\<Name>.json (only if absent).
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
Write-Host "Mods deployed to: $Dist\Mods"
