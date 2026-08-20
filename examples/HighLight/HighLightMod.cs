using System;
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
    [TimfMod(Id = "HighLight", Side = TimfSide.Client)]
    public sealed class HighLightMod : IClientMod, IModSettings, IModFeatureToggle
    {
        private IModContext _ctx;
        private HighLightConfig _config;
        private Texture2D _pixel;
        private bool _enabled = true;
        private IKeybind _toggle;
        private IKeybindService _keybinds;
        private const string ToggleId = "HighLight.Toggle";
        private bool _announcePending;
        private int _frameCounter;

        public string Name => "HighLight";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            _config = HighLightConfig.LoadOrCreate(context.Storage, "HighLight.json");
            _enabled = _config.Enabled;
            var defaultKey = ParseKey(_config.ToggleKey, Keys.P);
            _keybinds = context.Client != null ? context.Client.Keybinds : null;
            if (_keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "HighLight Toggle"), defaultKey);
            else
                context.Log.Error("IKeybindService unavailable — HighLight toggle will not work");
            _announcePending = true;
            context.Log.Info("HighLight loaded. Toggle=" + ToggleId + " default=" + defaultKey);
        }

        public void Unload()
        {
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _keybinds = null;
            _toggle = null;
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
            var L = _ctx.L;

            if (ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled))
            {
                _enabled = _config.Enabled;
                dirty = true;
            }

            dirty |= ui.SliderFloat(L.Get("Settings.Opacity", "Opacity"), ref _config.Opacity, 0f, 1f);

            ui.Separator();
            ui.Text(L.Get("Settings.MarkerStyle", "Marker style:"));
            dirty |= ui.Checkbox(L.Get("Settings.HitboxStyle", "Snap to hitbox (rectangle)"), ref _config.HitboxStyle);
            if (_config.HitboxStyle)
            {
                var ht = (float)_config.HitboxThickness;
                if (ui.SliderFloat(L.Get("Settings.OutlineThickness", "Outline thickness"), ref ht, 1f, 6f))
                {
                    _config.HitboxThickness = (int)Math.Round(ht);
                    dirty = true;
                }
                dirty |= ui.Checkbox(L.Get("Settings.FillHitbox", "Fill hitbox"), ref _config.FillHitbox);
                if (_config.FillHitbox)
                    dirty |= ui.SliderFloat(L.Get("Settings.FillOpacity", "Fill opacity"), ref _config.FillOpacity, 0f, 1f);
            }
            else
            {
                dirty |= ui.SliderFloat(L.Get("Settings.CircleScale", "Circle scale"), ref _config.CircleScale, 0.1f, 5f);
            }
            ui.Separator();

            ui.Text(L.Get("Settings.VelocityLine", "Projectile velocity line:"));
            dirty |= ui.SliderFloat(L.Get("Settings.LineLength", "Line length x"), ref _config.VelocityLineLengthMultiplier, 1f, 50f);
            dirty |= ui.SliderFloat(L.Get("Settings.LineThickness", "Line thickness x"), ref _config.VelocityLineThicknessMultiplier, 0.1f, 5f);

            var maxThick = (float)_config.MaxVelocityLineThickness;
            if (ui.SliderFloat(L.Get("Settings.MaxThickness", "Max thickness"), ref maxThick, 1f, 20f))
            {
                _config.MaxVelocityLineThickness = (int)Math.Round(maxThick);
                dirty = true;
            }

            dirty |= ui.Checkbox(L.Get("Settings.LineToEdge", "Line to screen edge"), ref _config.UseMaxScreenLengthForLine);
            dirty |= ui.Checkbox(L.Get("Settings.FadeEnds", "Fade line ends"), ref _config.FadeLineEnds);

            ui.Spacing();
            var bind = _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay)
                ? _toggle.CurrentBindingDisplay
                : L.Get("Settings.Unbound", "(unbound)");
            ui.Text(L.Format("Settings.Toggle", bind));

            if (dirty)
                SaveConfig();
        }

        /// <summary>In-world feature switch for hubs — mod enablement itself is menu-only.</summary>
        public bool FeatureEnabled
        {
            get { return _config != null && _config.Enabled; }
            set
            {
                if (_config == null || _config.Enabled == value)
                    return;
                _enabled = value;
                _config.Enabled = value;
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(_ctx.Storage, "HighLight.json");
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
                // World-space overlay: same transform as vanilla combat text / entity overlays.
                // +/- zoom writes GameZoomTarget → GameViewMatrix.Zoom; Identity would desync boxes.
                sb.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    GetWorldOverlayMatrix());

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

            // Gravity potion flips the world render (GameViewMatrix.Effects), so camera-relative
            // overlay points must be mirrored too, exactly like vanilla CombatText compensates.
            var gravityFlipped = IsGravityFlipped();

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

                    var center = ToCameraRelative(proj.Center, gravityFlipped);
                    if (!IsNearScreen(center, Math.Max(proj.width, proj.height)))
                        continue;

                    if (_config.HitboxStyle)
                        DrawHitbox(spriteBatch, proj.position, proj.width, proj.height, baseColor);
                    else
                        DrawCircle(spriteBatch, center, Math.Max(proj.width, proj.height) / 2f * scale, baseColor, 3);

                    var velocity = proj.velocity;
                    if (gravityFlipped)
                        velocity.Y = -velocity.Y;
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

            // Hostile NPCs: hitbox rectangle (default) or circle.
            var npcs = Main.npc;
            if (npcs != null)
            {
                var maxN = Math.Min(npcs.Length, Main.maxNPCs > 0 ? Main.maxNPCs : npcs.Length);
                for (var i = 0; i < maxN; i++)
                {
                    var npc = npcs[i];
                    if (npc == null || !npc.active || npc.friendly || npc.hide)
                        continue;

                    var center = ToCameraRelative(npc.Center, gravityFlipped);
                    if (!IsNearScreen(center, Math.Max(npc.width, npc.height)))
                        continue;

                    if (_config.HitboxStyle)
                        DrawHitbox(spriteBatch, npc.position, npc.width, npc.height, baseColor);
                    else
                        DrawCircle(spriteBatch, center, Math.Max(npc.width, npc.height) / 2f * scale, baseColor, 3);
                }
            }
        }

        /// <summary>
        /// World → camera-relative screen point, mirroring Y when the gravity potion flips the
        /// world render (GameViewMatrix.Effects = FlipVertically). Without this the overlay would
        /// stay anchored to the pre-flip Y like the tML HighLight bug.
        /// </summary>
        private static bool IsGravityFlipped()
        {
            try
            {
                return Main.player != null
                    && Main.myPlayer >= 0
                    && Main.myPlayer < Main.player.Length
                    && Main.player[Main.myPlayer] != null
                    && Main.player[Main.myPlayer].gravDir == -1f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Translate a world position into camera-relative screen coords with gravity flip applied.</summary>
        private static Vector2 ToCameraRelative(Vector2 worldPos, bool gravityFlipped)
        {
            var p = worldPos - Main.screenPosition;
            if (gravityFlipped)
                p.Y = Main.screenHeight - p.Y;
            return p;
        }

        /// <summary>Draw an outline (and optional fill) snapped to an entity's collision box.</summary>
        private void DrawHitbox(SpriteBatch sb, Vector2 worldPos, int width, int height, Color color)
        {
            if (_pixel == null || _pixel.IsDisposed)
                return;

            // Rectangle is offset from its center, so flip around the box's bottom edge:
            // screenY' = screenHeight - (worldY + height - screenPosition.Y).
            var gravityFlipped = IsGravityFlipped();
            var x = (int)(worldPos.X - Main.screenPosition.X);
            var y = (int)(worldPos.Y - Main.screenPosition.Y);
            if (gravityFlipped)
                y = (int)(Main.screenHeight - y - height);
            if (width <= 0 || height <= 0)
                return;

            var t = Math.Max(1, _config.HitboxThickness);

            if (_config.FillHitbox)
            {
                var fill = color * MathHelper.Clamp(_config.FillOpacity, 0f, 1f);
                sb.Draw(_pixel, new Rectangle(x, y, width, height), fill);
            }

            // Four edges as filled rectangles (crisp, no rotation).
            sb.Draw(_pixel, new Rectangle(x, y, width, t), color);                    // top
            sb.Draw(_pixel, new Rectangle(x, y + height - t, width, t), color);       // bottom
            sb.Draw(_pixel, new Rectangle(x, y, t, height), color);                   // left
            sb.Draw(_pixel, new Rectangle(x + width - t, y, t, height), color);       // right
        }

        /// <summary>
        /// Camera-relative world pixels → final screen via <see cref="Main.GameViewMatrix"/>.
        /// Matches vanilla world overlays (combat text uses ZoomMatrix + pos - screenPosition).
        /// </summary>
        private static Matrix GetWorldOverlayMatrix()
        {
            try
            {
                if (Main.GameViewMatrix != null)
                    return Main.GameViewMatrix.ZoomMatrix;
            }
            catch
            {
                // ignore — fall back to identity
            }

            return Matrix.Identity;
        }

        /// <summary>Current game zoom (1–2 from +/- keys, times ForcedMinimumZoom).</summary>
        private static float GetGameZoom()
        {
            try
            {
                if (Main.GameViewMatrix != null)
                {
                    var z = Main.GameViewMatrix.Zoom;
                    if (z.X > 0.01f)
                        return z.X;
                }
            }
            catch { /* ignore */ }

            try
            {
                return MathHelper.Clamp(Main.GameZoomTarget, 1f, 2f);
            }
            catch
            {
                return 1f;
            }
        }

        /// <summary>Skip entities far off-screen to avoid useless draws (accounts for zoom).</summary>
        private static bool IsNearScreen(Vector2 cameraRelative, float size)
        {
            // ZoomMatrix scales around screen center: when zoomed in, less world is visible.
            var zoom = Math.Max(0.01f, GetGameZoom());
            var halfW = Main.screenWidth * 0.5f;
            var halfH = Main.screenHeight * 0.5f;
            var viewHalfW = halfW / zoom;
            var viewHalfH = halfH / zoom;
            var pad = Math.Max(64f, size);
            var dx = cameraRelative.X - halfW;
            var dy = cameraRelative.Y - halfH;
            return dx >= -viewHalfW - pad
                && dy >= -viewHalfH - pad
                && dx <= viewHalfW + pad
                && dy <= viewHalfH + pad;
        }

        private static float RayToScreenEdge(Vector2 origin, Vector2 dir)
        {
            // dir assumed unit length. Bounds are in camera-relative (pre-ZoomMatrix) space —
            // same space as combat text positions. Visible edges shrink with zoom.
            var zoom = Math.Max(0.01f, GetGameZoom());
            var halfW = Main.screenWidth * 0.5f;
            var halfH = Main.screenHeight * 0.5f;
            var left = halfW - halfW / zoom;
            var right = halfW + halfW / zoom;
            var top = halfH - halfH / zoom;
            var bottom = halfH + halfH / zoom;

            var maxX = dir.X > 0f
                ? (right - origin.X) / dir.X
                : (dir.X < 0f ? (origin.X - left) / -dir.X : float.PositiveInfinity);
            var maxY = dir.Y > 0f
                ? (bottom - origin.Y) / dir.Y
                : (dir.Y < 0f ? (origin.Y - top) / -dir.Y : float.PositiveInfinity);
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
            if (_toggle == null || !_toggle.JustPressed)
                return;
            if (!IsGameFocused())
                return;

            _enabled = !_enabled;
            _config.Enabled = _enabled;
            try
            {
                _config.Save(_ctx.Storage, "HighLight.json");
            }
            catch { /* ignore */ }

            var msg = _enabled ? _ctx.L.Get("Chat.On", "HighLight: ON") : _ctx.L.Get("Chat.Off", "HighLight: OFF");
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

        private static bool IsGameFocused()
        {
            try
            {
                return Main.instance == null || Main.instance.IsActive;
            }
            catch
            {
                return true;
            }
        }

        private void MaybeAnnounce()
        {
            if (!_announcePending || Main.gameMenu || Main.dedServ)
                return;

            _announcePending = false;
            try
            {
                Main.NewText(
                    _ctx.L.Format("Chat.Ready",
                        _toggle != null ? _toggle.CurrentBindingDisplay : "?",
                        _enabled ? "ON" : "OFF"),
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
