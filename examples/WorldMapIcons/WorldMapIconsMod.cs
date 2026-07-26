using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TIMF.Abstractions;

namespace WorldMapIcons
{
    /// <summary>
    /// Renders nearby NPCs / enemies, items and projectiles as their in-game sprites on the
    /// fullscreen map and minimap (like MooMoo's MegaMap / Xaero's Minimap). Multi-segment
    /// worms are supported automatically — each active segment is its own NPC and draws.
    ///
    /// Draws via the framework map-overlay hook (Harmony postfix on MapIconOverlay.Draw), so
    /// icons share the exact transform / SpriteBatch the game uses for its own map icons.
    /// </summary>
    [TimfMod(Id = "WorldMapIcons", Side = TimfSide.Client)]
    public sealed class WorldMapIconsMod : IClientMod, IModSettings, IMapOverlayHook
    {
        private IModContext _ctx;
        private WorldMapIconsConfig _config;
        private GameTextures _tex;
        private IMapOverlayHookRegistry _mapHooks;

        public string Name => "World Map Icons";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = System.IO.Path.Combine(context.ConfigDirectory, "WorldMapIcons.json");
            _config = WorldMapIconsConfig.LoadOrCreate(cfgPath);
            _tex = new GameTextures(context.Log);

            if (context.Client != null && context.Client.MapOverlay != null)
            {
                _mapHooks = context.Client.MapOverlay;
                _mapHooks.Add(this);
            }
            else
                context.Log.Error("IClientServices.MapOverlay unavailable — map icons will not draw");

            context.Log.Info("WorldMapIcons loaded.");
        }

        public void Unload()
        {
            try { _mapHooks?.Remove(this); }
            catch { /* ignore */ }
            _mapHooks = null;
            _tex = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            // All drawing happens in OnDrawMap; nothing to do per-frame here.
        }

        // Called from the vanilla map draw pass, inside the open SpriteBatch.
        public void OnDrawMap(MapOverlayInfo info, ref string hoverText)
        {
            if (_ctx == null || _config == null || !_config.Enabled)
                return;
            if (_tex == null || !_tex.Ready)
                return;

            var player = Main.LocalPlayer;
            if (player == null || !player.active)
                return;

            var sb = Main.spriteBatch;
            if (sb == null)
                return;

            var alpha = MathHelper.Clamp(info.Alpha, 0f, 1f);

            if (_config.DrawNPCs)
                DrawNpcs(sb, info, player, alpha, ref hoverText);
            if (_config.DrawItems)
                DrawItems(sb, info, player, alpha, ref hoverText);
            if (_config.DrawProjectiles)
                DrawProjectiles(sb, info, player, alpha, ref hoverText);
        }

        private void DrawNpcs(SpriteBatch sb, MapOverlayInfo info, Player player, float alpha, ref string hoverText)
        {
            var npcs = Main.npc;
            if (npcs == null)
                return;
            var maxN = Math.Min(npcs.Length, Main.maxNPCs > 0 ? Main.maxNPCs : npcs.Length);

            for (var i = 0; i < maxN; i++)
            {
                var npc = npcs[i];
                if (npc == null || !npc.active)
                    continue;
                // Skip town NPCs and bosses (bosses have their own vanilla marker); worms are kept.
                if (npc.townNPC || npc.boss)
                    continue;
                if (!DistanceOk(player, npc.position, _config.DrawDistance))
                    continue;
                if (!Explored(npc.Center, _config.DrawNPCsIfNotExplored))
                    continue;

                var tex = _tex.NpcTexture(npc.type);
                if (tex == null)
                    continue;

                var frame = npc.frame;
                if (frame.Width <= 0 || frame.Height <= 0)
                {
                    frame = new Rectangle(0, 0, tex.Width, tex.Height);
                }
                if (frame.Width > _config.NpcCullWidth || frame.Height > _config.NpcCullHeight)
                    continue;

                var mapPos = info.WorldToMap(npc.Center);
                if (!info.Contains(mapPos))
                    continue;

                var scale = info.DrawScale * Math.Max(0.05f, _config.NpcScale);
                var origin = new Vector2(frame.Width / 2f, frame.Height / 2f);

                var color = PickColor(npc.color) * alpha;
                var hovering = HoverTest(mapPos, frame, scale);
                if (hovering)
                {
                    scale += 0.1f;
                    hoverText = SafeName(npc.FullName, "NPC");
                }

                var effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                sb.Draw(tex, mapPos, frame, color, npc.rotation, origin, scale, effects, 0f);
            }
        }

        private void DrawItems(SpriteBatch sb, MapOverlayInfo info, Player player, float alpha, ref string hoverText)
        {
            var items = Main.item;
            if (items == null)
                return;
            var maxI = Math.Min(items.Length, Main.maxItems > 0 ? Main.maxItems : items.Length);

            for (var i = 0; i < maxI; i++)
            {
                var item = items[i];
                if (item == null || !item.active || item.type <= 0)
                    continue;
                if (!DistanceOk(player, item.position, _config.DrawDistance))
                    continue;
                if (!Explored(item.Center, _config.DrawItemsIfNotExplored))
                    continue;

                Texture2D tex;
                Rectangle frame;
                if (!_tex.GetItemDrawFrame(item.type, out tex, out frame) || tex == null)
                    continue;
                if (frame.Width <= 0 || frame.Height <= 0)
                    frame = new Rectangle(0, 0, tex.Width, tex.Height);
                if (frame.Width > 75 || frame.Height > 75)
                    continue;

                var mapPos = info.WorldToMap(item.Center);
                if (!info.Contains(mapPos))
                    continue;

                var baseScale = ItemBaseScale(item.width, item.height);
                var scale = baseScale * info.DrawScale * Math.Max(0.05f, _config.ItemScale);
                var origin = new Vector2(frame.Width / 2f, frame.Height / 2f);

                var color = PickColor(item.color) * alpha;
                var hovering = HoverTest(mapPos, frame, scale);
                if (hovering)
                {
                    scale += 0.1f;
                    hoverText = SafeName(item.Name, "Item");
                }

                // Items have no meaningful facing on the map.
                sb.Draw(tex, mapPos, frame, color, 0f, origin, scale, SpriteEffects.None, 0f);
            }
        }

        private void DrawProjectiles(SpriteBatch sb, MapOverlayInfo info, Player player, float alpha, ref string hoverText)
        {
            var projs = Main.projectile;
            if (projs == null)
                return;
            var maxP = Math.Min(projs.Length, Main.maxProjectiles > 0 ? Main.maxProjectiles : projs.Length);

            for (var i = 0; i < maxP; i++)
            {
                var proj = projs[i];
                if (proj == null || !proj.active || proj.type <= 0)
                    continue;
                if (!DistanceOk(player, proj.position, _config.DrawDistance))
                    continue;
                if (!Explored(proj.Center, _config.DrawProjectilesIfNotExplored))
                    continue;

                var tex = _tex.ProjectileTexture(proj.type);
                if (tex == null)
                    continue;

                var frameCount = _tex.ProjectileFrameCount(proj.type);
                var frameH = frameCount > 0 ? tex.Height / frameCount : tex.Height;
                var frame = new Rectangle(0, frameH * Math.Max(0, proj.frame), tex.Width, frameH);
                if (frame.Width > 50 || frame.Height > 50)
                    continue;

                var mapPos = info.WorldToMap(proj.Center);
                if (!info.Contains(mapPos))
                    continue;

                var scale = info.DrawScale * Math.Max(0.05f, _config.ProjectileScale);
                var origin = new Vector2(frame.Width / 2f, frame.Height / 2f);

                var color = Color.White * alpha;
                var hovering = HoverTest(mapPos, frame, scale);
                if (hovering)
                {
                    scale += 0.1f;
                    hoverText = SafeName(proj.Name, "Projectile");
                }

                var effects = proj.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                sb.Draw(tex, mapPos, frame, color, proj.rotation, origin, scale, effects, 0f);
            }
        }

        private static bool HoverTest(Vector2 mapPos, Rectangle frame, float scale)
        {
            var w = frame.Width * scale;
            var h = frame.Height * scale;
            var rect = new Rectangle((int)(mapPos.X - w / 2f), (int)(mapPos.Y - h / 2f), (int)w, (int)h);
            var m = Main.MouseScreen;
            return rect.Contains((int)m.X, (int)m.Y);
        }

        private static Color PickColor(Color entityColor)
        {
            // Entities with no override color come through as (0,0,0,0) → draw white.
            if (entityColor.R == 0 && entityColor.G == 0 && entityColor.B == 0 && entityColor.A == 0)
                return Color.White;
            return entityColor;
        }

        private static float ItemBaseScale(int w, int h)
        {
            if (w >= 40 || h >= 40)
                return (w >= 60 || h >= 60) ? 0.4f : 0.5f;
            return 0.6f;
        }

        private bool DistanceOk(Player player, Vector2 worldPos, float maxTiles)
        {
            if (maxTiles < 0)
                return true;
            return Vector2.Distance(player.position, worldPos) / 16f < maxTiles;
        }

        private bool Explored(Vector2 worldCenter, bool drawIfNotExplored)
        {
            if (drawIfNotExplored)
                return true;
            var tx = (int)(worldCenter.X / 16f);
            var ty = (int)(worldCenter.Y / 16f);
            return _tex.IsRevealed(tx, ty);
        }

        private static string SafeName(string name, string fallback)
        {
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;
            var L = _ctx.L;

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);

            ui.Text(L.Get("Settings.WhatToDraw", "What to draw:"));
            dirty |= ui.Checkbox(L.Get("Settings.DrawNpcs", "NPCs / enemies"), ref _config.DrawNPCs);
            dirty |= ui.Checkbox(L.Get("Settings.DrawItems", "Items"), ref _config.DrawItems);
            dirty |= ui.Checkbox(L.Get("Settings.DrawProjectiles", "Projectiles"), ref _config.DrawProjectiles);

            ui.Text(L.Get("Settings.Unexplored", "Draw in unexplored areas:"));
            dirty |= ui.Checkbox(L.Get("Settings.NpcsUnexplored", "NPCs (unexplored)"), ref _config.DrawNPCsIfNotExplored);
            dirty |= ui.Checkbox(L.Get("Settings.ItemsUnexplored", "Items (unexplored)"), ref _config.DrawItemsIfNotExplored);
            dirty |= ui.Checkbox(L.Get("Settings.ProjectilesUnexplored", "Projectiles (unexplored)"), ref _config.DrawProjectilesIfNotExplored);

            dirty |= ui.SliderFloat(L.Get("Settings.NpcScale", "NPC scale"), ref _config.NpcScale, 0.2f, 1.5f);
            dirty |= ui.SliderFloat(L.Get("Settings.ItemScale", "Item scale"), ref _config.ItemScale, 0.4f, 1.5f);
            dirty |= ui.SliderFloat(L.Get("Settings.ProjectileScale", "Projectile scale"), ref _config.ProjectileScale, 0.1f, 1.5f);
            dirty |= ui.SliderFloat(L.Get("Settings.DrawDistance", "Draw distance (tiles, -1=∞)"), ref _config.DrawDistance, -1f, 500f);

            if (dirty)
                SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(System.IO.Path.Combine(_ctx.ConfigDirectory, "WorldMapIcons.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("WorldMapIcons save config failed", ex);
            }
        }
    }
}
