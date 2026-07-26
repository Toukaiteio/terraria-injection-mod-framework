using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace AutoAim
{
    /// <summary>
    /// AFK combat helper: auto-aims the held weapon at the nearest matching entity.
    /// Optional auto-click holds attack when a target is found.
    ///
    /// Runs from IPlayerUpdateHook which fires as a Prefix on Player.ItemCheck (after input
    /// CopyInto, so controlUseItem is not overwritten by the real mouse that frame).
    /// </summary>
    [TimfMod(Id = "AutoAim", Side = TimfSide.Client)]
    public sealed class AutoAimMod : IClientMod, IModSettings, IPlayerUpdateHook
    {
        private IModContext _ctx;
        private AutoAimConfig _config;
        private LineOfSight _los;
        private WeaponWallPolicy _weaponWalls;
        private IPlayerUpdateHookRegistry _hookRegistry;
        private const string ToggleId = "AutoAim.Toggle";
        private IKeybind _toggle;
        private IKeybindService _keybinds;
        private bool _announcePending = true;

        private int _currentTargetNpc = -1;
        private bool _wantReleaseNext;

        public string Name => "Auto Aim";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "AutoAim.json");
            _config = AutoAimConfig.LoadOrCreate(cfgPath);
            var defaultKey = ParseKey(_config.ToggleKey, Keys.OemTilde);
            _los = new LineOfSight(context.Log);
            _weaponWalls = new WeaponWallPolicy(context.Log);

            _keybinds = context.Client != null ? context.Client.Keybinds : null;
            if (_keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "Auto Aim Toggle"), defaultKey);
            else
                context.Log.Error("IKeybindService unavailable — AutoAim toggle will not work");

            if (context.Client != null && context.Client.PlayerUpdate != null)
            {
                _hookRegistry = context.Client.PlayerUpdate;
                _hookRegistry.Add(this);
            }
            else
                context.Log.Error("IClientServices.PlayerUpdate unavailable — feature will not run");

            context.Log.Info("AutoAim loaded. Toggle=" + ToggleId + " default=" + defaultKey + " enabled=" + _config.Enabled + " autoClick=" + _config.AutoClick);
        }

        public void Unload()
        {
            try { _hookRegistry?.Remove(this); } catch { /* ignore */ }
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _hookRegistry = null;
            _keybinds = null;
            _toggle = null;
            _los = null;
            _weaponWalls = null;
            _ctx = null;
        }

        // Fires as Prefix on Player.ItemCheck for the local player.
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

                // Don't fight UI / inventory clicks.
                if (player.mouseInterface || Main.playerInventory)
                    return;

                var target = FindBestTarget(player);
                _currentTargetNpc = target;
                if (target < 0)
                {
                    _wantReleaseNext = false;
                    return;
                }

                var npc = Main.npc[target];
                if (npc == null || !npc.active)
                    return;

                // Aim: screen-space mouse toward target (ItemCheck / shoot use Main.mouseX/Y).
                var aimWorld = npc.Center;
                var screen = aimWorld - Main.screenPosition;
                // Prefer unclamped aim so off-screen bosses still get a correct direction.
                Main.mouseX = (int)screen.X;
                Main.mouseY = (int)screen.Y;

                if (!_config.AutoClick)
                {
                    _wantReleaseNext = false;
                    return;
                }

                var held = player.HeldItem;
                if (held == null || held.IsAir || held.damage <= 0)
                    return;

                // Mid-swing: keep holding so channels / multi-frame uses continue.
                if (player.itemAnimation > 0)
                {
                    player.controlUseItem = true;
                    return;
                }

                // reuseDelay: wait it out, don't click.
                if (player.reuseDelay > 0)
                {
                    _wantReleaseNext = false;
                    return;
                }

                // Non-autoReuse weapons need a release between swings (releaseUseItem edge).
                if (!held.autoReuse && _wantReleaseNext)
                {
                    // One frame with controlUseItem left false so releaseUseItem becomes true next press.
                    player.controlUseItem = false;
                    _wantReleaseNext = false;
                    return;
                }

                // Start / continue attack this frame.
                player.controlUseItem = true;
                player.releaseUseItem = true;
                try
                {
                    // ItemCheck start-use path also checks mouseLeftRelease for a fresh press.
                    Main.mouseLeft = true;
                    Main.mouseLeftRelease = true;
                }
                catch { /* ignore */ }

                if (!held.autoReuse)
                    _wantReleaseNext = true; // next ready frame will release first
            }
            catch (Exception ex)
            {
                _ctx?.Log.Error("AutoAim OnPreUpdate error", ex);
            }
        }

        private int FindBestTarget(Player player)
        {
            var npcs = Main.npc;
            if (npcs == null)
                return -1;

            var center = player.Center;
            var rangeSq = _config.Range * _config.Range;
            var best = -1;
            var bestDistSq = float.MaxValue;

            // Resolve once per scan (not per NPC).
            var ignoreLos =
                _config.IgnoreWalls
                || (_weaponWalls != null && _weaponWalls.HeldWeaponPassesThroughWalls(player));

            var maxN = Math.Min(npcs.Length, Main.maxNPCs > 0 ? Main.maxNPCs : npcs.Length);
            for (var i = 0; i < maxN; i++)
            {
                var npc = npcs[i];
                if (npc == null || !npc.active)
                    continue;
                if (npc.life <= 0 || npc.immortal || npc.dontTakeDamage)
                    continue;
                if (!MatchesCategory(npc))
                    continue;

                var delta = npc.Center - center;
                var distSq = delta.LengthSquared();
                if (distSq > rangeSq || distSq >= bestDistSq)
                    continue;

                // Wall policy (strict):
                // - IgnoreWalls config → no LOS
                // - projectile-primary weapon with tileCollide==false → no LOS
                // - wall-phasing NPC (noTileCollide) → no LOS (vanilla melee rule)
                // - else require Collision.CanHit
                // Note: contact-melee swing projs often have tileCollide=false; those do NOT
                // set ignoreLos (see WeaponWallPolicy) so normal enemies still need LOS.
                if (!HasLineOfSightOrThroughWalls(player, npc, ignoreLos))
                    continue;

                best = i;
                bestDistSq = distSq;
            }

            return best;
        }

        /// <summary>
        /// True if we may aim at this NPC under the current wall / weapon policy.
        /// </summary>
        private bool HasLineOfSightOrThroughWalls(Player player, NPC npc, bool ignoreLosForWeaponOrConfig)
        {
            if (ignoreLosForWeaponOrConfig)
                return true;

            // Vanilla ItemCheck melee paths: (npc.noTileCollide || Collision.CanHit(...)).
            if (CanEngageThroughWalls(npc))
                return true;

            if (_los == null)
                return true;

            return _los.CanReach(
                player.position, player.width, player.height,
                npc.position, npc.width, npc.height);
        }

        /// <summary>
        /// NPCs that do not collide with tiles are normally hittable without open LOS
        /// (vanilla uses the same exception for melee / several hit checks).
        /// </summary>
        private static bool CanEngageThroughWalls(NPC npc)
        {
            if (npc == null)
                return false;
            try
            {
                if (npc.noTileCollide)
                    return true;
            }
            catch
            {
                // Field missing on unexpected builds — fall through to LOS.
            }

            return false;
        }

        private bool MatchesCategory(NPC npc)
        {
            if (npc.townNPC)
                return _config.TargetTownNpcs;
            if (npc.CountsAsACritter)
                return _config.TargetCritters;
            if (npc.boss)
                return _config.TargetBosses;
            if (npc.friendly)
                return false;
            return _config.TargetHostile;
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
                _ctx.Log.Error("AutoAim PostDraw error", ex);
            }
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;
            var L = _ctx.L;

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);
            dirty |= ui.Checkbox(L.Get("Settings.AutoClick", "Auto click"), ref _config.AutoClick);
            ui.Separator();

            ui.Text(L.Get("Settings.TargetTypes", "Target types"));
            dirty |= ui.Checkbox(L.Get("Settings.Hostile", "Hostile enemies"), ref _config.TargetHostile);
            dirty |= ui.Checkbox(L.Get("Settings.Bosses", "Bosses"), ref _config.TargetBosses);
            dirty |= ui.Checkbox(L.Get("Settings.Critters", "Critters"), ref _config.TargetCritters);
            dirty |= ui.Checkbox(L.Get("Settings.TownNpcs", "Town NPCs"), ref _config.TargetTownNpcs);
            ui.Separator();

            dirty |= ui.Checkbox(
                L.Get("Settings.IgnoreWalls", "Ignore walls for all targets"),
                ref _config.IgnoreWalls);
            dirty |= ui.SliderFloat(L.Get("Settings.Range", "Range"), ref _config.Range, 100f, 2000f);

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
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "AutoAim.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("AutoAim save config failed", ex);
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
            var msg = _config.Enabled
                ? _ctx.L.Get("Chat.On", "AutoAim: ON")
                : _ctx.L.Get("Chat.Off", "AutoAim: OFF");
            _ctx.Log.Info(msg);
            try { Main.NewText(msg, 255, 180, 80); }
            catch { /* ignore */ }
        }

        private bool IsGameFocused()
        {
            try
            {
                IImmediateModeUi ui;
                if (_ctx != null && _ctx.Services.TryGetService(out ui) && ui != null)
                    return ui.IsGameFocused;
            }
            catch { /* ignore */ }

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
                var bind = _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay)
                    ? _toggle.CurrentBindingDisplay : "?";
                var state = _config.Enabled ? "ON" : "OFF";
                Main.NewText(_ctx.L.Format("Chat.Ready", bind, state), 255, 180, 80);
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
