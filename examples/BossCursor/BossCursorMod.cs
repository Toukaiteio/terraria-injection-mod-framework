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
    [TimfMod]
    public sealed class BossCursorMod : IMod
    {
        // NPCID.LunarTower* (1.4.5.6)
        private static readonly int[] PillarTypes = { 422, 493, 507, 517 };

        private IModContext _ctx;
        private BossCursorConfig _config;
        private Texture2D _cursorTex;
        private bool _enabled = true;
        private Keys _toggleKey = Keys.Insert;
        private KeyboardState _prevKeyboard;
        private bool _textureLoadAttempted;

        public string Name => "BossCursor";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "BossCursor.json");
            _config = BossCursorConfig.LoadOrCreate(cfgPath);
            _enabled = _config.Enabled;
            _toggleKey = ParseKey(_config.ToggleKey, Keys.Insert);
            _prevKeyboard = Keyboard.GetState();
            context.Log.Info("BossCursor loaded. Toggle=" + _toggleKey + " config=" + cfgPath);

            // Deferred chat notice once we are in-world (Main may still be on menu at Load).
            _announcePending = true;
        }

        private bool _announcePending;

        public void Unload()
        {
            try
            {
                if (_cursorTex != null && !_cursorTex.IsDisposed)
                    _cursorTex.Dispose();
            }
            catch { /* ignore */ }
            _cursorTex = null;
            _ctx = null;
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
                        Main.NewText(
                            "BossCursor loaded. Press " + _toggleKey + " to toggle. Now: " +
                            (_enabled ? "ON" : "OFF"),
                            100, 200, 255);
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

                // UI / screen space overlay. OnPostDraw is after the game frame; begin a clean batch.
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

            // Screen-space player position (ignore world zoom for a stable HUD ring).
            var playerScreen = playerCenter - Main.screenPosition;
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

        private static bool IsOnScreen(NPC npc)
        {
            var pad = 32f;
            var left = Main.screenPosition.X - pad;
            var top = Main.screenPosition.Y - pad;
            var right = Main.screenPosition.X + Main.screenWidth + pad;
            var bottom = Main.screenPosition.Y + Main.screenHeight + pad;
            var c = npc.Center;
            return c.X >= left && c.X <= right && c.Y >= top && c.Y <= bottom;
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
            var state = Keyboard.GetState();
            if (state.IsKeyDown(_toggleKey) && _prevKeyboard.IsKeyUp(_toggleKey))
            {
                _enabled = !_enabled;
                _config.Enabled = _enabled;
                try
                {
                    _config.Save(Path.Combine(_ctx.ConfigDirectory, "BossCursor.json"));
                }
                catch { /* ignore */ }

                var msg = _enabled ? "BossCursor: ON" : "BossCursor: OFF";
                _ctx.Log.Info(msg);
                try
                {
                    // In-game chat / combat text line
                    Main.NewText(msg, 100, 200, 255);
                }
                catch (Exception ex)
                {
                    _ctx.Log.Error("Main.NewText failed", ex);
                }
            }
            _prevKeyboard = state;
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
                    Path.Combine(_ctx.ModDirectory, "Cursor.png"),
                    Path.Combine(_ctx.ModDirectory, "UI", "Cursor.png"),
                    Path.Combine(_ctx.HomeDirectory, "Mods", "Cursor.png"),
                    Path.Combine(_ctx.HomeDirectory, "content", "BossCursor", "Cursor.png"),
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
