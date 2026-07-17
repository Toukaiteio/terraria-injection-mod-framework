using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Core.UI;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Runs immediately after Main.DrawVersionNumber so TIMF text shares the
    /// same open SpriteBatch / UIScaleMatrix / sampler state as the vanilla version.
    /// </summary>
    [HarmonyPatch]
    internal static class DrawVersionNumberPatch
    {
        private static MenuVersionOverlay _overlay;

        internal static void SetOverlay(MenuVersionOverlay overlay)
        {
            _overlay = overlay;
        }

        // Target private static Main.DrawVersionNumber(Color, float)
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Main), "DrawVersionNumber", new[] { typeof(Color), typeof(float) });
        }

        // Signature must match Main.DrawVersionNumber(Color menuColor, float upBump)
        private static void Postfix(Color menuColor, float upBump)
        {
            try
            {
                _overlay?.DrawInMenuBatch(menuColor, upBump);
            }
            catch
            {
                // Never break the menu if overlay fails.
            }
        }
    }
}
