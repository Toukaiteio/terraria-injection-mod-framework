# Build the TIMF framework (managed projects + native bootstrap) and publish to ./dist.
# This does NOT build the mods — run build-mods.ps1 for those.
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Dist = Join-Path $Root "dist"
$Configuration = if ($args.Count -gt 0) { $args[0] } else { "Release" }

Write-Host "==> Building framework projects ($Configuration)"
dotnet build (Join-Path $Root "TIMF.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$abs = Join-Path $Root "src\TIMF.Abstractions\bin\$Configuration\net48\TIMF.Abstractions.dll"
$core = Join-Path $Root "src\TIMF.Core\bin\$Configuration\net48\TIMF.Core.dll"
$launcher = Join-Path $Root "src\TIMF.Launcher\bin\$Configuration\net48\TIMF.Launcher.exe"
$timfUi = Join-Path $Root "libs\TIMF.UI\bin\$Configuration\net48\TIMF.UI.dll"

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

# TIMF.UI is a framework-shipped library mod -> deploy into Mods\TIMF.UI\
if (Test-Path $timfUi) {
  $uiDir = Join-Path $Dist "Mods\TIMF.UI"
  New-Item -ItemType Directory -Force -Path $uiDir | Out-Null
  Copy-Item $timfUi (Join-Path $uiDir "TIMF.UI.dll") -Force
} else {
  Write-Warning "Missing TIMF.UI artifact: $timfUi"
}

Write-Host "==> Building native Bootstrap (MinGW i686)"
# Prefer known i686 toolchains over a random PATH g++ (often mingw64, which cannot -m32 link).
function Test-I686Gpp([string]$path) {
  if (-not $path -or -not (Test-Path $path)) { return $false }
  try {
    $out = & $path -dumpmachine 2>$null
    if ($LASTEXITCODE -ne 0) { return $false }
    $m = ($out | Out-String).Trim()
    # Accept i686-*/mingw32 targets. Reject x86_64-*-mingw*.
    if ($m -match '^(i[3-6]86|mingw32)') { return $true }
    if ($m -match 'x86_64') { return $false }
    # Some toolchains report i686-w64-mingw32
    if ($m -match 'i686') { return $true }
    return $false
  } catch {
    return $false
  }
}

$MingwGpp = $null
$candidates = @(
  "D:\i686-8.1.0-release-posix-dwarf-rt_v6-rev0\mingw32\bin\g++.exe",
  "C:\tools\msys64\mingw32\bin\g++.exe",
  "C:\msys64\mingw32\bin\g++.exe",
  "D:\msys64\mingw32\bin\g++.exe"
)
foreach ($c in $candidates) {
  if (Test-I686Gpp $c) { $MingwGpp = $c; break }
}
if (-not $MingwGpp) {
  $cmd = Get-Command g++ -ErrorAction SilentlyContinue
  if ($cmd -and (Test-I686Gpp $cmd.Source)) { $MingwGpp = $cmd.Source }
}
if (-not $MingwGpp) {
  Write-Warning "32-bit (i686) g++ not found. Skipping Bootstrap build. Install MinGW-w64 i686 and re-run."
} else {
  Write-Host "Using g++: $MingwGpp ($(& $MingwGpp -dumpmachine))"
  $bootSrc = Join-Path $Root "src\TIMF.Bootstrap\bootstrap.cpp"
  $bootOut = Join-Path $Dist "TIMF.Bootstrap.dll"
  # Fully static CRT/pthread (-static). Without this, MinGW emits a dependency on
  # libwinpthread-1.dll; Terraria's LoadLibrary then fails (DLL not on game search path).
  # -static-libgcc alone is NOT enough for winpthread on this toolchain.
  $argsGpp = @(
    "-shared", "-m32", "-O2", "-static",
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
Write-Host "Framework deployed to: $Dist"
Write-Host "Next: .\build-mods.ps1   (builds every mod under .\mods\ into dist\Mods\)"
Write-Host "Run:  $Dist\TIMF.Launcher.exe"
