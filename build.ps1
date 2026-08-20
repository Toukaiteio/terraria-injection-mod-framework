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
$timfPinyin = Join-Path $Root "libs\TIMF.Pinyin\bin\$Configuration\net48\TIMF.Pinyin.dll"

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

# TIMF.Pinyin is a framework-shipped library mod -> deploy into Mods\TIMF.Pinyin\.
# Unlike TIMF.UI it is NOT a trusted component (pure managed string logic), so it runs in the
# normal mod sandbox and is NOT added to trusted-framework-components.v1. It carries its NuGet
# dependency (NPinyin.dll) alongside so the pinyin dataset ships once for the whole install.
if (Test-Path $timfPinyin) {
  $pyDir = Join-Path $Dist "Mods\TIMF.Pinyin"
  New-Item -ItemType Directory -Force -Path $pyDir | Out-Null
  Copy-Item $timfPinyin (Join-Path $pyDir "TIMF.Pinyin.dll") -Force
  # Bundle non-framework dependency dlls (NPinyin) from the build output.
  $pyBin = Split-Path $timfPinyin
  $pyFrameworkPrefixes = @("TIMF.", "Terraria", "Microsoft.Xna", "0Harmony", "ReLogic", "System.", "mscorlib")
  Get-ChildItem $pyBin -Filter "*.dll" -File -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Name -eq "TIMF.Pinyin.dll") { return }
    $isFramework = $false
    foreach ($p in $pyFrameworkPrefixes) {
      if ($_.Name.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)) { $isFramework = $true; break }
    }
    if (-not $isFramework) {
      Copy-Item $_.FullName (Join-Path $pyDir $_.Name) -Force
      Write-Host "  TIMF.Pinyin dep: $($_.Name)"
    }
  }
} else {
  Write-Warning "Missing TIMF.Pinyin artifact: $timfPinyin"
}

# --- Assemble the standalone Mod SDK (for developing mods OUTSIDE this repo) ---
# Ships the redistributable compile references + shared build props + the dotnet new template,
# so a mod author only needs this folder (TIMF_SDK) plus their own Terraria.exe.
$SdkOut = Join-Path $Dist "ModSDK"
$SdkProps = Join-Path $Root "sdk\TIMF.Mod.props"
$TemplateSrc = Join-Path $Root "templates\timf-mod"
$XnaSrc = Join-Path $Root "lib\xna"
if (Test-Path $SdkProps) {
  New-Item -ItemType Directory -Force -Path $SdkOut, (Join-Path $SdkOut "xna"), (Join-Path $SdkOut "templates") | Out-Null
  Copy-Item $SdkProps $SdkOut -Force
  Copy-Item $abs $SdkOut -Force
  $harmony = Join-Path $Dist "0Harmony.dll"
  if (Test-Path $harmony) { Copy-Item $harmony $SdkOut -Force }
  else { Write-Warning "0Harmony.dll not in dist; mods referencing AccessTools will not compile against the SDK" }
  if (Test-Path $XnaSrc) { Copy-Item (Join-Path $XnaSrc "*.dll") (Join-Path $SdkOut "xna") -Force }
  else { Write-Warning "lib\xna missing; SDK will lack XNA reference assemblies" }
  if (Test-Path $TemplateSrc) {
    Copy-Item $TemplateSrc (Join-Path $SdkOut "templates") -Recurse -Force
  }
  $sdkReadme = @(
    "# TIMF Mod SDK",
    "",
    "Build Terraria mods for TIMF outside the framework repo.",
    "",
    "## One-time setup",
    "  setx TIMF_SDK `"<this folder>`"          # reopen the shell afterwards",
    "  dotnet new install .\templates\timf-mod",
    "",
    "You also need a Terraria.exe compile reference (a legal copy you own):",
    "  setx TIMF_TERRARIA `"<path-to-your-Terraria.exe>`"",
    "",
    "## Create and build a mod",
    "  dotnet new timf-mod -n MyMod --display `"My Mod`" --modAuthor `"you`"",
    "  cd MyMod",
    "  dotnet build -c Release",
    "",
    "The build produces a drop-in folder dist\MyMod\ — copy it into your TIMF home's Mods\.",
    "",
    "Terraria.exe is NEVER included in this SDK; supply your own."
  )
  Set-Content -LiteralPath (Join-Path $SdkOut "README.md") -Value $sdkReadme -Encoding utf8
  Write-Host "Mod SDK assembled -> $SdkOut"
} else {
  Write-Warning "Missing sdk\TIMF.Mod.props; skipping Mod SDK assembly."
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
$candidates = @()
foreach ($mingwRoot in @($env:TIMF_MINGW_ROOT, $env:MSYS2_ROOT)) {
  if ([string]::IsNullOrWhiteSpace($mingwRoot)) { continue }
  $candidates += Join-Path $mingwRoot "bin\g++.exe"
  $candidates += Join-Path $mingwRoot "mingw32\bin\g++.exe"
}
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
  $msg = "32-bit (i686) g++ not found. Install MinGW-w64 i686, set TIMF_MINGW_GPP, or set TIMF_MINGW_ROOT."
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
