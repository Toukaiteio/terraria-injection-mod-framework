using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace BlockLocator
{
    /// <summary>
    /// Quality-of-life: finds the nearest tile of a configured type around the player and
    /// draws a BossCursor-style arrow pointing at it. Tile scan is bounded to a radius around
    /// the player and throttled. Toggle with the ] key.
    /// </summary>
    [TimfMod(Id = "BlockLocator", Side = TimfSide.Client)]
    public sealed class BlockLocatorMod : IMod, IModSettings
    {
        private IModContext _ctx;
        private BlockLocatorConfig _config;
        private Texture2D _arrowTex;
        private bool _texAttempted;
        private const string ToggleId = "BlockLocator.Toggle";
        private IKeybind _toggle;
        private IKeybindService _keybinds;
        private bool _announcePending = true;

        private int _frameCounter;
        private bool _hasTarget;
        private Vector2 _targetWorld; // center of located tile, in world px
        private float _targetDist;

        // Scratch buffer for the type-picker text field in settings.
        private string _typesText = "";
        private bool _typesTextInit;

        public string Name => "Block Locator";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "BlockLocator.json");
            _config = BlockLocatorConfig.LoadOrCreate(cfgPath);
            var defaultKey = ParseKey(_config.ToggleKey, Keys.OemCloseBrackets);
            if (context.Services.TryGetService(out _keybinds) && _keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "Block Locator Toggle"), defaultKey);
            else
                context.Log.Error("IKeybindService unavailable — BlockLocator toggle will not work");
            context.Log.Info("BlockLocator loaded. Toggle=" + ToggleId + " default=" + defaultKey +
                             " types=[" + string.Join(",", _config.TargetTileTypes) + "]");
        }

        public void Unload()
        {
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _keybinds = null;
            _toggle = null;
            try
            {
                if (_arrowTex != null && !_arrowTex.IsDisposed)
                    _arrowTex.Dispose();
            }
            catch { /* ignore */ }
            _arrowTex = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            if (_ctx == null)
                return;

            try
            {
                HandleToggle();
                MaybeAnnounce();

                if (!_config.Enabled || Main.gameMenu || Main.dedServ || Main.mapFullscreen)
                    return;

                var player = Main.LocalPlayer;
                if (player == null || !player.active)
                    return;
                if (Main.spriteBatch == null)
                    return;

                // Throttled rescan.
                _frameCounter++;
                if (!_hasTarget || _frameCounter >= Math.Max(1, _config.RescanEveryFrames))
                {
                    _frameCounter = 0;
                    Rescan(player);
                }

                if (!_hasTarget)
                    return;

                EnsureTexture();
                if (_arrowTex == null)
                    return;

                if (_config.HideWhenOnScreen && IsOnScreen(_targetWorld))
                    return;

                DrawArrow(Main.spriteBatch, player.Center, _targetWorld, _targetDist);
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("BlockLocator PostDraw error", ex);
            }
        }

        private void Rescan(Player player)
        {
            _hasTarget = false;
            var types = _config.TargetTileTypes;
            if (types == null || types.Count == 0)
                return;

            var tiles = Main.tile;
            if (tiles == null)
                return;

            // Player tile position.
            var pcx = (int)(player.Center.X / 16f);
            var pcy = (int)(player.Center.Y / 16f);
            var r = Math.Max(4, _config.SearchRadiusTiles);

            var minX = Math.Max(1, pcx - r);
            var maxX = Math.Min(Main.maxTilesX - 2, pcx + r);
            var minY = Math.Max(1, pcy - r);
            var maxY = Math.Min(Main.maxTilesY - 2, pcy + r);

            var bestDistSq = float.MaxValue;
            var bestX = -1;
            var bestY = -1;
            var center = player.Center;

            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    Tile tile;
                    try
                    {
                        tile = tiles[x, y];
                    }
                    catch
                    {
                        continue;
                    }

                    if (tile == null || !tile.active())
                        continue;

                    var type = (int)tile.type;
                    if (!TypeMatches(types, type))
                        continue;

                    var wx = x * 16f + 8f;
                    var wy = y * 16f + 8f;
                    var dx = wx - center.X;
                    var dy = wy - center.Y;
                    var distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            if (bestX >= 0)
            {
                _hasTarget = true;
                _targetWorld = new Vector2(bestX * 16f + 8f, bestY * 16f + 8f);
                _targetDist = (float)Math.Sqrt(bestDistSq);
            }
        }

        private static bool TypeMatches(List<int> types, int type)
        {
            for (var i = 0; i < types.Count; i++)
            {
                if (types[i] == type)
                    return true;
            }
            return false;
        }

        private void DrawArrow(SpriteBatch sb, Vector2 playerCenter, Vector2 targetWorld, float dist)
        {
            var delta = targetWorld - playerCenter;
            var len = delta.Length();
            if (len < 1f)
                return;
            var dir = delta / len;
            var angle = (float)Math.Atan2(dir.Y, dir.X);

            // Player's on-screen pixel pos (ZoomMatrix). Ring radius stays in screen pixels.
            var playerScreen = WorldToScreenPixels(playerCenter);
            var ring = Math.Max(16f, _config.ArrowDistance);
            var drawPos = playerScreen + dir * ring;

            var scale = Math.Max(0.1f, _config.ArrowSize);
            var origin = new Vector2(_arrowTex.Width / 2f, _arrowTex.Height / 2f);

            // True screen-pixel HUD overlay (Identity); anchor already transformed.
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
                // Green tint to distinguish from BossCursor's white arrow.
                var color = new Color(120, 230, 140);
                sb.Draw(_arrowTex, drawPos, null, color, angle, origin, scale, SpriteEffects.None, 0f);
            }
            finally
            {
                sb.End();
            }
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

        private static bool IsOnScreen(Vector2 world)
        {
            // After zoom-in, less world is visible — test transformed screen pixels.
            var pad = 24f;
            var s = WorldToScreenPixels(world);
            return s.X >= -pad
                && s.Y >= -pad
                && s.X <= Main.screenWidth + pad
                && s.Y <= Main.screenHeight + pad;
        }

        private void EnsureTexture()
        {
            if (_arrowTex != null || _texAttempted)
                return;
            _texAttempted = true;

            try
            {
                var device = Main.instance != null ? Main.instance.GraphicsDevice : null;
                if (device == null && Main.graphics != null)
                    device = Main.graphics.GraphicsDevice;
                if (device == null)
                    return;

                _arrowTex = CreateArrow(device);
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("BlockLocator arrow texture failed", ex);
            }
        }

        private static Texture2D CreateArrow(GraphicsDevice device)
        {
            const int w = 16;
            const int h = 16;
            var tex = new Texture2D(device, w, h);
            var data = new Color[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var cy = y - h / 2;
                    var shaft = x >= 1 && x <= 9 && Math.Abs(cy) <= 1;
                    var head = x >= 8 && x <= 14 && Math.Abs(cy) <= (14 - x);
                    data[y * w + x] = (shaft || head) ? Color.White : Color.Transparent;
                }
            }
            tex.SetData(data);
            return tex;
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;
            var L = _ctx.L;
            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);

            if (!_typesTextInit)
            {
                _typesText = string.Join(",", _config.TargetTileTypes);
                _typesTextInit = true;
            }

            ui.Text(L.Get("Settings.TileIds", "Target tile ids (comma separated):"));
            if (ui.InputText(L.Get("Settings.TileIdsLabel", "Tile ids"), ref _typesText, 96))
            {
                ParseTypesText();
                dirty = true;
            }

            var radius = (float)_config.SearchRadiusTiles;
            if (ui.SliderFloat(L.Get("Settings.SearchRadius", "Search radius (tiles)"), ref radius, 20f, 300f))
            {
                _config.SearchRadiusTiles = (int)radius;
                dirty = true;
            }

            dirty |= ui.SliderFloat(L.Get("Settings.ArrowDistance", "Arrow ring distance"), ref _config.ArrowDistance, 24f, 240f);
            dirty |= ui.SliderFloat(L.Get("Settings.ArrowSize", "Arrow size"), ref _config.ArrowSize, 0.4f, 3f);
            dirty |= ui.Checkbox(L.Get("Settings.HideOnScreen", "Hide when on screen"), ref _config.HideWhenOnScreen);

            ui.Spacing();
            var bind = _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay)
                ? _toggle.CurrentBindingDisplay
                : L.Get("Settings.Unbound", "(unbound)");
            ui.Text(L.Format("Settings.Toggle", bind));
            ui.TextColored(
                _hasTarget
                    ? L.Format("Settings.Nearest", (int)_targetDist)
                    : L.Get("Settings.NoTarget", "No target in range"),
                _hasTarget ? new Color(120, 230, 140) : new Color(150, 150, 150));
            ui.Text(L.Get("Settings.CommonIds", "Common ids: 21=Chest, 7=Copper ore, 6=Iron, 8=Gold, 12=Heart Crystal"));

            if (dirty)
                SaveConfig();
        }

        private void ParseTypesText()
        {
            var list = new List<int>();
            foreach (var part in (_typesText ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(part.Trim(), out v))
                    list.Add(v);
            }
            _config.TargetTileTypes = list;
            _hasTarget = false; // force rescan with new types
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "BlockLocator.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("BlockLocator save config failed", ex);
            }
        }

        private void HandleToggle()
        {
            if (_toggle == null || !_toggle.JustPressed)
                return;
            if (!IsGameFocused())
                return;

            _config.Enabled = !_config.Enabled;
            SaveConfig();
            var msg = _config.Enabled ? _ctx.L.Get("Chat.On", "BlockLocator: ON") : _ctx.L.Get("Chat.Off", "BlockLocator: OFF");
            _ctx.Log.Info(msg);
            try { Main.NewText(msg, 120, 230, 140); }
            catch { /* ignore */ }
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
                        _toggle != null ? _toggle.CurrentBindingDisplay : "?"),
                    120, 230, 140);
            }
            catch { /* ignore */ }
        }

        private static Keys ParseKey(string name, Keys fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
                return fallback;
            Keys k;
            return Enum.TryParse(name.Trim(), true, out k) ? k : fallback;
        }
    }
}
