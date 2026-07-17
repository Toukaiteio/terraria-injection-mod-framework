using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;
using TIMF.Core.Modding;
using TIMF.Core.UI;

namespace TIMF.Core.Hooks
{
    internal sealed class GameHooks
    {
        private readonly ILogger _log;
        private readonly ModLoader _mods;
        private readonly MenuVersionOverlay _menuVersion;
        private Harmony _harmony;
        private bool _installed;
        private bool _versionPatchInstalled;
        private Action<GameTime> _postDrawHandler;

        public GameHooks(ILogger log, ModLoader mods)
        {
            _log = log;
            _mods = mods;
            _menuVersion = new MenuVersionOverlay(log);
        }

        public void Install()
        {
            if (_installed)
                return;

            // Prefer drawing TIMF version inside the same SpriteBatch as vanilla version.
            try
            {
                _harmony = new Harmony("timf.core");
                DrawVersionNumberPatch.SetOverlay(_menuVersion);
                _harmony.PatchAll(typeof(DrawVersionNumberPatch).Assembly);
                // PatchAll patches every [HarmonyPatch] in the assembly; that's fine for now.
                _versionPatchInstalled = true;
                _log.Info("Harmony patch installed: Main.DrawVersionNumber postfix");
            }
            catch (Exception ex)
            {
                _versionPatchInstalled = false;
                _log.Error("Harmony DrawVersionNumber patch failed; falling back to OnPostDraw overlay", ex);
            }

            _postDrawHandler = OnPostDraw;
            Main.OnPostDraw += _postDrawHandler;
            _installed = true;
            _log.Info("Subscribed to Main.OnPostDraw");
        }

        public void Uninstall()
        {
            if (!_installed)
                return;

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

            _installed = false;
        }

        private void OnPostDraw(GameTime gameTime)
        {
            try
            {
                if (Main.dedServ)
                    return;

                // Only use PostDraw fallback when Harmony patch is unavailable.
                if (!_versionPatchInstalled)
                {
                    try
                    {
                        _menuVersion.Draw();
                    }
                    catch (Exception ex)
                    {
                        _log.Error("MenuVersionOverlay threw", ex);
                    }
                }

                var list = _mods.Mods;
                for (var i = 0; i < list.Count; i++)
                {
                    try
                    {
                        list[i].PostDraw(gameTime);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("PostDraw failed in mod " + list[i].Name, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("PostDraw dispatcher error", ex);
            }
        }
    }
}
