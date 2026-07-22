using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace AutoFishing
{
    /// <summary>
    /// Client-side auto-fishing (port of WzrterFX's Auto Fishing, no tModLoader / IL).
    ///
    /// Driven from a Player.Update prefix (framework IPlayerUpdateHook). While holding a fishing
    /// pole with auto enabled, it casts the line, waits for a bite, reels in, and recasts —
    /// reporting catches to chat. A "bite" is read directly from the vanilla FishingBobber
    /// projectile state; the reel-in / recast reuse the game's own ItemCheck path.
    ///
    /// Toggle with the \ key.
    /// </summary>
    [TimfMod(Id = "AutoFishing", Side = TimfSide.Client)]
    public sealed class AutoFishingMod : IMod, IModSettings, IPlayerUpdateHook
    {
        private IModContext _ctx;
        private AutoFishingConfig _config;
        private IPlayerUpdateHookRegistry _hookRegistry;
        private const string ToggleId = "AutoFishing.Toggle";
        private IKeybind _toggle;
        private IKeybindService _keybinds;
        private bool _announcePending = true;

        // Fishing state machine.
        private int _recastTimer;
        private bool _hadBobber;
        private int _lastBait;

        // Reflection for Player.ItemCheck() (invoked to reel / cast).
        private MethodInfo _itemCheck;
        private bool _itemCheckResolved;

        public string Name => "Auto Fishing";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "AutoFishing.json");
            _config = AutoFishingConfig.LoadOrCreate(cfgPath);
            var defaultKey = ParseKey(_config.ToggleKey, Keys.OemBackslash);

            if (context.Services.TryGetService(out _keybinds) && _keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "Auto Fishing Toggle"), defaultKey);
            else
                context.Log.Error("IKeybindService unavailable — AutoFishing toggle will not work");

            if (context.Services.TryGetService(out _hookRegistry) && _hookRegistry != null)
                _hookRegistry.Add(this);
            else
                context.Log.Error("IPlayerUpdateHookRegistry unavailable — auto fishing will not run");

            context.Log.Info("AutoFishing loaded. Toggle=" + ToggleId + " default=" + defaultKey + " enabled=" + _config.Enabled);
        }

        public void Unload()
        {
            try { _hookRegistry?.Remove(this); } catch { /* ignore */ }
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _hookRegistry = null;
            _keybinds = null;
            _toggle = null;
            _ctx = null;
        }

        // Runs inside Player.Update (local player), before item-use processing.
        public void OnPreUpdate()
        {
            if (_config == null || !_config.Enabled)
                return;

            try
            {
                if (Main.gameMenu || Main.dedServ)
                    return;

                var player = Main.LocalPlayer;
                if (player == null || !player.active || player.dead)
                    return;
                if (player.mouseInterface)
                    return; // don't fight the player when using UI

                // Only while a fishing pole is held.
                var held = player.HeldItem;
                if (held == null || held.IsAir || held.fishingPole <= 0)
                {
                    _hadBobber = false;
                    _recastTimer = 0;
                    return;
                }

                var bobber = FindBobber(player);
                var hasBobber = bobber != null;

                if (hasBobber)
                {
                    _hadBobber = true;
                    _recastTimer = 0;

                    // Bite? The vanilla bobber sets ai[1] > 0 while a fish is on the hook and the
                    // bobber is bobbing/sinking. Reel in by triggering the game's fishing pull.
                    if (HasBite(bobber))
                    {
                        UpdateBait(player);
                        ReelOrCast(player);
                    }
                    return;
                }

                // No bobber present.
                if (_hadBobber)
                {
                    // Line just returned (a catch or empty pull) — a catch message is emitted by
                    // the reel path. Start the short recast delay.
                    _hadBobber = false;
                    _recastTimer = Math.Max(1, _config.RecastDelay);
                    return;
                }

                if (_recastTimer > 0)
                {
                    _recastTimer--;
                    return;
                }

                // Cast a fresh line.
                UpdateBait(player);
                ReelOrCast(player);
                _recastTimer = Math.Max(1, _config.RecastDelay);
            }
            catch (Exception ex)
            {
                _ctx?.Log.Error("AutoFishing OnPreUpdate error", ex);
            }
        }

        /// <summary>Trigger the held item's use exactly like the original mod (reel or cast).</summary>
        private void ReelOrCast(Player player)
        {
            if (!ResolveItemCheck())
                return;

            player.controlUseItem = true;
            player.releaseUseItem = true;
            try
            {
                _itemCheck.Invoke(player, null);
            }
            catch (Exception ex)
            {
                _ctx?.Log.Error("AutoFishing ItemCheck invoke failed", ex);
            }
        }

        private Projectile FindBobber(Player player)
        {
            var projs = Main.projectile;
            if (projs == null)
                return null;
            var maxP = Math.Min(projs.Length, Main.maxProjectiles > 0 ? Main.maxProjectiles : projs.Length);
            for (var i = 0; i < maxP; i++)
            {
                var p = projs[i];
                if (p != null && p.active && p.owner == player.whoAmI && p.bobber)
                    return p;
            }
            return null;
        }

        /// <summary>
        /// A catch is ready to be reeled in when the vanilla bobber has rolled a drop: the game's
        /// own reel path (ItemCheck_CheckFishingBobbers) pulls the catch only when the bobber is
        /// idle (ai[0] == 0) and a drop has been rolled (ai[1] &lt; 0 with localAI[1] != 0).
        /// We mirror that condition so we reel exactly when there is something to catch.
        /// </summary>
        private static bool HasBite(Projectile bobber)
        {
            return bobber.ai[0] == 0f && bobber.ai[1] < 0f && bobber.localAI[1] != 0f;
        }

        private void UpdateBait(Player player)
        {
            _lastBait = 0;
            var inv = player.inventory;
            if (inv == null)
                return;
            for (var i = 0; i < inv.Length; i++)
            {
                if (inv[i] != null && inv[i].bait > 0)
                {
                    _lastBait = inv[i].type;
                    return;
                }
            }
        }

        private bool ResolveItemCheck()
        {
            if (_itemCheckResolved)
                return _itemCheck != null;
            _itemCheckResolved = true;
            try
            {
                _itemCheck = typeof(Player).GetMethod(
                    "ItemCheck",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (_itemCheck == null)
                    _ctx?.Log.Error("AutoFishing: Player.ItemCheck() not found");
                return _itemCheck != null;
            }
            catch (Exception ex)
            {
                _ctx?.Log.Error("AutoFishing ItemCheck reflection failed", ex);
                return false;
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
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("AutoFishing PostDraw error", ex);
            }
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;
            var L = _ctx.L;

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);
            ui.TextColored(L.Get("Settings.Hint", "Hold a fishing pole to auto-cast & reel."), new Color(160, 200, 255));
            ui.Separator();

            dirty |= ui.Checkbox(L.Get("Settings.ShowMessages", "Show catch messages"), ref _config.ShowMessages);
            dirty |= ui.Checkbox(L.Get("Settings.ShowIcons", "Show item icons in messages"), ref _config.ShowIcons);
            dirty |= ui.Checkbox(L.Get("Settings.ShareToChat", "Share messages to chat (MP)"), ref _config.ShareToChat);

            var delay = (float)_config.RecastDelay;
            if (ui.SliderFloat(L.Get("Settings.RecastDelay", "Recast delay (frames)"), ref delay, 1f, 60f))
            {
                _config.RecastDelay = (int)Math.Round(delay);
                dirty = true;
            }

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
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "AutoFishing.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("AutoFishing save config failed", ex);
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
            var msg = _config.Enabled ? _ctx.L.Get("Chat.On", "AutoFishing: ON") : _ctx.L.Get("Chat.Off", "AutoFishing: OFF");
            _ctx.Log.Info(msg);
            try { Main.NewText(msg, 120, 200, 255); }
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
                Main.NewText(_ctx.L.Format("Chat.Ready", _toggle != null ? _toggle.CurrentBindingDisplay : "?", _config.Enabled ? "ON" : "OFF"), 120, 200, 255);
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
