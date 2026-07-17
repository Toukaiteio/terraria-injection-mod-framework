using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace HighLight
{
    /// <summary>
    /// Independent sample mod: red circles on hostile NPCs / projectiles,
    /// plus velocity-based direction lines on hostile projectiles.
    /// Behavior inspired by the tML HighLight client mod (not a source port).
    /// </summary>
    [TimfMod]
    public sealed class HighLightMod : IMod, IModSettings
    {
        private IModContext _ctx;
        private HighLightConfig _config;
        private Texture2D _pixel;
        private bool _enabled = true;
        private Keys _toggleKey = Keys.P;
        private KeyboardState _prevKeyboard;
        private bool _announcePending;
        private int _frameCounter;

        public string Name => "HighLight";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "HighLight.json");
            _config = HighLightConfig.LoadOrCreate(cfgPath);
            _enabled = _config.Enabled;
            _toggleKey = ParseKey(_config.ToggleKey, Keys.P);
            _prevKeyboard = Keyboard.GetState();
            _announcePending = true;
            context.Log.Info("HighLight loaded. Toggle=" + _toggleKey + " config=" + cfgPath);
        }

        public void Unload()
        {
            try
            {
                if (_pixel != null && !_pixel.IsDisposed)
                    _pixel.Dispose();
            }
            catch { /* ignore */ }

            _pixel = null;
            _ctx = null;
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;

            if (ui.Checkbox("Enabled", ref _config.Enabled))
            {
                _enabled = _config.Enabled;
                dirty = true;
            }

            dirty |= ui.SliderFloat("Opacity", ref _config.Opacity, 0f, 1f);
            dirty |= ui.SliderFloat("Circle scale", ref _config.CircleScale, 0.1f, 5f);
            dirty |= ui.SliderFloat("Line length x", ref _config.VelocityLineLengthMultiplier, 1f, 50f);
            dirty |= ui.SliderFloat("Line thickness x", ref _config.VelocityLineThicknessMultiplier, 0.1f, 5f);

            var maxThick = (float)_config.MaxVelocityLineThickness;
            if (ui.SliderFloat("Max thickness", ref maxThick, 1f, 20f))
            {
                _config.MaxVelocityLineThickness = (int)Math.Round(maxThick);
                dirty = true;
            }

            dirty |= ui.Checkbox("Line to screen edge", ref _config.UseMaxScreenLengthForLine);
            dirty |= ui.Checkbox("Fade line ends", ref _config.FadeLineEnds);

            ui.Spacing();
            ui.Text("Toggle key: " + _toggleKey);

            if (dirty)
                SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "HighLight.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("HighLight save config failed", ex);
            }
        }

        public void PostDraw(GameTime gameTime)
        {
            if (_ctx == null)
                return;

            try
            {
                HandleToggle();
                MaybeAnnounce();

                if (!_enabled || _config == null || !_config.Enabled)
                    return;
                if (Main.gameMenu || Main.dedServ || Main.mapFullscreen)
                    return;
                if (Main.spriteBatch == null || Main.graphics == null)
                    return;

                EnsurePixel();
                if (_pixel == null || _pixel.IsDisposed)
                    return;

                var interval = Math.Max(1, _config.DrawEveryNFrames);
                _frameCounter++;
                if (_frameCounter < interval)
                    return;
                _frameCounter = 0;

                var sb = Main.spriteBatch;
                sb.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    Matrix.Identity);

                try
                {
                    DrawOverlays(sb);
                }
                finally
                {
                    sb.End();
                }
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("HighLight PostDraw error", ex);
            }
        }

        private void DrawOverlays(SpriteBatch spriteBatch)
        {
            var baseColor = _config.CircleColor * MathHelper.Clamp(_config.Opacity, 0f, 1f);
            var scale = Math.Max(0.05f, _config.CircleScale);

            // Hostile projectiles: circle + velocity prediction line.
            var projectiles = Main.projectile;
            if (projectiles != null)
            {
                var maxP = Math.Min(projectiles.Length, Main.maxProjectiles > 0 ? Main.maxProjectiles : projectiles.Length);
                for (var i = 0; i < maxP; i++)
                {
                    var proj = projectiles[i];
                    if (proj == null || !proj.active || proj.friendly || proj.hide)
                        continue;

                    var center = proj.Center - Main.screenPosition;
                    if (!IsNearScreen(center, Math.Max(proj.width, proj.height)))
                        continue;

                    var radius = Math.Max(proj.width, proj.height) / 2f;
                    DrawCircle(spriteBatch, center, radius * scale, baseColor, 3);

                    var velocity = proj.velocity;
                    var speed = velocity.Length();
                    if (speed <= 0.1f)
                        continue;

                    var dir = velocity / speed;
                    float length;
                    if (_config.UseMaxScreenLengthForLine)
                    {
                        length = RayToScreenEdge(center, dir);
                        if (length <= 0f || float.IsInfinity(length) || float.IsNaN(length))
                            continue;
                    }
                    else
                    {
                        length = speed * _config.VelocityLineLengthMultiplier;
                    }

                    var thickness = (int)MathHelper.Clamp(
                        speed * _config.VelocityLineThicknessMultiplier,
                        1f,
                        Math.Max(1, _config.MaxVelocityLineThickness));
                    var end = center + dir * length;

                    if (_config.FadeLineEnds)
                        DrawLineFade(spriteBatch, center, end, baseColor, baseColor * 0f, thickness);
                    else
                        DrawLine(spriteBatch, center, end, baseColor, thickness);
                }
            }

            // Hostile NPCs: circle only.
            var npcs = Main.npc;
            if (npcs != null)
            {
                var maxN = Math.Min(npcs.Length, Main.maxNPCs > 0 ? Main.maxNPCs : npcs.Length);
                for (var i = 0; i < maxN; i++)
                {
                    var npc = npcs[i];
                    if (npc == null || !npc.active || npc.friendly || npc.hide)
                        continue;

                    var center = npc.Center - Main.screenPosition;
                    if (!IsNearScreen(center, Math.Max(npc.width, npc.height)))
                        continue;

                    var radius = Math.Max(npc.width, npc.height) / 2f;
                    DrawCircle(spriteBatch, center, radius * scale, baseColor, 3);
                }
            }
        }

        /// <summary>Skip entities far off-screen to avoid useless draws.</summary>
        private static bool IsNearScreen(Vector2 screenPos, float size)
        {
            var pad = Math.Max(64f, size);
            return screenPos.X >= -pad
                && screenPos.Y >= -pad
                && screenPos.X <= Main.screenWidth + pad
                && screenPos.Y <= Main.screenHeight + pad;
        }

        private static float RayToScreenEdge(Vector2 origin, Vector2 dir)
        {
            // dir assumed unit length.
            var maxX = dir.X > 0f
                ? (Main.screenWidth - origin.X) / dir.X
                : (dir.X < 0f ? origin.X / -dir.X : float.PositiveInfinity);
            var maxY = dir.Y > 0f
                ? (Main.screenHeight - origin.Y) / dir.Y
                : (dir.Y < 0f ? origin.Y / -dir.Y : float.PositiveInfinity);
            return Math.Min(maxX, maxY);
        }

        private void DrawCircle(SpriteBatch spriteBatch, Vector2 center, float radius, Color color, int thickness)
        {
            if (radius < 0.5f)
                return;

            const int segments = 60;
            var increment = MathHelper.TwoPi / segments;
            var last = center + radius * new Vector2(1f, 0f);
            for (var i = 1; i <= segments; i++)
            {
                var angle = i * increment;
                var next = center + radius * new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                DrawLine(spriteBatch, last, next, color, thickness);
                last = next;
            }
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, int thickness)
        {
            if (_pixel == null || _pixel.IsDisposed)
                return;
            if (thickness < 1)
                thickness = 1;

            var edge = end - start;
            var length = edge.Length();
            if (length < 0.5f)
                return;

            var angle = (float)Math.Atan2(edge.Y, edge.X);
            spriteBatch.Draw(
                _pixel,
                new Rectangle((int)start.X, (int)start.Y, (int)length, thickness),
                null,
                color,
                angle,
                Vector2.Zero,
                SpriteEffects.None,
                0f);
        }

        private void DrawLineFade(
            SpriteBatch spriteBatch,
            Vector2 start,
            Vector2 end,
            Color startColor,
            Color endColor,
            int thickness)
        {
            var edge = end - start;
            var total = edge.Length();
            if (total < 0.5f)
                return;

            var dir = edge / total;
            const int numSegments = 10;
            var segmentLength = total / numSegments;
            for (var i = 0; i < numSegments; i++)
            {
                var tStart = (float)i / numSegments;
                var segmentStart = start + dir * (segmentLength * i);
                var segmentEnd = start + dir * (segmentLength * (i + 1));
                var lerped = Color.Lerp(startColor, endColor, tStart);
                DrawLine(spriteBatch, segmentStart, segmentEnd, lerped, thickness);
            }
        }

        private void EnsurePixel()
        {
            if (_pixel != null && !_pixel.IsDisposed)
                return;

            try
            {
                var device = Main.instance != null ? Main.instance.GraphicsDevice : null;
                if (device == null && Main.graphics != null)
                    device = Main.graphics.GraphicsDevice;
                if (device == null)
                    return;

                _pixel = new Texture2D(device, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("Failed to create 1x1 pixel texture", ex);
                _pixel = null;
            }
        }

        private void HandleToggle()
        {
            var state = Keyboard.GetState();
            if (state.IsKeyDown(_toggleKey) && _prevKeyboard.IsKeyUp(_toggleKey))
            {
                _enabled = !_enabled;
                _config.Enabled = _enabled;
                try
                {
                    _config.Save(Path.Combine(_ctx.ConfigDirectory, "HighLight.json"));
                }
                catch { /* ignore */ }

                var msg = _enabled ? "HighLight: ON" : "HighLight: OFF";
                _ctx.Log.Info(msg);
                try
                {
                    Main.NewText(msg, 255, 255, 0);
                }
                catch (Exception ex)
                {
                    _ctx.Log.Error("Main.NewText failed", ex);
                }
            }

            _prevKeyboard = state;
        }

        private void MaybeAnnounce()
        {
            if (!_announcePending || Main.gameMenu || Main.dedServ)
                return;

            _announcePending = false;
            try
            {
                Main.NewText(
                    "HighLight loaded. Press " + _toggleKey + " to toggle. Now: " +
                    (_enabled ? "ON" : "OFF"),
                    255, 200, 80);
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("Load announce NewText failed", ex);
            }
        }

        private static Keys ParseKey(string name, Keys fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
                return fallback;
            Keys k;
            if (Enum.TryParse(name.Trim(), true, out k))
                return k;
            return fallback;
        }
    }
}
