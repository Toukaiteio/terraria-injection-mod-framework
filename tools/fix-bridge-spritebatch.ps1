# Harden ImmediateModeUi.Render's ambient SpriteBatch.End():
# probe FNA's private _beginCalled so we skip End() when no batch is active, instead of
# throwing a first-chance InvalidOperationException that tML logs as "silently caught".
$ErrorActionPreference = 'Stop'
$documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
$bridge = if ($env:TIMF_TML_BRIDGE) { $env:TIMF_TML_BRIDGE } else { Join-Path $documents 'My Games\Terraria\tModLoader\ModSources\TimfBridge' }
$f = Join-Path $bridge 'Ui\ImmediateModeUi.cs'
$raw = Get-Content $f -Raw
$nl = if ($raw -match "`r`n") { "`r`n" } else { "`n" }
$t = $raw -replace "`r`n", "`n"

$oldGuard = @"
                // Previous UI interface layer may still have Begin active.
                try { sb.End(); }
                catch (InvalidOperationException) { /* not begun */ }
                catch { /* ignore */ }
"@ -replace "`r`n", "`n"

$newGuard = @"
                // Another interface layer may or may not have a batch open (blur systems /
                // render-target pools in other mods reorder this). Only End() when one is actually
                // active (probe FNA's private _beginCalled) so we don't raise a first-chance
                // InvalidOperationException that tModLoader logs as "silently caught".
                if (SpriteBatchBegun(sb))
                {
                    try { sb.End(); }
                    catch { /* ignore */ }
                }
"@ -replace "`r`n", "`n"

$anchor = @"
        private void EnsureScissorRaster()
        {
            if (_scissorRaster != null)
                return;
"@ -replace "`r`n", "`n"

$helper = @"
        private static System.Reflection.FieldInfo _sbBeginField;
        private static bool _sbBeginFieldResolved;

        // FNA's SpriteBatch.End() throws if Begin() wasn't called. Probe the private _beginCalled
        // flag so we can skip End() when no batch is active, rather than catching (and letting tML
        // log) the first-chance exception. Falls back to "assume begun" if the field is absent.
        private static bool SpriteBatchBegun(SpriteBatch sb)
        {
            if (sb == null)
                return false;
            try
            {
                if (!_sbBeginFieldResolved)
                {
                    _sbBeginField =
                        sb.GetType().GetField("_beginCalled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?? sb.GetType().GetField("beginCalled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    _sbBeginFieldResolved = true;
                }
                if (_sbBeginField != null)
                    return (bool)_sbBeginField.GetValue(sb);
            }
            catch { /* fall through */ }
            return true;
        }

        private void EnsureScissorRaster()
        {
            if (_scissorRaster != null)
                return;
"@ -replace "`r`n", "`n"

if (-not $t.Contains($oldGuard)) { Write-Host 'GUARD anchor NOT found'; exit 2 }
if (-not $t.Contains($anchor))   { Write-Host 'HELPER anchor NOT found'; exit 3 }
$t = $t.Replace($oldGuard, $newGuard)
$t = $t.Replace($anchor, $helper)

$t = $t -replace "`n", $nl
Set-Content -Path $f -Value $t -Encoding UTF8 -NoNewline
Write-Host 'ImmediateModeUi.cs patched: SpriteBatchBegun guard + helper added'
