# Build TIMF managed projects + native bootstrap, publish to ./dist
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Dist = Join-Path $Root "dist"
$Configuration = if ($args.Count -gt 0) { $args[0] } else { "Release" }

Write-Host "==> Building managed projects ($Configuration)"
dotnet build (Join-Path $Root "TIMF.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$abs = Join-Path $Root "src\TIMF.Abstractions\bin\$Configuration\net48\TIMF.Abstractions.dll"
$core = Join-Path $Root "src\TIMF.Core\bin\$Configuration\net48\TIMF.Core.dll"
$launcher = Join-Path $Root "src\TIMF.Launcher\bin\$Configuration\net48\TIMF.Launcher.exe"

# Each mod is published into its own folder: dist\Mods\<ModId>\ (dll + assets).
# Assets: extra files copied alongside the dll inside that folder.
$modArtifacts = @(
  @{ Id = "BossCursor";       Dll = "examples\BossCursor\bin\$Configuration\net48\BossCursor.dll";
     Assets = @("examples\BossCursor\Cursor.png") },
  @{ Id = "HighLight";        Dll = "examples\HighLight\bin\$Configuration\net48\HighLight.dll"; Assets = @() },
  @{ Id = "LowHealthWarning"; Dll = "examples\LowHealthWarning\bin\$Configuration\net48\LowHealthWarning.dll"; Assets = @() },
  @{ Id = "TIMF.UI";          Dll = "libs\TIMF.UI\bin\$Configuration\net48\TIMF.UI.dll"; Assets = @() },
  @{ Id = "ModSettingsHub";   Dll = "examples\ModSettingsHub\bin\$Configuration\net48\ModSettingsHub.dll"; Assets = @() }
)

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "Mods") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "config") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "logs") | Out-Null

Copy-Item $abs $Dist -Force
Copy-Item $core $Dist -Force
Copy-Item $launcher $Dist -Force
# Harmony and any other managed deps next to Core
$coreDir = Split-Path $core
Get-ChildItem $coreDir -Filter "*.dll" | ForEach-Object {
  if ($_.Name -notin @("TIMF.Core.dll", "TIMF.Abstractions.dll", "Terraria.exe")) {
    Copy-Item $_.FullName $Dist -Force
  }
}

foreach ($m in $modArtifacts) {
  $src = Join-Path $Root $m.Dll
  if (-not (Test-Path $src)) {
    Write-Warning "Missing mod artifact: $src"
    continue
  }

  $modDir = Join-Path $Dist ("Mods\" + $m.Id)
  New-Item -ItemType Directory -Force -Path $modDir | Out-Null
  Copy-Item $src (Join-Path $modDir ([IO.Path]::GetFileName($src))) -Force

  foreach ($asset in $m.Assets) {
    $assetSrc = Join-Path $Root $asset
    if (Test-Path $assetSrc) {
      Copy-Item $assetSrc (Join-Path $modDir ([IO.Path]::GetFileName($assetSrc))) -Force
    } else {
      Write-Warning "Missing asset for $($m.Id): $assetSrc"
    }
  }
}

function Install-DefaultConfig([string]$src, [string]$dst) {
  if ((Test-Path $src) -and -not (Test-Path $dst)) {
    Copy-Item $src $dst
  }
}

Install-DefaultConfig `
  (Join-Path $Root "examples\BossCursor\BossCursor.default.json") `
  (Join-Path $Dist "config\BossCursor.json")
Install-DefaultConfig `
  (Join-Path $Root "examples\HighLight\HighLight.default.json") `
  (Join-Path $Dist "config\HighLight.json")
Install-DefaultConfig `
  (Join-Path $Root "examples\LowHealthWarning\LowHealthWarning.default.json") `
  (Join-Path $Dist "config\LowHealthWarning.json")

Write-Host "==> Building native Bootstrap (MinGW i686)"
$MingwGpp = "D:\i686-8.1.0-release-posix-dwarf-rt_v6-rev0\mingw32\bin\g++.exe"
if (-not (Test-Path $MingwGpp)) {
  $cmd = Get-Command g++ -ErrorAction SilentlyContinue
  if ($cmd) { $MingwGpp = $cmd.Source }
}
if (-not $MingwGpp -or -not (Test-Path $MingwGpp)) {
  Write-Warning "32-bit g++ not found. Skipping Bootstrap build. Install MinGW-w64 i686 and re-run."
} else {
  $bootSrc = Join-Path $Root "src\TIMF.Bootstrap\bootstrap.cpp"
  $bootOut = Join-Path $Dist "TIMF.Bootstrap.dll"
  $argsGpp = @(
    "-shared", "-m32", "-O2",
    "-static-libgcc", "-static-libstdc++",
    $bootSrc,
    "-o", $bootOut,
    "-lole32", "-loleaut32", "-luuid",
    "-Wl,--kill-at"
  )
  Write-Host "g++ $($argsGpp -join ' ')"
  & $MingwGpp @argsGpp
  if ($LASTEXITCODE -ne 0) {
    throw "Bootstrap g++ build failed"
  }
  Write-Host "Bootstrap -> $bootOut"
}

Write-Host ""
Write-Host "Deploy folder: $Dist"
Write-Host "Run: $Dist\TIMF.Launcher.exe"
