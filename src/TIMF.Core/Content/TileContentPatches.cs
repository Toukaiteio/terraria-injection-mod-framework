using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ObjectData;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Bridges custom tile ids through placement code that is hard-coded around vanilla tile
    /// categories. Simple 1x1 mod tiles use a small direct placement path: vanilla's generic
    /// fallback writes the tile, but then unconditionally enters SquareTileFrame, where several
    /// internal tables are still limited to vanilla ids and can abort the entire item use.
    /// </summary>
    internal static class TileContentPatches
    {
        private static ContentManager _content;
        private static ILogger _log;
        private static TextureAssetSlots _tileTextureSlots;
        private static readonly FieldInfo SceneTileCountsField =
            AccessTools.Field(typeof(SceneMetrics), "_tileCounts");
        private static readonly MethodInfo PreparePaintTextureMethod =
            AccessTools.Method(typeof(TilePaintSystemV2.ARenderTargetHolder),
                "PrepareTextureIfNecessary");

        internal static void Bind(ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;
            _tileTextureSlots = null;
        }

        internal static void Install(Harmony harmony, ILogger log)
        {
            _log = log;
            try
            {
                var playerPlacement = AccessTools.Method(typeof(Player), "PlaceThing_Tiles_TryPlacing");
                if (playerPlacement == null)
                {
                    log.Error("Content: Player.PlaceThing_Tiles_TryPlacing not found — custom tiles cannot be placed by items");
                    return;
                }

                harmony.Patch(playerPlacement,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeTryPlacing)));

                var setAdjTile = AccessTools.Method(typeof(Player), nameof(Player.SetAdjTile),
                    new[] { typeof(int) });
                if (setAdjTile == null)
                {
                    log.Error("Content: Player.SetAdjTile not found — opening inventory near custom tiles is unsafe");
                    return;
                }
                harmony.Patch(setAdjTile,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeSetAdjTile)));

                var playerCommit = AccessTools.Method(typeof(Player), "PlaceThing_Tiles_PlaceIt");
                if (playerCommit == null)
                {
                    log.Error("Content: Player.PlaceThing_Tiles_PlaceIt not found — custom placement cannot finish safely");
                    return;
                }

                harmony.Patch(playerCommit,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforePlayerPlaceIt)));

                var worldPlacement = AccessTools.Method(typeof(WorldGen), nameof(WorldGen.PlaceTile),
                    new[]
                    {
                        typeof(int), typeof(int), typeof(int), typeof(bool),
                        typeof(bool), typeof(int), typeof(int)
                    });
                if (worldPlacement == null)
                {
                    log.Error("Content: WorldGen.PlaceTile not found — simple custom tiles cannot be committed to the world");
                    return;
                }

                harmony.Patch(worldPlacement,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeWorldPlaceTile)));

                var tileFrame = AccessTools.Method(typeof(WorldGen), nameof(WorldGen.TileFrame),
                    new[] { typeof(int), typeof(int), typeof(bool), typeof(bool) });
                var cosmeticFrame = AccessTools.Method(typeof(WorldGen), nameof(WorldGen.TileFrameCosmetic),
                    new[] { typeof(int), typeof(int), typeof(Tile), typeof(bool) });
                if (tileFrame == null || cosmeticFrame == null)
                {
                    log.Error("Content: tile framing methods not found — single-frame custom tiles may become invisible");
                    return;
                }
                harmony.Patch(tileFrame,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeTileFrame)));
                harmony.Patch(cosmeticFrame,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeCosmeticTileFrame)));

                var sceneScan = AccessTools.Method(typeof(SceneMetrics), "ScanTiles");
                if (sceneScan == null || SceneTileCountsField == null)
                {
                    log.Error("Content: SceneMetrics.ScanTiles/_tileCounts not found — custom tiles would crash biome scanning");
                    return;
                }

                harmony.Patch(sceneScan,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeSceneTileScan)));

                var paintPrepare = AccessTools.Method(
                    typeof(TilePaintSystemV2.TileRenderTargetHolder), "Prepare");
                if (paintPrepare == null || PreparePaintTextureMethod == null)
                {
                    log.Error("Content: tile paint preparation methods not found — custom tile rendering is unsafe");
                    return;
                }

                harmony.Patch(paintPrepare,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeTilePaintPrepare)));

                var itemDrops = AccessTools.Method(typeof(WorldGen), "KillTile_GetItemDrops");
                if (itemDrops == null)
                {
                    log.Error("Content: WorldGen.KillTile_GetItemDrops not found — custom tiles cannot drop items");
                    return;
                }

                harmony.Patch(itemDrops,
                    postfix: new HarmonyMethod(typeof(TileContentPatches), nameof(AfterGetTileItemDrops)));

                var canKillTile = AccessTools.Method(typeof(WorldGen), nameof(WorldGen.CanKillTile),
                    new[] { typeof(int), typeof(int), typeof(bool).MakeByRefType() });
                var chestDrop = AccessTools.Method(typeof(WorldGen), "GetItemDrop_Chests",
                    new[] { typeof(int), typeof(int), typeof(int) });
                if (canKillTile == null || chestDrop == null)
                {
                    log.Error("Content: custom container destruction hooks not found");
                    return;
                }
                harmony.Patch(canKillTile,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeCanKillTile)));
                harmony.Patch(chestDrop,
                    prefix: new HarmonyMethod(typeof(TileContentPatches), nameof(BeforeGetChestDrop)));

                var lightScanner = typeof(Main).Assembly.GetType("Terraria.Graphics.Light.TileLightScanner");
                var getTileLight = AccessTools.Method(lightScanner, "GetTileLight",
                    new[] { typeof(int), typeof(int), typeof(Vector3).MakeByRefType() });
                if (getTileLight == null)
                {
                    log.Error("Content: TileLightScanner.GetTileLight not found — custom luminous tiles will not emit light");
                    return;
                }
                harmony.Patch(getTileLight,
                    postfix: new HarmonyMethod(typeof(TileContentPatches), nameof(AfterGetTileLight)));
                log.Info("Content: custom tile placement bridges installed");
            }
            catch (Exception ex)
            {
                log.Error("Content: custom tile player-placement bridge failed to install", ex);
            }
        }

        private static void BeforeTryPlacing(
            Player __instance,
            int tileToCreate,
            ref bool? overrideCanPlace,
            int placeStyle)
        {
            var content = _content;
            if (content == null || __instance == null || !content.IsModdedTile(tileToCreate))
                return;

            var definition = content.GetTile(tileToCreate);
            if (!content.IsSessionAllowed(definition))
            {
                overrideCanPlace = false;
                return;
            }

            // Definitions with TileObjectData have their own anchor and footprint rules; the
            // vanilla object-placement path already handles them and must remain authoritative.
            try
            {
                if (TileObjectData.CustomPlace(tileToCreate, placeStyle))
                    return;
            }
            catch
            {
                // If this version cannot answer, treat it as a simple tile below.
            }

            var x = Player.tileTargetX;
            var y = Player.tileTargetY;
            if (x <= 0 || y <= 0 || x >= Main.maxTilesX - 1 || y >= Main.maxTilesY - 1)
                return;

            var target = Main.tile[x, y];
            if (target == null || target.active())
                return;

            if (!HasPlacementSupport(x, y))
                return;

            overrideCanPlace = true;
        }

        private static bool BeforeSetAdjTile(Player __instance, int tileType)
        {
            if (__instance == null || tileType < 0)
                return false;
            try
            {
                var current = __instance.adjTile;
                if (current != null && tileType < current.Length)
                    return true;

                var required = Math.Max((int)TileID.Count, tileType + 1);
                // A tile from the world should always be below TileID.Count. Refuse a corrupt
                // value rather than allocate an arbitrarily large array from it.
                if (tileType >= TileID.Count || required > ushort.MaxValue)
                {
                    _log?.Warn("Content: ignored invalid adjacent tile id " + tileType);
                    return false;
                }

                var grown = new bool[required];
                if (current != null)
                    Array.Copy(current, grown, current.Length);
                __instance.adjTile = grown;
                _log?.Info("Content: expanded late Player.adjTile "
                           + (current?.Length ?? 0) + " -> " + required);
                return true;
            }
            catch (Exception ex)
            {
                _log?.Error("Content: late Player.adjTile expansion failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Completes a simple custom placement without running Player's long list of vanilla
        /// post-placement handlers. Those handlers assume the tile belongs to one of their
        /// hard-coded furniture/plant/trap categories; an exception there occurs before
        /// ItemCheck decrements itemAnimation, leaving the player permanently frozen in the
        /// use pose even though the block was already committed.
        /// </summary>
        private static bool BeforePlayerPlaceIt(
            Player __instance,
            bool newObjectType,
            TileObject data,
            int tileToCreate,
            ref TileObject __result)
        {
            var content = _content;
            if (__instance == null || content == null || !content.IsModdedTile(tileToCreate))
                return true;

            // TileObjectData definitions need the original atomic multi-cell path.
            if (newObjectType)
                return true;
            try
            {
                if (TileObjectData.CustomPlace(tileToCreate, __instance.HeldItem.placeStyle))
                    return true;
            }
            catch
            {
                // No TileObjectData means the direct one-cell path below.
            }

            __result = data;
            var x = Player.tileTargetX;
            var y = Player.tileTargetY;
            var style = __instance.HeldItem.placeStyle;
            var placed = WorldGen.PlaceTile(
                x, y, tileToCreate, false, false, __instance.whoAmI, style);
            if (!placed)
                return false;

            // These are the only generic effects required for ItemCheck to finish the use and
            // consume one item. Consumption itself remains in vanilla's normal end-of-use path.
            __instance.ApplyItemTime(__instance.HeldItem, __instance.tileSpeed);
            try { SoundEngine.PlaySound(0, x * 16, y * 16, 1, 1f, 0f); }
            catch { /* sound must never invalidate a successful placement */ }

            if (Main.netMode != 0)
            {
                NetMessage.SendData(17, -1, -1, null, 1,
                    x, y, tileToCreate, style, 0, 0);
            }

            return false;
        }

        /// <summary>
        /// Commits an ordinary one-cell mod tile without entering vanilla's id-specific tile
        /// switch and framing pipeline. ObjectData-backed definitions retain the vanilla path,
        /// because their footprint and anchors must be placed atomically by TileObject.
        /// </summary>
        private static bool BeforeWorldPlaceTile(
            int i,
            int j,
            int Type,
            bool mute,
            bool forced,
            int plr,
            int style,
            ref bool __result)
        {
            var content = _content;
            if (content == null || !content.IsModdedTile(Type))
                return true;

            try
            {
                if (TileObjectData.CustomPlace(Type, style))
                    return true;
            }
            catch
            {
                // A type without TileObjectData is a simple one-cell tile.
            }

            __result = false;
            if (i <= 0 || j <= 0 || i >= Main.maxTilesX - 1 || j >= Main.maxTilesY - 1)
                return false;

            var tile = Main.tile[i, j];
            if (tile == null)
            {
                tile = new Tile();
                Main.tile[i, j] = tile;
            }

            if (tile.active())
                return false;

            // Match the first gate in WorldGen.PlaceTile. The player path already performs its
            // normal reach/support checks; this prevents programmatic calls from placing a solid
            // tile through an entity unless the caller explicitly requested a forced placement.
            if (!forced && Main.tileSolid[Type] && !Collision.EmptyTile(i, j))
                return false;

            try
            {
                // Keep the existing wall and liquid, as vanilla placement does. Only stale block
                // shape/paint state belonging to a previously removed tile is reset.
                tile.Clear(TileDataType.Tile | TileDataType.TilePaint | TileDataType.Slope);
                tile.type = (ushort)Type;
                tile.frameX = 0;
                tile.frameY = 0;
                tile.active(true);
                __result = true;

                _log?.Info("Content: placed simple custom tile type=" + Type
                           + " at " + i + "," + j);
            }
            catch (Exception ex)
            {
                // Never leave a half-active custom tile behind if the direct write itself fails.
                try { tile.active(false); }
                catch { }
                _log?.Error("Content: direct custom tile placement failed for type=" + Type
                            + " at " + i + "," + j, ex);
                __result = false;
            }

            // false tells Harmony to skip the original WorldGen.PlaceTile implementation.
            return false;
        }

        private static bool BeforeTileFrame(int i, int j)
        {
            if (i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY)
                return true;
            return KeepSingleFrameIfNeeded(Main.tile[i, j]);
        }

        private static bool BeforeCosmeticTileFrame(Tile tileCache)
        {
            return KeepSingleFrameIfNeeded(tileCache);
        }

        private static bool KeepSingleFrameIfNeeded(Tile tile)
        {
            if (tile == null || !tile.active())
                return true;
            var def = _content?.GetTile(tile.type);
            if (def == null || def.PlacementTemplateTile >= 0)
                return true;
            tile.frameX = 0;
            tile.frameY = 0;
            return false;
        }

        /// <summary>
        /// SceneMetrics instances are constructed before content activation, and their readonly
        /// _tileCounts arrays therefore retain the vanilla TileID.Count. They are instance fields,
        /// so the assembly-wide static array expander cannot discover them. Repair each live
        /// metrics object immediately before it scans tiles; this covers both Main's player and
        /// camera metrics objects as well as any instance Terraria may create later.
        /// </summary>
        private static bool BeforeSceneTileScan(SceneMetrics __instance)
        {
            if (__instance == null || SceneTileCountsField == null)
                return true;

            try
            {
                var current = SceneTileCountsField.GetValue(__instance) as int[];
                var required = (int)TileID.Count;
                if (current == null)
                {
                    _log?.Error("Content: SceneMetrics._tileCounts is null; skipping this tile scan");
                    return false;
                }
                if (current.Length >= required)
                    return true;

                var grown = new int[required];
                Array.Copy(current, grown, current.Length);
                SceneTileCountsField.SetValue(__instance, grown);
                _log?.Info("Content: expanded SceneMetrics._tileCounts "
                           + current.Length + " -> " + required);
                return true;
            }
            catch (Exception ex)
            {
                // AggregateTileCounts only reads vanilla indices, so omitting this scan is a safe
                // degradation. Letting it continue with a short array would crash the draw loop.
                _log?.Error("Content: could not expand SceneMetrics._tileCounts; skipping this tile scan", ex);
                return false;
            }
        }

        /// <summary>
        /// Vanilla paint preparation re-requests TextureAssets.Tile[id].Name from Main.Assets.
        /// TIMF's assets already contain a loaded Texture2D but intentionally do not live in the
        /// vanilla repository, so requesting their synthetic TIMF/... name throws
        /// AssetLoadException. Feed the injected texture straight into the original render-target
        /// builder instead.
        /// </summary>
        private static bool BeforeTilePaintPrepare(
            TilePaintSystemV2.TileRenderTargetHolder __instance)
        {
            if (__instance == null)
                return true;

            var type = __instance.Key.TileType;
            var content = _content;
            if (content == null || !content.IsModdedTile(type))
                return true;

            try
            {
                var slots = _tileTextureSlots;
                if (slots == null)
                {
                    slots = TextureAssetSlots.Resolve(_log, "Tile");
                    _tileTextureSlots = slots;
                }

                var texture = slots?.GetTexture(type);
                if (texture == null)
                {
                    _log?.Error("Content: no loaded texture available for tile paint type=" + type);
                    return false;
                }

                PreparePaintTextureMethod.Invoke(__instance, new object[] { texture, null });
            }
            catch (Exception ex)
            {
                _log?.Error("Content: custom tile paint preparation failed for type=" + type,
                            ex.InnerException ?? ex);
            }

            // Never enter the vanilla Main.Assets.Request path for a synthetic TIMF asset name.
            return false;
        }

        /// <summary>
        /// Supplies the standard WorldGen drop pipeline with a custom tile's declared item.
        /// KillTile_DropItems already limits spawning to single-player/the server and creates
        /// the correct tile-break entity source, so networking and pickup behavior remain
        /// identical to vanilla drops.
        /// </summary>
        private static void AfterGetTileItemDrops(
            Tile tileCache,
            ref int dropItem,
            ref int dropItemStack,
            ref bool noPrefix,
            bool includeLargeObjectDrops)
        {
            var content = _content;
            if (content == null || tileCache == null || !content.IsModdedTile(tileCache.type))
                return;

            var definition = content.GetTile(tileCache.type);
            if (definition == null || definition.ItemDrop <= 0)
                return;

            // Multi-cell objects are first broken one cell at a time, then their generic
            // object checker requests one large-object drop. Supplying a drop on the initial
            // per-cell calls duplicates furniture. One-cell templates (for example torches)
            // retain their normal immediate drop.
            if (!includeLargeObjectDrops && IsMultiCellObject(tileCache.type))
                return;

            // A future specialized vanilla-compatible hook may already have supplied a drop;
            // do not duplicate it. Ordinary custom ids reach this point with dropItem == 0.
            if (dropItem != 0)
                return;

            dropItem = definition.ItemDrop;
            dropItemStack = Math.Max(1, definition.ItemDropStack);
            noPrefix = true;
        }

        private static bool IsMultiCellObject(int type)
        {
            try
            {
                var data = TileObjectData.GetTileData(type, 0, 0);
                return data != null && (data.Width > 1 || data.Height > 1);
            }
            catch { return false; }
        }

        /// <summary>Vanilla checks only tile ids 21/467 before allowing a filled chest break.</summary>
        private static bool BeforeCanKillTile(
            int i,
            int j,
            ref bool blockDamaged,
            ref bool __result)
        {
            if (i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY)
                return true;
            var tile = Main.tile[i, j];
            if (tile == null || !tile.active()
                || !(_content?.GetTile(tile.type) is TimfContainerTile))
                return true;

            var left = i - tile.frameX / 18 % 2;
            var top = j - tile.frameY / 18;
            blockDamaged = false;
            __result = Chest.CanDestroyChest(left, top);
            return false;
        }

        /// <summary>
        /// CheckChest is generic through BasicChest, but its drop lookup is hard-coded to the
        /// two vanilla chest tile ids. Supply the custom container's declared item instead.
        /// </summary>
        private static bool BeforeGetChestDrop(int type, ref int __result)
        {
            var definition = _content?.GetTile(type) as TimfContainerTile;
            if (definition == null)
                return true;
            __result = definition.ItemDrop;
            return false;
        }

        private static void AfterGetTileLight(int x, int y, ref Vector3 outputColor)
        {
            try
            {
                var tile = Main.tile[x, y];
                if (tile == null || !tile.active()) return;
                var def = _content?.GetTile(tile.type);
                if (def == null) return;
                var r = 0f; var g = 0f; var b = 0f;
                def.ModifyLight(x, y, ref r, ref g, ref b);
                outputColor.X = Math.Max(outputColor.X, r);
                outputColor.Y = Math.Max(outputColor.Y, g);
                outputColor.Z = Math.Max(outputColor.Z, b);
            }
            catch (Exception ex)
            {
                _log?.Error("Content: custom tile ModifyLight failed", ex);
            }
        }

        private static bool HasPlacementSupport(int x, int y)
        {
            if (Main.tile[x, y].wall > 0)
                return true;
            return IsSupport(Main.tile[x - 1, y])
                   || IsSupport(Main.tile[x + 1, y])
                   || IsSupport(Main.tile[x, y - 1])
                   || IsSupport(Main.tile[x, y + 1]);
        }

        private static bool IsSupport(Tile tile)
        {
            if (tile == null)
                return false;
            if (tile.wall > 0)
                return true;
            if (!tile.active())
                return false;

            var type = (int)tile.type;
            return Main.tileSolid[type]
                   || TileID.Sets.IsBeam[type]
                   || Main.tileRope[type]
                   || type == TileID.MinecartTrack;
        }
    }
}
