# Applies the Harmony-backed IModPatchService to TimfBridge:
#  - bundles net8 0Harmony.dll under lib/ and declares it in build.txt (dllReferences)
#  - references it for compile in the csproj
#  - swaps the no-op BridgePatchService stub for the real Harmony-backed file
#  - passes the mod id into BridgePatchService in TimfHost.LoadOne
$ErrorActionPreference = 'Stop'

$documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
$bridge   = if ($env:TIMF_TML_BRIDGE) { $env:TIMF_TML_BRIDGE } else { Join-Path $documents 'My Games\Terraria\tModLoader\ModSources\TimfBridge' }
$adapter  = Join-Path $bridge 'Adapter'
$repoRoot = Split-Path -Parent $PSScriptRoot
$staging  = Join-Path $repoRoot 'tools\tml-staging\Adapter'
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.nuget\packages' }
$harmony  = if ($env:TIMF_HARMONY_DLL) { $env:TIMF_HARMONY_DLL } else { Join-Path $nugetRoot 'lib.harmony\2.3.3\lib\net8.0\0Harmony.dll' }

# 1) Bundle Harmony
$lib = Join-Path $bridge 'lib'
New-Item -ItemType Directory -Force -Path $lib | Out-Null
Copy-Item $harmony (Join-Path $lib '0Harmony.dll') -Force
Write-Host '1) bundled lib/0Harmony.dll'

# 2) build.txt dllReferences
$bt = Join-Path $bridge 'build.txt'
$t = Get-Content $bt -Raw
if ($t -notmatch '(?m)^\s*dllReferences\s*=') {
    $t = $t -replace '(?m)^(side\s*=\s*Client\s*)$', "`$1`r`ndllReferences = 0Harmony"
    Set-Content $bt $t -Encoding UTF8 -NoNewline
    Write-Host '2) build.txt: added dllReferences = 0Harmony'
} else { Write-Host '2) build.txt already has dllReferences' }

# 3) csproj reference for compile
$cp = Join-Path $bridge 'TimfBridge.csproj'
$c = Get-Content $cp -Raw
if ($c -notmatch 'Include="0Harmony"') {
    $ig = "  <ItemGroup>`r`n    <Reference Include=""0Harmony""><HintPath>lib\0Harmony.dll</HintPath><Private>false</Private></Reference>`r`n  </ItemGroup>`r`n</Project>"
    $c = $c -replace '</Project>\s*$', $ig
    Set-Content $cp $c -Encoding UTF8 -NoNewline
    Write-Host '3) csproj: added 0Harmony reference'
} else { Write-Host '3) csproj already references 0Harmony' }

# 4) copy the real BridgePatchService.cs, remove the stub from BridgeStubs.cs
Copy-Item (Join-Path $staging 'BridgePatchService.cs') (Join-Path $adapter 'BridgePatchService.cs') -Force
$sb = Join-Path $adapter 'BridgeStubs.cs'
$s = Get-Content $sb -Raw
$stub = @'
    /// <summary>
    /// No-op patch broker. tModLoader ships MonoMod (not HarmonyLib), so the framework's Harmony-style
    /// postfix/prefix broker cannot be forwarded faithfully; mods that need detours should use tML's
    /// own MonoModHooks / On_ hooks. Mods relying on this broker are inert under the bridge.
    /// </summary>
    internal sealed class BridgePatchService : IModPatchService
    {
        public void PatchPostfix(System.Reflection.MethodInfo original, System.Reflection.MethodInfo postfix) { }
        public void PatchPrefix(System.Reflection.MethodInfo original, System.Reflection.MethodInfo prefix) { }
        public void Patch(System.Reflection.MethodInfo original, System.Reflection.MethodInfo prefix, System.Reflection.MethodInfo postfix) { }
        public void UnpatchAll() { }
    }

'@
if ($s.Contains('internal sealed class BridgePatchService')) {
    $s = $s.Replace($stub, '')
    $s = $s.Replace('the reflection', 'the reflection') # no-op guard
    $s = $s.Replace('the Harmony patch broker — tModLoader ships MonoMod rather than HarmonyLib, so the postfix/prefix broker is not forwarded.', 'the Harmony patch broker is now real (BridgePatchService, backed by a bundled net8 HarmonyLib).')
    Set-Content $sb $s -Encoding UTF8 -NoNewline
    Write-Host '4) removed stub BridgePatchService from BridgeStubs.cs'
} else { Write-Host '4) BridgeStubs.cs stub already removed' }

# 5) TimfHost.LoadOne -> pass mod id
$h = Join-Path $adapter 'TimfHost.cs'
$hc = Get-Content $h -Raw
if ($hc.Contains('new BridgePatchService(),')) {
    $hc = $hc.Replace('Patches = new BridgePatchService(),', 'Patches = new BridgePatchService(id),')
    Set-Content $h $hc -Encoding UTF8 -NoNewline
    Write-Host '5) TimfHost: BridgePatchService(id)'
} else { Write-Host '5) TimfHost already passes id (or pattern differs)'; (Get-Content $h | Select-String 'new BridgePatchService').Line }

Write-Host 'DONE'
