using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TIMF.Abstractions;
using TIMF.Core.Keybinds;
using TIMF.Core.Modding;
using TIMF.Core.Session;
using TIMF.Core.UI;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Prefix on Main.DrawMenu: block vanilla main-menu clicks when the pointer is over a TIMF
    /// window that was open last frame. Menu items read mouseLeft during DrawMenu itself —
    /// DrawCursor (where we build UI) is too late for the same-frame click.
    /// </summary>
    [HarmonyPatch]
    internal static class DrawMenuInputBlockPatch
    {
        private static GameHooks _hooks;

        internal static void SetHooks(GameHooks hooks)
        {
            _hooks = hooks;
        }

        private static MethodBase TargetMethod()
        {
            // protected void DrawMenu(GameTime gameTime)
            return AccessTools.Method(typeof(Main), "DrawMenu", new[] { typeof(GameTime) });
        }

        private static void Prefix()
        {
            try
            {
                _hooks?.EarlyBlockUiInput();
            }
            catch
            {
                // Never break the main menu.
            }
        }
    }

    /// <summary>
    /// Prefix on Main.DrawCursor: flush TIMF UI just before the vanilla cursor so panels sit
    /// under the real pointer. Covers in-world interface, fullscreen map, and main menu
    /// (DrawMenu calls DrawCursor directly — it never goes through DrawInterface_36_Cursor).
    ///
    /// After we draw we re-Begin a cursor-style batch so vanilla DrawCursor can issue Draw calls.
    /// </summary>
    [HarmonyPatch]
    internal static class DrawCursorUiPatch
    {
        private static GameHooks _hooks;
        private static bool _ranThisFrame;

        internal static void SetHooks(GameHooks hooks)
        {
            _hooks = hooks;
            _ranThisFrame = false;
        }

        internal static bool RanThisFrame
        {
            get { return _ranThisFrame; }
        }

        internal static void ResetFrame()
        {
            _ranThisFrame = false;
        }

        private static MethodBase TargetMethod()
        {
            // public static void DrawCursor(Vector2 bonus, bool smart = false)
            return AccessTools.Method(typeof(Main), "DrawCursor", new[] { typeof(Vector2), typeof(bool) });
        }

        private static void Prefix()
        {
            if (_hooks == null)
                return;

            if (!_ranThisFrame)
            {
                _ranThisFrame = true;
                try
                {
                    _hooks.RunUiPass(null);
                }
                catch
                {
                    // Never break the cursor.
                }
            }

            // Vanilla DrawCursor assumes an open SpriteBatch (UIScaleMatrix + cursor sampler).
            // RunUiPass / mod PostDraw may have End'd it — restore a safe cursor batch.
            EnsureCursorBatchOpen();
        }

        private static void EnsureCursorBatchOpen()
        {
            try
            {
                var sb = Main.spriteBatch;
                if (sb == null)
                    return;

                try { sb.End(); }
                catch (InvalidOperationException) { /* not begun */ }
                catch { /* ignore */ }

                try
                {
                    // Mirror vanilla cursor Begin (SamplerStateForCursor ≈ PointClamp).
                    sb.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.AlphaBlend,
                        SamplerState.PointClamp,
                        DepthStencilState.None,
                        RasterizerState.CullCounterClockwise,
                        null,
                        Main.UIScaleMatrix);
                }
                catch
                {
                    // ignore — better to skip our restore than crash the frame
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    internal sealed class GameHooks
    {
        private readonly ILogger _log;
        private readonly ModLoader _mods;
        private readonly MenuVersionOverlay _menuVersion;
        private readonly PlayerUpdateHookRegistry _playerUpdate;
        private readonly MapOverlayHookRegistry _mapOverlay;
        private readonly InfoAccessoryHookRegistry _infoAcc;
        private readonly KeybindService _keybinds;
        private SessionService _session;
        private Harmony _harmony;
        private bool _installed;
        private bool _versionPatchInstalled;
        private bool _cursorUiPatchInstalled;
        private Action<GameTime> _postDrawHandler;

        public GameHooks(ILogger log, ModLoader mods)
        {
            _log = log;
            _mods = mods;
            _menuVersion = new MenuVersionOverlay(log);
            _playerUpdate = new PlayerUpdateHookRegistry(log);
            _mapOverlay = new MapOverlayHookRegistry(log);
            _infoAcc = new InfoAccessoryHookRegistry(log);
            _keybinds = new KeybindService(log);
            _session = new SessionService(log, mods);
        }

        public void RegisterServices()
        {
            try
            {
                _mods.Services.Register<IPlayerUpdateHookRegistry>(_playerUpdate);
                _mods.Services.Register<IMapOverlayHookRegistry>(_mapOverlay);
                _mods.Services.Register<IInfoAccessoryHookRegistry>(_infoAcc);
                _mods.Services.Register<IKeybindService>(_keybinds);
                _mods.Services.Register<ITimfSession>(_session);
                KeybindUiPatches.SetService(_keybinds, _log);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to register framework hook services", ex);
            }
        }

        public void Install()
        {
            if (_installed)
                return;

            try
            {
                _harmony = new Harmony("timf.core");
                DrawVersionNumberPatch.SetOverlay(_menuVersion);
                PlayerUpdatePatch.SetRegistry(_playerUpdate);
                MapIconOverlayPatch.SetRegistry(_mapOverlay);
                RefreshInfoAccsPatch.SetRegistry(_infoAcc);
                DrawCursorUiPatch.SetHooks(this);
                DrawMenuInputBlockPatch.SetHooks(this);
                KeybindUiPatches.SetService(_keybinds, _log);
                _harmony.PatchAll(typeof(DrawVersionNumberPatch).Assembly);
                // Keybind UI patches are registered explicitly (class has no [HarmonyPatch] marker
                // for PatchAll discovery of nested method attributes in all Harmony versions).
                KeybindUiPatches.Install(_harmony, _log);
                _versionPatchInstalled = true;
                _cursorUiPatchInstalled = true;
                _log.Info("Harmony patches installed (ItemCheck, DrawCursor UI flush, DrawMenu input block, keybind UI, UpdateEquips/RefreshInfoAccs info-acc hooks)");
            }
            catch (Exception ex)
            {
                _versionPatchInstalled = false;
                _cursorUiPatchInstalled = false;
                DrawCursorUiPatch.SetHooks(null);
                DrawMenuInputBlockPatch.SetHooks(null);
                _log.Error("Harmony patch install failed; UI will use OnPostDraw fallback", ex);
            }

            _postDrawHandler = OnPostDraw;
            Main.OnPostDraw += _postDrawHandler;
            _installed = true;
            _log.Info("Subscribed to Main.OnPostDraw");

            try
            {
                _session?.Start();
            }
            catch (Exception ex)
            {
                _log.Error("SessionService.Start failed", ex);
            }
        }

        public void Uninstall()
        {
            if (!_installed)
                return;

            try { _session?.Stop(); } catch { /* ignore */ }

            try
            {
                if (_postDrawHandler != null)
                    Main.OnPostDraw -= _postDrawHandler;
            }
            catch (Exception ex)
            {
                _log.Error("Failed to unsubscribe OnPostDraw", ex);
            }

            try
            {
                _harmony?.UnpatchAll("timf.core");
            }
            catch (Exception ex)
            {
                _log.Error("Harmony unpatch failed", ex);
            }

            DrawCursorUiPatch.SetHooks(null);
            DrawMenuInputBlockPatch.SetHooks(null);
            KeybindUiPatches.SetService(null);
            _installed = false;
        }

        /// <summary>
        /// Early click block for main-menu (and any other pre-DrawCursor consumers).
        /// Uses last frame's TIMF window rects.
        /// </summary>
        internal void EarlyBlockUiInput()
        {
            try
            {
                IUiHost uiHost;
                if (_mods.Services.TryGetService(out uiHost) && uiHost != null)
                    uiHost.EarlyBlockGameInput();
            }
            catch (Exception ex)
            {
                _log.Error("EarlyBlockUiInput failed", ex);
            }
        }

        /// <summary>
        /// One UI frame: close any open batch → NewFrame → mod PostDraw (build) → Render → click block.
        /// </summary>
        internal void RunUiPass(GameTime gameTime)
        {
            try
            {
                // Always tick session (handshake / SP activate), including dedicated server.
                try { _session?.Poll(); }
                catch (Exception ex) { _log.Error("SessionService.Poll failed", ex); }

                if (Main.dedServ)
                    return;

                // Critical: DrawCursor / interface layers often leave SpriteBatch open.
                // Overlay mods (HighLight, BossCursor, …) do their own Begin/End — they need a closed batch.
                SafeEndSpriteBatch();

                // Keep language service in sync with the game's active culture.
                try { _mods.Language?.Poll(); }
                catch { /* ignore */ }

                IUiHost uiHost = null;
                try
                {
                    _mods.Services.TryGetService(out uiHost);
                    uiHost?.NewFrame(gameTime ?? new GameTime());
                }
                catch (Exception ex)
                {
                    _log.Error("IUiHost.NewFrame failed", ex);
                }

                if (!_versionPatchInstalled)
                {
                    try { _menuVersion.Draw(); }
                    catch (Exception ex) { _log.Error("MenuVersionOverlay threw", ex); }
                }

                var list = _mods.Mods;
                for (var i = 0; i < list.Count; i++)
                {
                    try { list[i].PostDraw(gameTime ?? new GameTime()); }
                    catch (Exception ex) { _log.Error("PostDraw failed in mod " + list[i].Name, ex); }
                }

                // Ensure batch is closed again before UI host opens its own.
                SafeEndSpriteBatch();

                try { uiHost?.Render(); }
                catch (Exception ex) { _log.Error("IUiHost.Render failed", ex); }

                try
                {
                    IImmediateModeUi imui;
                    if (_mods.Services.TryGetService(out imui) && imui != null && imui.WantCaptureMouse)
                    {
                        try
                        {
                            if (Main.LocalPlayer != null)
                                Main.LocalPlayer.mouseInterface = true;
                        }
                        catch { /* ignore */ }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("WantCaptureMouse input block failed", ex);
                }
            }
            catch (Exception ex)
            {
                _log.Error("RunUiPass error", ex);
            }
        }

        private static void SafeEndSpriteBatch()
        {
            try
            {
                var sb = Main.spriteBatch;
                if (sb == null)
                    return;
                try { sb.End(); }
                catch (InvalidOperationException) { /* not begun */ }
                catch { /* ignore */ }
            }
            catch
            {
                // ignore
            }
        }

        private void OnPostDraw(GameTime gameTime)
        {
            try
            {
                // If DrawCursor prefix already built+drew UI this frame, just reset.
                // Otherwise (edge cases / patch missing) run the full pass here.
                if (!_cursorUiPatchInstalled || !DrawCursorUiPatch.RanThisFrame)
                    RunUiPass(gameTime);
            }
            finally
            {
                DrawCursorUiPatch.ResetFrame();
            }
        }
    }
}
