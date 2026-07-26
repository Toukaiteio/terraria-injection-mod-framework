using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace AutoSwingAim
{
    /// <summary>
    /// Fixes sluggish facing when vanilla "Auto swing for all weapons" is enabled.
    ///
    /// Root cause (Terraria 1.4.5.6):
    /// - SettingsEnabled_AutoReuseAllItems only sets Player.autoReuseAllWeapons, which
    ///   TryAllowingItemReuse uses to force releaseUseItem=true so attacks auto-repeat.
    /// - Classic melee swings (useStyle 1 — swords like Volcano / FieryGreatsword type 121)
    ///   draw and hit based on Player.direction. Direction is NOT updated toward the mouse
    ///   on swing start or mid-swing unless Item.useTurn is true (most broadswords are false).
    /// - Whip / shoot weapons go through ItemCheck_Shoot which does ChangeDir toward MouseWorld
    ///   every shot — so they feel fine under auto-reuse.
    ///
    /// Fix: while auto-reuse-all is on and the local player is attacking with a direction-based
    /// melee style, face the mouse (ChangeDir). Optionally treat the held item as useTurn for
    /// A/D mid-swing turns without permanently rewriting item defaults.
    /// </summary>
    [TimfMod(Id = "AutoSwingAim", Side = TimfSide.Client)]
    public sealed class AutoSwingAimMod : IClientMod, IModSettings, IPlayerUpdateHook
    {
        private IModContext _ctx;
        private AutoSwingAimConfig _config;
        private IPlayerUpdateHookRegistry _hookRegistry;

        // Live inventory item whose useTurn we temporarily forced true.
        private Item _useTurnForcedItem;
        private bool _useTurnWas;

        private static FieldInfo _settingsAutoReuseField;
        private static bool _settingsFieldTried;

        public string Name => "Auto Swing Aim";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "AutoSwingAim.json");
            _config = AutoSwingAimConfig.LoadOrCreate(cfgPath);

            if (context.Client != null && context.Client.PlayerUpdate != null)
            {
                _hookRegistry = context.Client.PlayerUpdate;
                _hookRegistry.Add(this);
            }
            else
                context.Log.Error("IClientServices.PlayerUpdate unavailable — feature will not run");

            context.Log.Info(
                "AutoSwingAim loaded. Enabled=" + _config.Enabled +
                " ContinuousTurn=" + _config.ContinuousTurn);
        }

        public void Unload()
        {
            RestoreUseTurn();
            try { _hookRegistry?.Remove(this); }
            catch { /* ignore */ }
            _hookRegistry = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            // no draw
        }

        // Player.ItemCheck prefix — runs after input CopyInto, before item use body.
        public void OnPreUpdate()
        {
            if (_config == null || !_config.Enabled)
            {
                RestoreUseTurn();
                return;
            }

            try
            {
                if (Main.gameMenu || Main.dedServ)
                {
                    RestoreUseTurn();
                    return;
                }

                var player = Main.LocalPlayer;
                if (player == null || !player.active || player.dead)
                {
                    RestoreUseTurn();
                    return;
                }

                if (!IsAutoReuseAllEnabled(player))
                {
                    RestoreUseTurn();
                    return;
                }

                if (!player.controlUseItem)
                {
                    RestoreUseTurn();
                    return;
                }

                Item item = null;
                try { item = player.HeldItem; }
                catch { item = null; }
                if (item == null || item.IsAir)
                {
                    RestoreUseTurn();
                    return;
                }

                if (!IsDirectionBasedMelee(item))
                {
                    RestoreUseTurn();
                    return;
                }

                // Optionally grant useTurn for this held item while auto-swinging so the
                // movement system also allows mid-swing A/D turns (vanilla gate at ~18067).
                if (_config.AllowMoveTurnWhileSwinging)
                    ForceUseTurn(item);
                else
                    RestoreUseTurn();

                // Face mouse: either every frame, or only when idle / just about to start a swing.
                var shouldFace =
                    _config.ContinuousTurn ||
                    player.itemAnimation == 0 ||
                    player.itemAnimation == player.itemAnimationMax ||
                    player.itemAnimation == player.itemAnimationMax - 1;

                if (shouldFace)
                    FaceMouse(player);
            }
            catch (Exception ex)
            {
                _ctx?.Log.Error("AutoSwingAim OnPreUpdate error", ex);
            }
        }

        /// <summary>
        /// Classic swing / thrust styles whose hitbox &amp; sprite are driven by Player.direction
        /// rather than continuous mouse aim (unlike useStyle 5 guns or whip shoot path).
        /// </summary>
        private static bool IsDirectionBasedMelee(Item item)
        {
            if (item == null)
                return false;

            // No weapon damage → not a combat swing we care about.
            if (item.damage <= 0)
                return false;

            // Channel weapons keep their own facing logic.
            if (item.channel)
                return false;

            // useStyle:
            //  1 = swing (swords, axes, hammers) — Volcano
            //  3 = stab/thrust (shortswords)
            //  15 = modern composite sword swings still keyed off direction for some items
            // Whips are summon + shoot + different useStyle and already face mouse in Shoot.
            var style = item.useStyle;
            if (style == 1 || style == 3)
                return true;

            // Melee with no projectile: always direction-based.
            if (item.melee && item.shoot <= 0)
                return true;

            // Melee that shoots only as a side effect on first frame (Volcano type 121 shoots
            // explosion particles via special flags, but still swings with useStyle 1).
            if (item.melee && style == 1)
                return true;

            return false;
        }

        private static void FaceMouse(Player player)
        {
            try
            {
                // Match ItemCheck_Shoot facing: MouseWorld vs mounted center X.
                var mouseWorldX = Main.mouseX + Main.screenPosition.X;
                var centerX = player.MountedCenter.X;
                var dir = mouseWorldX >= centerX ? 1 : -1;
                if (player.direction != dir)
                    player.ChangeDir(dir);
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsAutoReuseAllEnabled(Player player)
        {
            try
            {
                if (player.autoReuseAllWeapons)
                    return true;
            }
            catch { /* ignore */ }

            // Fallback: read Main.SettingsEnabled_AutoReuseAllItems if field exists.
            try
            {
                if (!_settingsFieldTried)
                {
                    _settingsFieldTried = true;
                    _settingsAutoReuseField = typeof(Main).GetField(
                        "SettingsEnabled_AutoReuseAllItems",
                        BindingFlags.Public | BindingFlags.Static);
                }

                if (_settingsAutoReuseField != null)
                    return (bool)_settingsAutoReuseField.GetValue(null);
            }
            catch { /* ignore */ }

            return false;
        }

        private void ForceUseTurn(Item item)
        {
            if (item == null)
                return;

            if (!ReferenceEquals(_useTurnForcedItem, item))
            {
                RestoreUseTurn();
                _useTurnForcedItem = item;
                _useTurnWas = item.useTurn;
            }

            if (!item.useTurn)
                item.useTurn = true;
        }

        private void RestoreUseTurn()
        {
            if (_useTurnForcedItem == null)
                return;
            try
            {
                _useTurnForcedItem.useTurn = _useTurnWas;
            }
            catch { /* ignore */ }
            _useTurnForcedItem = null;
            _useTurnWas = false;
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;
            var L = _ctx.L;

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);
            dirty |= ui.Checkbox(L.Get("Settings.ContinuousTurn", "Continuous turn (follow mouse mid-swing)"), ref _config.ContinuousTurn);
            dirty |= ui.Checkbox(L.Get("Settings.AllowMoveTurn", "Allow A/D turn while swinging (useTurn)"), ref _config.AllowMoveTurnWhileSwinging);

            if (dirty)
                SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "AutoSwingAim.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("AutoSwingAim save config failed", ex);
            }
        }
    }
}
