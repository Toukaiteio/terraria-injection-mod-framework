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
$content = Join-Path $Root "src\TIMF.Content\bin\$Configuration\net48\TIMF.Content.dll"
$launcher = Join-Path $Root "src\TIMF.Launcher\bin\$Configuration\net48\TIMF.Launcher.exe"
$timfUi = Join-Path $Root "libs\TIMF.UI\bin\$Configuration\net48\TIMF.UI.dll"

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "Mods") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "config") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Dist "logs") | Out-Null

Copy-Item $abs $Dist -Force
Copy-Item $core $Dist -Force
Copy-Item $content $Dist -Force
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
  $uiOut = Join-Path $uiDir "TIMF.UI.dll"
  Copy-Item $timfUi $uiOut -Force
  $uiHash = (Get-FileHash $uiOut -Algorithm SHA256).Hash
  Set-Content -LiteralPath (Join-Path $Dist "trusted-framework-components.v1") `
    -Value ($uiHash + "`tMods/TIMF.UI/TIMF.UI.dll") -Encoding ASCII
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
# CI / override first (winlibs install step exports TIMF_MINGW_GPP).
if ($env:TIMF_MINGW_GPP -and (Test-I686Gpp $env:TIMF_MINGW_GPP)) {
  $MingwGpp = $env:TIMF_MINGW_GPP
}
$candidates = @(
  "D:\i686-8.1.0-release-posix-dwarf-rt_v6-rev0\mingw32\bin\g++.exe",
  "C:\tools\msys64\mingw32\bin\g++.exe",
  "C:\msys64\mingw32\bin\g++.exe",
  "D:\msys64\mingw32\bin\g++.exe"
)
if (-not $MingwGpp) {
  foreach ($c in $candidates) {
    if (Test-I686Gpp $c) { $MingwGpp = $c; break }
  }
}
if (-not $MingwGpp) {
  # Prefer g++.exe (Windows) over bare g++ (Git Bash may shadow with non-i686).
  foreach ($name in @("g++.exe", "g++")) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd -and (Test-I686Gpp $cmd.Source)) { $MingwGpp = $cmd.Source; break }
  }
}
if (-not $MingwGpp) {
  $msg = "32-bit (i686) g++ not found. Install MinGW-w64 i686 or set TIMF_MINGW_GPP."
  # GitHub Actions / explicit require must never produce a dist without Bootstrap.
  if ($env:GITHUB_ACTIONS -eq "true" -or $env:TIMF_REQUIRE_BOOTSTRAP -eq "1") {
    throw $msg
  }
  Write-Warning "$msg Skipping Bootstrap build."
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
