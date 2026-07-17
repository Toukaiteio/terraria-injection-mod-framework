using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace LowHealthWarning
{
    /// <summary>
    /// Screen-edge red vignette when local player HP is low.
    /// Only paints a thin fading border — center FOV stays clear.
    /// </summary>
    [TimfMod]
    public sealed class LowHealthWarningMod : IMod, IModSettings
    {
        private IModContext _ctx;
        private LowHealthWarningConfig _config;
        private Texture2D _pixel;
        private bool _enabled = true;
        private Keys _toggleKey = Keys.Home;
        private KeyboardState _prevKeyboard;
        private bool _announcePending;
        private float _pulsePhase;

        public string Name => "LowHealthWarning";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "LowHealthWarning.json");
            _config = LowHealthWarningConfig.LoadOrCreate(cfgPath);
            _enabled = _config.Enabled;
            _toggleKey = ParseKey(_config.ToggleKey, Keys.Home);
            _prevKeyboard = Keyboard.GetState();
            _announcePending = true;
            context.Log.Info("LowHealthWarning loaded. Toggle=" + _toggleKey + " threshold=" +
                             _config.ThresholdRatio.ToString("0.##") + " config=" + cfgPath);
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

            dirty |= ui.SliderFloat("Warn below (HP%)", ref _config.ThresholdRatio, 0.05f, 1f);
            dirty |= ui.SliderFloat("Full strength (HP%)", ref _config.FullStrengthRatio, 0.02f, 0.5f);
            dirty |= ui.SliderFloat("Edge thickness", ref _config.MaxEdgeThickness, 16f, 160f);
            dirty |= ui.SliderFloat("Max opacity", ref _config.MaxOpacity, 0.05f, 0.75f);
            dirty |= ui.SliderFloat("Pulse speed", ref _config.PulseSpeed, 0f, 6f);
            dirty |= ui.SliderFloat("Pulse amount", ref _config.PulseAmount, 0f, 0.5f);

            ui.Spacing();
            ui.Text("Toggle key: " + _toggleKey);

            if (dirty)
                SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "LowHealthWarning.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("LowHealthWarning save config failed", ex);
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
                if (Main.spriteBatch == null)
                    return;

                var player = Main.LocalPlayer;
                if (player == null || !player.active || player.dead || player.ghost)
                    return;

                var maxLife = Math.Max(1, player.statLifeMax2 > 0 ? player.statLifeMax2 : player.statLifeMax);
                var life = Math.Max(0, player.statLife);
                var ratio = life / (float)maxLife;

                var threshold = MathHelper.Clamp(_config.ThresholdRatio, 0.02f, 1f);
                var full = MathHelper.Clamp(_config.FullStrengthRatio, 0.01f, threshold);
                if (ratio > threshold)
                    return;

                // 0 at threshold, 1 at full-strength floor.
                var danger = MathHelper.Clamp((threshold - ratio) / Math.Max(0.001f, threshold - full), 0f, 1f);
                // Ease-in so mild dips are subtle.
                danger = danger * danger;

                var dt = 1f / 60f;
                if (gameTime != null)
                    dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                _pulsePhase += dt * Math.Max(0f, _config.PulseSpeed);
                var pulse = 1f + MathHelper.Clamp(_config.PulseAmount, 0f, 0.5f) *
                            (float)Math.Sin(_pulsePhase * MathHelper.TwoPi) * danger;

                var strength = MathHelper.Clamp(danger * pulse, 0f, 1f);
                if (strength < 0.01f)
                    return;

                EnsurePixel();
                if (_pixel == null || _pixel.IsDisposed)
                    return;

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
                    DrawEdgeVignette(sb, strength);
                }
                finally
                {
                    sb.End();
                }
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("LowHealthWarning PostDraw error", ex);
            }
        }

        /// <summary>
        /// Draw four edge strips as nested bands: outer high alpha → inner transparent.
        /// Center of the screen is never covered.
        /// </summary>
        private void DrawEdgeVignette(SpriteBatch sb, float strength)
        {
            var w = Main.screenWidth;
            var h = Main.screenHeight;
            if (w < 32 || h < 32)
                return;

            // Cap thickness so middle ~70%+ of view stays free even on small resolutions.
            var maxByRes = Math.Min(w, h) * 0.18f;
            var thickness = MathHelper.Clamp(_config.MaxEdgeThickness, 8f, maxByRes) * MathHelper.Lerp(0.55f, 1f, strength);
            var maxA = MathHelper.Clamp(_config.MaxOpacity, 0.05f, 0.75f) * strength;
            var bands = Math.Max(2, _config.GradientBands);
            var bandH = thickness / bands;
            var bandW = thickness / bands;
            var tint = _config.TintColor;

            for (var i = 0; i < bands; i++)
            {
                // Outer band (i=0) strongest; innermost band nearly transparent.
                var t = i / (float)(bands - 1);
                // Smoothstep-ish falloff toward center.
                var falloff = (1f - t) * (1f - t);
                var a = maxA * falloff;
                if (a < 0.01f)
                    continue;

                var color = tint * a;
                var y0 = i * bandH;
                var y1 = h - (i + 1) * bandH;
                var x0 = i * bandW;
                var x1 = w - (i + 1) * bandW;

                // Top / bottom full-width strips for this band.
                FillRect(sb, new Rectangle(0, (int)y0, w, Math.Max(1, (int)Math.Ceiling(bandH))), color);
                FillRect(sb, new Rectangle(0, (int)y1, w, Math.Max(1, (int)Math.Ceiling(bandH))), color);

                // Left / right: only the middle vertical span so corners aren't double-darkened too hard.
                var midTop = (int)Math.Ceiling((i + 1) * bandH);
                var midBot = (int)Math.Floor(h - (i + 1) * bandH);
                var midH = midBot - midTop;
                if (midH > 0)
                {
                    FillRect(sb, new Rectangle((int)x0, midTop, Math.Max(1, (int)Math.Ceiling(bandW)), midH), color);
                    FillRect(sb, new Rectangle((int)x1, midTop, Math.Max(1, (int)Math.Ceiling(bandW)), midH), color);
                }
            }
        }

        private void FillRect(SpriteBatch sb, Rectangle rect, Color color)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;
            sb.Draw(_pixel, rect, color);
        }

        private void EnsurePixel()
        {
            if (_pixel != null && !_pixel.IsDisposed)
                return;

            try
            {
                GraphicsDevice device = null;
                if (Main.instance != null)
                    device = Main.instance.GraphicsDevice;
                if (device == null && Main.graphics != null)
                    device = Main.graphics.GraphicsDevice;
                if (device == null)
                    return;

                _pixel = new Texture2D(device, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("Failed to create pixel texture", ex);
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
                    _config.Save(Path.Combine(_ctx.ConfigDirectory, "LowHealthWarning.json"));
                }
                catch { /* ignore */ }

                var msg = _enabled ? "LowHealthWarning: ON" : "LowHealthWarning: OFF";
                _ctx.Log.Info(msg);
                try
                {
                    Main.NewText(msg, 255, 120, 120);
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
                    "LowHealthWarning loaded. Toggle " + _toggleKey +
                    " · warn below " + (int)(_config.ThresholdRatio * 100) + "% HP",
                    255, 140, 140);
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
