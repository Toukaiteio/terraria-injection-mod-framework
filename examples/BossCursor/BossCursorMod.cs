using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace BossCursor
{
    /// <summary>
    /// Independent sample mod: arrows around the local player pointing at active bosses.
    /// Behavior inspired by the classic Boss Cursor client mod (not a source port).
    /// </summary>
    [TimfMod(Id = "BossCursor", Side = TimfSide.Client)]
    public sealed class BossCursorMod : IMod, IModSettings
    {
        // NPCID.LunarTower* (1.4.5.6)
        private static readonly int[] PillarTypes = { 422, 493, 507, 517 };
        private const string ToggleId = "BossCursor.Toggle";

        private IModContext _ctx;
        private BossCursorConfig _config;
        private Texture2D _cursorTex;
        private bool _enabled = true;
        private IKeybind _toggle;
        private IKeybindService _keybinds;
        private bool _textureLoadAttempted;
        private bool _announcePending;

        public string Name => "BossCursor";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "BossCursor.json");
            _config = BossCursorConfig.LoadOrCreate(cfgPath);
            _enabled = _config.Enabled;

            var defaultKey = ParseKey(_config.ToggleKey, Keys.Insert);
            if (context.Services.TryGetService(out _keybinds) && _keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "Boss Cursor Toggle"), defaultKey);
            else
                context.Log.Error("IKeybindService unavailable — BossCursor toggle will not work");

            context.Log.Info("BossCursor loaded. Toggle=" + ToggleId + " default=" + defaultKey + " config=" + cfgPath);
            _announcePending = true;
        }

        public void Unload()
        {
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _keybinds = null;
            _toggle = null;
            try
            {
                if (_cursorTex != null && !_cursorTex.IsDisposed)
                    _cursorTex.Dispose();
            }
            catch { /* ignore */ }
            _cursorTex = null;
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

            dirty |= ui.SliderFloat(L.Get("Settings.CursorSize", "Cursor size"), ref _config.CursorSize, 0.2f, 3f);
            dirty |= ui.SliderFloat(L.Get("Settings.RingDistance", "Ring distance"), ref _config.CursorDistance, 16f, 400f);
            dirty |= ui.Checkbox(L.Get("Settings.HideOnScreen", "Hide when on screen"), ref _config.HideOnScreen);
            dirty |= ui.Checkbox(L.Get("Settings.SkipPillars", "Skip pillars"), ref _config.BlackListPillars);

            ui.Spacing();
            var bind = _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay)
                ? _toggle.CurrentBindingDisplay
                : L.Get("Settings.Unbound", "(unbound)");
            ui.Text(L.Format("Settings.Toggle", bind));

            if (dirty)
                SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "BossCursor.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("BossCursor save config failed", ex);
            }
        }

        public void PostDraw(GameTime gameTime)
        {
            if (_ctx == null)
                return;

            try
            {
                HandleToggle();

                if (_announcePending && !Main.gameMenu && !Main.dedServ)
                {
                    _announcePending = false;
                    try
                    {
                        var bind = _toggle != null ? _toggle.CurrentBindingDisplay : "?";
                        Main.NewText(_ctx.L.Format("Chat.Ready", bind, _enabled ? "ON" : "OFF"), 100, 200, 255);
                    }
                    catch (Exception ex)
                    {
                        _ctx.Log.Error("Load announce NewText failed", ex);
                    }
                }

                if (!_enabled)
                    return;
                if (Main.gameMenu || Main.dedServ || Main.mapFullscreen)
                    return;
                if (Main.spriteBatch == null || Main.graphics == null)
                    return;

                EnsureTexture();
                if (_cursorTex == null)
                    return;

                var player = Main.LocalPlayer;
                if (player == null || !player.active)
                    return;

                var playerCenter = player.Center;
                var sb = Main.spriteBatch;

                // True screen-pixel HUD overlay (Identity). Player anchor goes through ZoomMatrix
                // so the ring stays locked to the rendered player when +/- zoom is active.
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
                    var npcs = Main.npc;
                    if (npcs == null)
                        return;

                    for (var i = 0; i < npcs.Length; i++)
                    {
                        var npc = npcs[i];
                        if (npc == null || !npc.active || !npc.boss)
                            continue;
                        if (_config.BlackListPillars && IsPillar(npc.type))
                            continue;
                        if (npc.realLife >= 0 && npc.realLife != npc.whoAmI)
                            continue; // multi-segment: only head / real life owner

                        DrawCursorFor(sb, playerCenter, npc);
                    }
                }
                finally
                {
                    sb.End();
                }
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("BossCursor PostDraw error", ex);
            }
        }

        private void DrawCursorFor(SpriteBatch sb, Vector2 playerCenter, NPC npc)
        {
            var bossCenter = npc.Center;
            var delta = bossCenter - playerCenter;
            var dist = delta.Length();
            if (dist < 1f)
                return;

            if (_config.HideOnScreen && IsOnScreen(npc))
                return;

            var dir = delta / dist;
            var angle = (float)Math.Atan2(dir.Y, dir.X);

            // Player's on-screen pixel pos (ZoomMatrix). Ring radius stays in screen pixels.
            var playerScreen = WorldToScreenPixels(playerCenter);
            var ring = Math.Max(16f, _config.CursorDistance);
            var drawPos = playerScreen + dir * ring;

            // Closer → larger & more opaque (readable distance cue).
            const float near = 400f;
            const float far = 4000f;
            var t = MathHelper.Clamp((dist - near) / (far - near), 0f, 1f);
            var scale = MathHelper.Lerp(1.35f, 0.55f, t) * Math.Max(0.1f, _config.CursorSize);
            var alpha = MathHelper.Lerp(1f, 0.35f, t);

            var origin = new Vector2(_cursorTex.Width / 2f, _cursorTex.Height / 2f);
            // Texture is assumed to point right (0 rad). Adjust if art points up: subtract Pi/2.
            var rotation = angle;
            var color = Color.White * alpha;

            sb.Draw(
                _cursorTex,
                drawPos,
                null,
                color,
                rotation,
                origin,
                scale,
                SpriteEffects.None,
                0f);
        }

        /// <summary>
        /// World → final screen pixels via <see cref="Main.GameViewMatrix.ZoomMatrix"/>
        /// (same transform as combat text / entity overlays when +/- zoom is active).
        /// </summary>
        private static Vector2 WorldToScreenPixels(Vector2 world)
        {
            var camera = world - Main.screenPosition;
            try
            {
                if (Main.GameViewMatrix != null)
                    return Vector2.Transform(camera, Main.GameViewMatrix.ZoomMatrix);
            }
            catch { /* fall through */ }

            return camera;
        }

        private static bool IsOnScreen(NPC npc)
        {
            // After zoom-in, less world is visible — test transformed screen pixels, not raw frustum.
            var pad = 32f;
            var s = WorldToScreenPixels(npc.Center);
            return s.X >= -pad
                && s.Y >= -pad
                && s.X <= Main.screenWidth + pad
                && s.Y <= Main.screenHeight + pad;
        }

        private static bool IsPillar(int type)
        {
            for (var i = 0; i < PillarTypes.Length; i++)
            {
                if (PillarTypes[i] == type)
                    return true;
            }
            return false;
        }

        private void HandleToggle()
        {
            if (_toggle == null || !_toggle.JustPressed)
                return;
            if (!IsGameFocused())
                return;
            // Don't toggle while typing in TIMF text fields.
            try
            {
                IImmediateModeUi ui;
                if (_ctx != null && _ctx.Services.TryGetService(out ui) && ui != null && ui.WantCaptureKeyboard)
                    return;
            }
            catch { /* ignore */ }

            _enabled = !_enabled;
            _config.Enabled = _enabled;
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "BossCursor.json"));
            }
            catch { /* ignore */ }

            var msg = _enabled ? _ctx.L.Get("Chat.On", "BossCursor: ON") : _ctx.L.Get("Chat.Off", "BossCursor: OFF");
            _ctx.Log.Info(msg);
            try { Main.NewText(msg, 100, 200, 255); }
            catch (Exception ex) { _ctx.Log.Error("Main.NewText failed", ex); }
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

        private void EnsureTexture()
        {
            if (_cursorTex != null || _textureLoadAttempted)
                return;
            _textureLoadAttempted = true;

            try
            {
                var candidates = new[]
                {
                    Path.Combine(_ctx.ContentDirectory, "Cursor.png"),
                    Path.Combine(_ctx.ModDirectory, "Cursor.png"),
                };

                string found = null;
                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        found = c;
                        break;
                    }
                }

                if (found == null)
                {
                    _ctx.Log.Warn("Cursor.png not found next to mod; generating fallback triangle texture");
                    _cursorTex = CreateFallbackArrow(Main.instance.GraphicsDevice);
                    return;
                }

                using (var fs = File.OpenRead(found))
                {
                    _cursorTex = Texture2D.FromStream(Main.instance.GraphicsDevice, fs);
                }
                _ctx.Log.Info("Loaded cursor texture: " + found);
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("Failed to load cursor texture", ex);
                try
                {
                    _cursorTex = CreateFallbackArrow(Main.instance.GraphicsDevice);
                }
                catch (Exception ex2)
                {
                    _ctx.Log.Error("Fallback texture failed", ex2);
                }
            }
        }

        private static Texture2D CreateFallbackArrow(GraphicsDevice device)
        {
            // 16x16 simple arrow pointing right
            const int w = 16;
            const int h = 16;
            var tex = new Texture2D(device, w, h);
            var data = new Color[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var cx = x - 2;
                    var cy = y - h / 2;
                    // shaft
                    var shaft = x >= 1 && x <= 9 && Math.Abs(cy) <= 1;
                    // head
                    var head = x >= 8 && x <= 14 && Math.Abs(cy) <= (14 - x);
                    data[y * w + x] = (shaft || head) ? Color.White : Color.Transparent;
                }
            }
            tex.SetData(data);
            return tex;
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
