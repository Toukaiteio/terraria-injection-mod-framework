# Ports client-side TIMF mods into tModLoader's ModSources as tML mods that reference TimfBridge,
# then compile-verifies each offline against the already-built TimfBridge.dll (BuildMod=false, so no
# game launch). Real .tmod builds happen inside tModLoader (Develop Mods -> Build), which resolves
# modReferences and packs assets.
#
# With TimfBridge v2 (keybinds + player-update / info-accessory / map-overlay hooks + reflection
# broker + mod registry) all pure client mods port. Two need a small source fixup applied here:
#   * BlockLocator  — latest picker/database source plus vanilla Tile API (active()/type,
#                     Main.tile null-check) -> tML Tile struct; TIMF.Pinyin is optional in tML,
#                     so the picker keeps name/ID search when the shared library is unavailable.
#   * AutoTorch     — vanilla Tile API (active()/type/wall, Main.tile null-check) -> tML Tile struct.
#   * CreativeMode  — NPinyin package is unavailable on net8; pinyin helper degrades to name search.
# MyLifeIsValuable is intentionally excluded: its Fullbright/ChainMining/BestReforge use HarmonyLib
# directly and tModLoader ships MonoMod (no Harmony), so it needs a MonoMod rewrite (follow-up).

param(
    [string]$RepoRoot   = '',
    [string]$ModSources = '',
    [string]$TmlTargets = '',
    [string[]]$Mods     = @(
        'HighLight','BossCursor','LowHealthWarning',
        'AutoHeal','AutoAim','AutoFishing','AutoSwingAim','AutoTorch',
        'I-Have-My-Phone-Anyway','WorldMapIcons','ModSettingsHub',
        'CreativeMode','BlockLocator','MyLifeIsValuable')
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $scriptRoot }
if ([string]::IsNullOrWhiteSpace($ModSources)) {
    $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    $ModSources = Join-Path $documents 'My Games\Terraria\tModLoader\ModSources'
}
if ([string]::IsNullOrWhiteSpace($TmlTargets)) { $TmlTargets = $env:TIMF_TML_TARGETS }
if ([string]::IsNullOrWhiteSpace($TmlTargets)) {
    $nearbyTargets = @(
        (Join-Path $ModSources '..\tMLMod.targets'),
        (Join-Path $ModSources '..\tModLoader.targets')
    ) | ForEach-Object { [System.IO.Path]::GetFullPath($_) } | Where-Object { Test-Path -LiteralPath $_ }
    if ($nearbyTargets) { $TmlTargets = $nearbyTargets[0] }
}
if ([string]::IsNullOrWhiteSpace($TmlTargets) -or -not (Test-Path -LiteralPath $TmlTargets)) {
    throw 'tModLoader targets not found. Set TIMF_TML_TARGETS or pass -TmlTargets <path>.'
}
$bridgeDll = Join-Path $ModSources 'TimfBridge\bin\Debug\net8.0\TimfBridge.dll'
if (-not (Test-Path $bridgeDll)) { throw "Build TimfBridge first; not found: $bridgeDll" }

# --- tML tile-API replacement for BlockLocator -------------------------------------------------
function Fix-BlockLocator([string]$dir) {
    $f = Join-Path $dir 'BlockLocatorMod.cs'
    if (-not (Test-Path $f)) { return }
    $t = Get-Content $f -Raw

    # The native TIMF build can consume the shared TIMF.Pinyin library and exposes the
    # in-world feature-toggle capability. TimfBridge currently provides neither surface, so
    # keep the complete picker/label UI but make the tML port self-contained and name/ID based.
    $t = $t -replace '(?m)^using TIMF\.Pinyin;\r?\n', ''
    $t = $t -replace '(?m)^\s*\[TimfDependsOn\("TIMF\.Pinyin",.*\]\r?\n', ''
    $t = $t.Replace(', IModFeatureToggle', '')
    $t = $t -replace '(?ms)            IPinyinService pinyin;\r?\n            context\.Services\.TryGetService\(out pinyin\);\r?\n            if \(pinyin == null\)\r?\n                context\.Log\.Warn\("IPinyinService unavailable — TIMF\.Pinyin missing\? Search falls back to name/id only"\);\r?\n            _db = new TileDatabase\(context\.Log, pinyin\);', '            _db = new TileDatabase(context.Log);'
    $t = $t.Replace('Search (name / id / pinyin)', 'Search (name / id)')

    # Main.tile is a Tilemap struct in tML — drop the null-check on the tilemap.
    $t = $t -replace '(?m)^\s*if \(tiles == null\)\s*\r?\n\s*return;\s*\r?\n', ''
    # Tile is a value type; HasTile replaces active(), TileType replaces type.
    $t = $t -replace 'if \(tile == null \|\| !tile\.active\(\)\)', 'if (!tile.HasTile)'
    $t = $t -replace '\(int\)tile\.type', '(int)tile.TileType'
    Set-Content -Path $f -Value $t -Encoding UTF8 -NoNewline

    $dbFile = Join-Path $dir 'TileDatabase.cs'
    if (Test-Path $dbFile) {
        $db = Get-Content $dbFile -Raw
        $db = $db -replace '(?m)^using TIMF\.Pinyin;\r?\n', ''
        $db = $db -replace '(?m)^\s*private readonly IPinyinService _pinyin;\r?\n', ''
        $db = $db.Replace('public TileDatabase(ILogger log, IPinyinService pinyin)', 'public TileDatabase(ILogger log)')
        $db = $db -replace '(?m)^\s*_pinyin = pinyin;\r?\n', ''
        $db = $db -replace '(?m)^\s*Pinyin = _pinyin != null \? _pinyin\.ToPinyin\(name\) : "",\r?\n\s*Initials = _pinyin != null \? _pinyin\.ToInitials\(name\) : "",\r?\n', ''
        $db = $db.Replace('/// <summary>Name/pinyin/initials match via the shared service, or name-only fallback.</summary>', '/// <summary>Matches localized names; numeric IDs are handled by Search.</summary>')
        $db = $db -replace '(?ms)        private bool Matches\(TileEntry e, string queryLower\)\r?\n        \{.*?\r?\n        \}', @'
        private static bool Matches(TileEntry e, string queryLower)
        {
            if (string.IsNullOrEmpty(queryLower))
                return true;
            if (!string.IsNullOrEmpty(e.NameLower)
                && e.NameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            return !string.IsNullOrEmpty(e.Name)
                   && e.Name.IndexOf(queryLower, StringComparison.OrdinalIgnoreCase) >= 0;
        }
'@
        Set-Content -Path $dbFile -Value $db -Encoding UTF8 -NoNewline
    }

    # Keep the tML UI honest: this port searches localized names and numeric tile IDs;
    # pinyin remains available in the native TIMF build through TIMF.Pinyin.
    $locDir = Join-Path $dir 'Localization'
    if (Test-Path $locDir) {
        Get-ChildItem $locDir -Filter *.json -File | ForEach-Object {
            $loc = Get-Content $_.FullName -Raw
            $loc = $loc.Replace('Search (name / id / pinyin)', 'Search (name / id)')
            $loc = $loc.Replace('搜索（名称 / ID / 拼音）', '搜索（名称 / ID）')
            Set-Content -Path $_.FullName -Value $loc -Encoding UTF8 -NoNewline
        }
    }
}

# --- tML tile-API replacement for AutoTorch ----------------------------------------------------
function Fix-AutoTorch([string]$dir) {
    $f = Join-Path $dir 'AutoTorchMod.cs'
    if (-not (Test-Path $f)) { return }
    $t = Get-Content $f -Raw
    # tML Tile is a value struct: drop the null-check and use HasTile/TileType/WallType.
    $t = $t -replace 'if \(t == null\)\s*\r?\n\s*return false;\s*\r?\n\s*return !t\.active\(\);', 'return !t.HasTile;'
    $t = $t -replace 'if \(t != null && t\.wall > 0\)', 'if (t.WallType > 0)'
    $t = $t -replace 'if \(placed == null \|\| !placed\.active\(\) \|\| placed\.type != TileID\.Torches\)', 'if (!placed.HasTile || placed.TileType != TileID.Torches)'
    Set-Content -Path $f -Value $t -Encoding UTF8 -NoNewline
}

# --- NPinyin-free helper for CreativeMode ------------------------------------------------------
function Fix-CreativeMode([string]$dir) {
    $f = Join-Path $dir 'PinyinHelper.cs'
    if (-not (Test-Path $f)) { return }
    $content = @'
using System;

namespace CreativeMode
{
    /// <summary>
    /// Pinyin helper. NPinyin is unavailable under tModLoader (net8), so pinyin/initials search is
    /// disabled here; name substring matching (including raw CJK) still works via <see cref="Matches"/>.
    /// </summary>
    internal static class PinyinHelper
    {
        public static string ToPinyin(string text) => "";
        public static string ToInitials(string text) => "";

        public static bool Matches(string name, string nameLower, string pinyin, string initials, string queryLower)
        {
            if (string.IsNullOrEmpty(queryLower))
                return true;
            if (!string.IsNullOrEmpty(nameLower) && nameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            if (!string.IsNullOrEmpty(name) && name.IndexOf(queryLower, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(pinyin) && pinyin.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            if (!string.IsNullOrEmpty(initials) && initials.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }
    }
}
'@
    Set-Content -Path $f -Value $content -Encoding UTF8
}

# --- tML damage-class API: item.melee/magic/ranged/summon bools were removed in favor of DamageClass.
function Fix-DamageClass([string]$dir) {
    Get-ChildItem $dir -Filter *.cs -File | ForEach-Object {
        $p = $_.FullName
        $t = Get-Content $p -Raw
        $orig = $t
        $t = $t -replace 'item\.melee(?![A-Za-z0-9_])','item.CountsAsClass(DamageClass.Melee)'
        $t = $t -replace 'item\.magic(?![A-Za-z0-9_])','item.CountsAsClass(DamageClass.Magic)'
        $t = $t -replace 'item\.ranged(?![A-Za-z0-9_])','item.CountsAsClass(DamageClass.Ranged)'
        $t = $t -replace 'item\.summon(?![A-Za-z0-9_])','item.CountsAsClass(DamageClass.Summon)'
        # AutoAim uses 'held' as the item variable name.
        $t = $t -replace 'held\.melee(?![A-Za-z0-9_])','held.CountsAsClass(DamageClass.Melee)'
        $t = $t -replace 'held\.magic(?![A-Za-z0-9_])','held.CountsAsClass(DamageClass.Magic)'
        $t = $t -replace 'held\.ranged(?![A-Za-z0-9_])','held.CountsAsClass(DamageClass.Ranged)'
        $t = $t -replace 'held\.summon(?![A-Za-z0-9_])','held.CountsAsClass(DamageClass.Summon)'
        if ($t -ne $orig) {
            if ($t -notmatch 'using Terraria\.ModLoader;') {
                $t = $t -replace 'using Terraria;(\r?\n)','using Terraria;$1using Terraria.ModLoader;$1'
            }
            Set-Content -Path $p -Value $t -Encoding UTF8 -NoNewline
        }
    }
}

# MyLifeIsValuable uses HarmonyLib only for AccessTools.Method (MethodInfo lookup) and the Harmony
# parameter-name conventions (__instance/__result/__state). The bridge now owns Harmony (see
# BridgePatchService), so the ported mod stays HarmonyLib-free: drop the using, route lookups through
# a tiny reflection helper, and let the bridge's Harmony introspect the (unchanged) prefix/postfix.
function Fix-MyLifeIsValuable([string]$dir) {
    foreach ($fn in @('ChainMining.cs','BestReforge.cs','Fullbright.cs','PylonTeleport.cs')) {
        $p = Join-Path $dir $fn
        if (-not (Test-Path $p)) { continue }
        $t = Get-Content $p -Raw
        $t = $t -replace '(?m)^using HarmonyLib;\r?\n',''
        $t = $t.Replace('AccessTools.Method(','PatchReflect.Method(')
        $t = $t.Replace('AccessTools.TypeByName(','PatchReflect.TypeByName(')
        Set-Content -Path $p -Value $t -Encoding UTF8 -NoNewline
    }
    # tML Tile is a value type: HasTile replaces active(), TileType replaces type; drop struct null-checks.
    $cm = Join-Path $dir 'ChainMining.cs'
    if (Test-Path $cm) {
        $t = Get-Content $cm -Raw
        $t = $t.Replace('if (tile == null || !tile.active())', 'if (!tile.HasTile)')
        $t = $t.Replace('__state = tile.type;', '__state = tile.TileType;')
        $t = $t.Replace('if (tile != null && tile.active())', 'if (tile.HasTile)')
        $t = $t.Replace('if (t == null || !t.active() || t.type != type)', 'if (!t.HasTile || t.TileType != type)')
        $t = $t.Replace('if (after == null || !after.active())', 'if (!after.HasTile)')
        $t = $t.Replace('if (after.type != type)', 'if (after.TileType != type)')
        $t = $t.Replace('if (i >= 3 && after.active() && after.type == type)', 'if (i >= 3 && after.HasTile && after.TileType == type)')
        $t = $t.Replace('return final == null || !final.active();', 'return !final.HasTile;')
        Set-Content -Path $cm -Value $t -Encoding UTF8 -NoNewline
    }
    # Same Tile-struct fix in the main mod file (Add-current-target-as-chain-type helper).
    $mm = Join-Path $dir 'MyLifeIsValuableMod.cs'
    if (Test-Path $mm) {
        $t = Get-Content $mm -Raw
        $t = $t.Replace('if (tile == null || !tile.active())', 'if (!tile.HasTile)')
        $t = $t.Replace('_config.SetChainExtraType(tile.type, true);', '_config.SetChainExtraType(tile.TileType, true);')
        Set-Content -Path $mm -Value $t -Encoding UTF8 -NoNewline
    }
    # tML Item has no Prefix(int, out bool) overload and no GetRollablePrefixes(); use Prefix(int) and
    # let the existing brute-force prefix scan (candidates == null) compute the top-tier set.
    # Also: tML's Item.Prefix is Prefix(int prefixWeWant) (1 arg) and Main.ReforgeItemInReforgeSlot
    # does not exist — retarget the roll hook to Prefix(int) and drop the out-bool patch parameter
    # (Harmony binds prefix params by name; a param with no matching original arg would fail to patch).
    $br = Join-Path $dir 'BestReforge.cs'
    if (Test-Path $br) {
        $t = Get-Content $br -Raw
        $t = $t.Replace('item.Prefix(prefixId, out top)', 'item.Prefix(prefixId)')
        $t = $t.Replace('probe.GetRollablePrefixes()', '((int[])null)')
        $t = $t.Replace('new[] { typeof(int), typeof(bool).MakeByRefType() }', 'new[] { typeof(int) }')
        $t = $t -replace 'int prefixWeWant,\s*\r?\n\s*ref bool rolledPrefixIsTopTier\)', 'int prefixWeWant)'
        $t = $t -replace '\r?\n\s*rolledPrefixIsTopTier = true;', ''
        Set-Content -Path $br -Value $t -Encoding UTF8 -NoNewline
    }
    # tML's Main.DrawBlack is DrawBlack(bool force) (1 arg), not (bool, bool); fix the lookup so the
    # (parameterless) DrawBlack_Prefix actually gets attached and fullbright can skip the black fill.
    $fb = Join-Path $dir 'Fullbright.cs'
    if (Test-Path $fb) {
        $t = Get-Content $fb -Raw
        $t = $t.Replace('"DrawBlack", new[] { typeof(bool), typeof(bool) }', '"DrawBlack", new[] { typeof(bool) }')
        Set-Content -Path $fb -Value $t -Encoding UTF8 -NoNewline
    }
    @(
        'using System;'
        'using System.Reflection;'
        ''
        'namespace MyLifeIsValuable'
        '{'
        '    // Replaces HarmonyLib.AccessTools.Method for the tModLoader port. The bridge owns Harmony,'
        '    // so hosted mods stay HarmonyLib-free and only need MethodInfo lookup (public + non-public,'
        '    // instance + static), which is exactly what AccessTools.Method provided here.'
        '    internal static class PatchReflect'
        '    {'
        '        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;'
        '        public static MethodInfo Method(Type type, string name)'
        '            => type == null ? null : type.GetMethod(name, All);'
        '        public static MethodInfo Method(Type type, string name, Type[] parameters)'
        '            => type == null ? null : type.GetMethod(name, All, null, parameters ?? Type.EmptyTypes, null);'
        '        public static Type TypeByName(string name)'
        '        {'
        '            if (string.IsNullOrEmpty(name)) return null;'
        '            var t = Type.GetType(name);'
        '            if (t != null) return t;'
        '            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())'
        '            {'
        '                t = asm.GetType(name);'
        '                if (t != null) return t;'
        '            }'
        '            return null;'
        '        }'
        '    }'
        '}'
    ) | Set-Content -Path (Join-Path $dir 'PatchReflect.cs') -Encoding UTF8
}

# The raw TIMF names (AutoFishing, BossCursor, ...) collide with popular Steam Workshop mods,
# which then shadow our local dev builds. Prefix with 'Timf' to guarantee uniqueness; also
# strip characters that are illegal in a tML internal name (e.g. the hyphens in the phone mod).
$NameMap = @{
    'I-Have-My-Phone-Anyway' = 'TimfPhoneAnyway'
}
function Get-TmlName([string]$name) {
    if ($NameMap.ContainsKey($name)) { return $NameMap[$name] }
    return 'Timf' + ($name -replace '[^A-Za-z0-9_]', '')
}

$results = @()

foreach ($name in $Mods) {
    $srcMod = Join-Path $RepoRoot ("mods\" + $name)
    if (-not (Test-Path $srcMod)) { Write-Host "SKIP (missing source): $name"; continue }
    $tml = Get-TmlName $name
    # Remove any stale folder from a previous (raw-named) port so old names can't linger.
    $legacy = Join-Path $ModSources $name
    if (($legacy -ne (Join-Path $ModSources $tml)) -and (Test-Path $legacy)) { Remove-Item $legacy -Recurse -Force }
    $dst = Join-Path $ModSources $tml
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dst | Out-Null

    # Source (top-level .cs only; obj/bin live in subfolders) + assets.
    Get-ChildItem $srcMod -Filter *.cs -File | Copy-Item -Destination $dst -Force
    Get-ChildItem $srcMod -Filter *.png -File | Copy-Item -Destination $dst -Force -ErrorAction SilentlyContinue
    Get-ChildItem $srcMod -Filter *.default.json -File | Copy-Item -Destination $dst -Force -ErrorAction SilentlyContinue
    $locSrc = Join-Path $srcMod 'Localization'
    if (Test-Path $locSrc) { Copy-Item $locSrc (Join-Path $dst 'Localization') -Recurse -Force }

    # Per-mod source fixups for tML API differences.
    if ($name -eq 'BlockLocator') { Fix-BlockLocator $dst }
    if ($name -eq 'AutoTorch') { Fix-AutoTorch $dst }
    if ($name -eq 'CreativeMode') { Fix-CreativeMode $dst }
    if ($name -eq 'AutoAim' -or $name -eq 'AutoSwingAim') { Fix-DamageClass $dst }
    if ($name -eq 'MyLifeIsValuable') { Fix-MyLifeIsValuable $dst }

    # tML's AssemblyManager.VerifyMod requires at least one type whose namespace starts with
    # the internal (folder) name. The ported sources keep their original root namespace
    # (e.g. AutoFishing), which no longer matches the Timf* folder, so emit a marker type in
    # the folder namespace to satisfy the loader check without touching the ported code.
    @(
        "// Auto-generated by Port-ClientModsToTml.ps1 to satisfy tML's namespace/folder check."
        "namespace $tml"
        '{'
        '    internal static class NamespaceMarker'
        '    {'
        '    }'
        '}'
    ) | Set-Content -Path (Join-Path $dst 'NamespaceMarker.cs') -Encoding UTF8

    # build.txt — references TimfBridge so tML loads the bridge first and resolves TIMF.Abstractions.
    @(
        'author = TIMF'
        'version = 1.0.0'
        "displayName = $name (TIMF)"
        'side = Client'
        'modReferences = TimfBridge'
    ) | Set-Content -Path (Join-Path $dst 'build.txt') -Encoding UTF8

    "$name - TIMF client mod running inside tModLoader via TimfBridge." |
        Set-Content -Path (Join-Path $dst 'description.txt') -Encoding UTF8

    # Shipped csproj (tML build path resolves TimfBridge from modReferences).
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\tModLoader.targets" />
  <PropertyGroup>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -Path (Join-Path $dst ($tml + '.csproj')) -Encoding UTF8

    # Throwaway offline compile-verify project (references the built TimfBridge.dll directly).
    $validate = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>$tml</AssemblyName>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>latest</LangVersion>
    <BuildMod>false</BuildMod>
    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="TimfBridge"><HintPath>$bridgeDll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
  <Import Project="$TmlTargets" />
</Project>
"@
    $vproj = Join-Path $dst '_validate.csproj'
    $validate | Set-Content -Path $vproj -Encoding UTF8

    Write-Host ("=== building $name ===")
    $out = & dotnet build $vproj -p:BuildMod=false -v q 2>&1
    $ok = $LASTEXITCODE -eq 0
    $results += [pscustomobject]@{ Mod = $tml; Ok = $ok }
    if (-not $ok) { $out | Select-String -Pattern 'error ' | Select-Object -First 12 | ForEach-Object { Write-Host $_ } }

    # Clean verify artifacts (keep the shipped mod files).
    Remove-Item $vproj -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $dst 'bin') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $dst 'obj') -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '=== SUMMARY ==='
$results | ForEach-Object { Write-Host ("{0,-22} {1}" -f $_.Mod, ($(if($_.Ok){'OK'}else{'FAILED'}))) }
