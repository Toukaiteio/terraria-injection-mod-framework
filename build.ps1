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
$boss = Join-Path $Root "examples\BossCursor\bin\$Configuration\net48\BossCursor.dll"
$cursorSrc = Join-Path $Root "examples\BossCursor\Cursor.png"

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
Copy-Item $boss (Join-Path $Dist "Mods\BossCursor.dll") -Force
if (Test-Path $cursorSrc) {
  Copy-Item $cursorSrc (Join-Path $Dist "Mods\Cursor.png") -Force
}

$defaultCfg = Join-Path $Root "examples\BossCursor\BossCursor.default.json"
$targetCfg = Join-Path $Dist "config\BossCursor.json"
if ((Test-Path $defaultCfg) -and -not (Test-Path $targetCfg)) {
  Copy-Item $defaultCfg $targetCfg
}

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
