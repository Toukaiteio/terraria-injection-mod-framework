using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Postfix on Terraria.Map.MapIconOverlay.Draw(...): dispatches registered map-overlay hooks
    /// using the same transform parameters the game uses for its own icons. Runs inside the open
    /// SpriteBatch, on the fullscreen map, minimap and overlay.
    /// </summary>
    [HarmonyPatch]
    internal static class MapIconOverlayPatch
    {
        private static MapOverlayHookRegistry _registry;

        internal static void SetRegistry(MapOverlayHookRegistry registry)
        {
            _registry = registry;
        }

        private static MethodBase TargetMethod()
        {
            var type = typeof(Main).Assembly.GetType("Terraria.Map.MapIconOverlay");
            if (type == null)
                return null;
            // Draw(Vector2 mapPosition, Vector2 mapOffset, Rectangle? clippingRect,
            //      float mapScale, float drawScale, int alpha, ref string text)
            return AccessTools.Method(type, "Draw");
        }

        private static void Postfix(
            Vector2 mapPosition,
            Vector2 mapOffset,
            Rectangle? clippingRect,
            float mapScale,
            float drawScale,
            int alpha,
            ref string text)
        {
            try
            {
                if (_registry == null || Main.dedServ)
                    return;

                var info = new MapOverlayInfo
                {
                    MapPosition = mapPosition,
                    MapOffset = mapOffset,
                    ClippingRect = clippingRect,
                    MapScale = mapScale,
                    DrawScale = drawScale,
                    Alpha = alpha / 255f,
                    Fullscreen = Main.mapFullscreen,
                };

                text = _registry.Dispatch(info, text);
            }
            catch
            {
                // Never break map drawing.
            }
        }
    }
}
