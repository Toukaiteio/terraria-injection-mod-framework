using System;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    internal static class WallContentPatches
    {
        private static ContentManager _content;
        private static ILogger _log;
        private static TextureAssetSlots _wallSlots;
        private static readonly MethodInfo PreparePaintTextureMethod =
            AccessTools.Method(typeof(TilePaintSystemV2.ARenderTargetHolder), "PrepareTextureIfNecessary");

        internal static void Bind(ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;
            _wallSlots = null;
        }

        internal static void Install(Harmony harmony, ILogger log)
        {
            try
            {
                var place = AccessTools.Method(typeof(WorldGen), nameof(WorldGen.PlaceWall),
                    new[] { typeof(int), typeof(int), typeof(int), typeof(bool) });
                var drops = AccessTools.Method(typeof(WorldGen), "KillWall_GetItemDrops");
                var paint = AccessTools.Method(typeof(TilePaintSystemV2.WallRenderTargetHolder), "Prepare");
                if (place == null || drops == null || paint == null || PreparePaintTextureMethod == null)
                {
                    log.Error("Content: one or more custom wall hooks could not be resolved");
                    return;
                }
                harmony.Patch(place,
                    prefix: new HarmonyMethod(typeof(WallContentPatches), nameof(BeforePlaceWall)));
                harmony.Patch(drops,
                    postfix: new HarmonyMethod(typeof(WallContentPatches), nameof(AfterGetWallDrop)));
                harmony.Patch(paint,
                    prefix: new HarmonyMethod(typeof(WallContentPatches), nameof(BeforeWallPaintPrepare)));
                log.Info("Content: custom wall placement/drop/paint bridges installed");
            }
            catch (Exception ex)
            {
                log.Error("Content: custom wall hooks failed to install", ex);
            }
        }

        private static bool BeforePlaceWall(int i, int j, int type, bool mute)
        {
            if (_content == null || !_content.IsModdedWall(type))
                return true;
            if (i <= 1 || j <= 1 || i >= Main.maxTilesX - 2 || j >= Main.maxTilesY - 2)
                return false;
            var tile = Main.tile[i, j] ?? (Main.tile[i, j] = new Tile());
            if (tile.wall != 0)
                return false;
            tile.wall = (ushort)type;
            tile.wallFrameX(0);
            tile.wallFrameY(0);
            if (!mute)
            {
                try { SoundEngine.PlaySound(0, i * 16, j * 16, 1, 1f, 0f); }
                catch { }
            }
            return false;
        }

        private static void AfterGetWallDrop(Tile tileCache, ref int __result)
        {
            if (_content == null || tileCache == null || !_content.IsModdedWall(tileCache.wall))
                return;
            var def = _content.GetWall(tileCache.wall);
            if (def != null && def.ItemDrop > 0)
                __result = def.ItemDrop;
        }

        private static bool BeforeWallPaintPrepare(TilePaintSystemV2.WallRenderTargetHolder __instance)
        {
            if (__instance == null || _content == null || !_content.IsModdedWall(__instance.Key.WallType))
                return true;
            try
            {
                _wallSlots = _wallSlots ?? TextureAssetSlots.Resolve(_log, "Wall");
                var texture = _wallSlots?.GetTexture(__instance.Key.WallType);
                if (texture == null)
                {
                    _log?.Error("Content: no loaded texture for custom wall " + __instance.Key.WallType);
                    return false;
                }
                PreparePaintTextureMethod.Invoke(__instance, new object[] { texture, null });
            }
            catch (Exception ex)
            {
                _log?.Error("Content: custom wall paint preparation failed", ex.InnerException ?? ex);
            }
            return false;
        }
    }
}
