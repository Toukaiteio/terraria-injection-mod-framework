using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace LootRates
{
    /// <summary>
    /// Vanilla-compatible host plugin: multiplies coin drops and adds extra item-loot rolls
    /// on the authoritative process only (SP / Host &amp; Play / dedicated).
    /// Pure vanilla clients can join; loot is generated server-side and synced with vanilla packets.
    /// </summary>
    [TimfMod(Id = "LootRates")]
    public sealed class LootRatesMod : IAuthorityMod, IModSettings, IAuthorityLifecycle, IModFeatureToggle
    {
        private IModContext _ctx;
        private LootRatesConfig _config;
        private IModPatchService _patches;
        private bool _hooksLive;

        // Guard re-entrancy when we re-invoke loot helpers from postfixes.
        private static int _extraRollDepth;

        public string Name => "Loot Rates";
        public string Version => "1.0.0";

        // Shared with Harmony patches (set before patches run).
        internal static LootRatesConfig ActiveConfig;
        internal static ILogger ActiveLog;
        internal static MethodInfo DropItemsMethod;
        internal static MethodInfo DropMoneyMethod;
        internal static ITerrariaReflection Reflection;

        public void Load(IModContext context)
        {
            _ctx = context;
            _patches = context.Patches;
            _config = LootRatesConfig.LoadOrCreate(context.Storage, "LootRates.json");
            ActiveConfig = _config;
            ActiveLog = context.Log;
            Reflection = context.Services.GetService<ITerrariaReflection>();

            try
            {
                DropItemsMethod = AccessTools.Method(typeof(NPC), "NPCLoot_DropItems", new[] { typeof(Player) });
                DropMoneyMethod = AccessTools.Method(typeof(NPC), "NPCLoot_DropMoney", new[] { typeof(Player) });
            }
            catch (Exception ex)
            {
                context.Log.Error("LootRates: failed to resolve NPC loot methods", ex);
            }

            context.Log.Info(
                "LootRates plugin Load. Enabled=" + _config.Enabled +
                " ExtraItemRolls=" + _config.ExtraItemRolls +
                " CoinMult=" + _config.CoinMultiplier);
        }

        /// <summary>In-world feature switch for hubs — mod enablement itself is menu-only.</summary>
        public bool FeatureEnabled
        {
            get { return _config != null && _config.Enabled; }
            set
            {
                if (_config == null || _config.Enabled == value)
                    return;
                _config.Enabled = value;
                try
                {
                    _config.Save(_ctx.Storage, "LootRates.json");
                    ActiveConfig = _config;
                }
                catch (Exception ex)
                {
                    _ctx.Log.Error("LootRates save failed", ex);
                }
            }
        }

        public void Unload()
        {
            UninstallHooks();
            ActiveConfig = null;
            ActiveLog = null;
            DropItemsMethod = null;
            DropMoneyMethod = null;
            Reflection = null;
            _ctx = null;
        }

        public void OnAuthorityActivate(IModContext context)
        {
            ActiveConfig = _config;
            ActiveLog = context.Log ?? _ctx?.Log;
            if (context.Authority == null || !context.Authority.IsAuthoritative)
            {
                context.Log.Warn("LootRates OnAuthorityActivate skipped — not authoritative");
                return;
            }
            InstallHooks();
            context.Log.Info("LootRates OnAuthorityActivate — hooks live (host authority)");
        }

        public void OnAuthorityDeactivate()
        {
            UninstallHooks();
            _ctx?.Log.Info("LootRates OnAuthorityDeactivate — hooks removed");
        }

        public void PostDraw(GameTime gameTime)
        {
            // Plugin: no draw.
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            if (_config == null)
            {
                ui.TextColored("Config not loaded yet (enter a world / host to activate plugin).", new Color(200, 180, 120));
                return;
            }

            var dirty = false;
            var L = _ctx.L;

            ui.TextColored(L.Get("Settings.Title", "Vanilla-compatible loot multipliers (host only)."), new Color(160, 200, 255));
            ui.TextColored(
                L.Get("Settings.Hint", "Runs on SP / Host / dedicated. Vanilla clients receive normal item packets."),
                new Color(150, 150, 150));
            ui.Separator();

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enable loot rates"), ref _config.Enabled);

            var rolls = (float)_config.ExtraItemRolls;
            if (ui.SliderFloat(L.Get("Settings.ExtraRolls", "Extra item-drop rolls"), ref rolls, 0f, 10f))
            {
                _config.ExtraItemRolls = LootRatesConfig.ClampRolls((int)Math.Round(rolls));
                dirty = true;
            }
            _config.ExtraItemRolls = LootRatesConfig.ClampRolls((int)Math.Round(rolls));

            dirty |= ui.SliderFloat(
                L.Get("Settings.CoinMult", "Coin multiplier"),
                ref _config.CoinMultiplier,
                1f,
                20f);

            ui.Spacing(4f);
            ui.TextColored(
                L.Format("Settings.Status",
                    _config.Enabled ? "ON" : "OFF",
                    _config.ExtraItemRolls.ToString(),
                    LootRatesConfig.ClampCoin(_config.CoinMultiplier).ToString("0.##"),
                    _hooksLive ? "live" : "idle"),
                _hooksLive && _config.Enabled ? new Color(140, 220, 140) : new Color(160, 160, 160));

            if (dirty)
            {
                try
                {
                    _config.CoinMultiplier = LootRatesConfig.ClampCoin(_config.CoinMultiplier);
                    _config.Save(_ctx.Storage, "LootRates.json");
                    ActiveConfig = _config;
                }
                catch (Exception ex)
                {
                    _ctx.Log.Error("LootRates save failed", ex);
                }
            }
        }

        private void InstallHooks()
        {
            if (_hooksLive)
                return;

            try
            {
                if (DropItemsMethod != null)
                {
                    _patches.PatchPostfix(DropItemsMethod, typeof(LootRatesMod).GetMethod(
                        nameof(DropItems_Postfix), BindingFlags.NonPublic | BindingFlags.Static));
                }
                if (DropMoneyMethod != null)
                {
                    _patches.PatchPostfix(DropMoneyMethod, typeof(LootRatesMod).GetMethod(
                        nameof(DropMoney_Postfix), BindingFlags.NonPublic | BindingFlags.Static));
                }
                _hooksLive = true;
                ActiveLog?.Info("LootRates Harmony patches installed");
            }
            catch (Exception ex)
            {
                ActiveLog?.Error("LootRates InstallHooks failed", ex);
                try { _patches?.UnpatchAll(); } catch { /* ignore */ }
                _hooksLive = false;
            }
        }

        private void UninstallHooks()
        {
            if (!_hooksLive)
                return;
            try
            {
                _patches?.UnpatchAll();
            }
            catch { /* ignore */ }
            _hooksLive = false;
        }

        /// <summary>
        /// After vanilla item rolls, optionally run more full DropItems passes.
        /// NPCLoot already no-ops on multiplayer clients (netMode==1).
        /// </summary>
        private static void DropItems_Postfix(NPC __instance, Player closestPlayer)
        {
            if (_extraRollDepth > 0)
                return;
            var cfg = ActiveConfig;
            if (cfg == null || !cfg.Enabled)
                return;
            var extra = LootRatesConfig.ClampRolls(cfg.ExtraItemRolls);
            if (extra <= 0 || DropItemsMethod == null || __instance == null)
                return;

            _extraRollDepth++;
            try
            {
                for (var i = 0; i < extra; i++)
                    Reflection.Invoke(DropItemsMethod, __instance, new object[] { closestPlayer });
            }
            catch (Exception ex)
            {
                ActiveLog?.Error("LootRates DropItems extra roll failed", ex);
            }
            finally
            {
                _extraRollDepth--;
            }
        }

        /// <summary>
        /// After vanilla coin drop, run additional DropMoney rounds for fractional/integer mult.
        /// CoinMultiplier 2.0 → one extra full money drop; 2.5 → one extra + 50% chance of another.
        /// </summary>
        private static void DropMoney_Postfix(NPC __instance, Player closestPlayer)
        {
            if (_extraRollDepth > 0)
                return;
            var cfg = ActiveConfig;
            if (cfg == null || !cfg.Enabled)
                return;
            var mult = LootRatesConfig.ClampCoin(cfg.CoinMultiplier);
            if (mult <= 1.001f || DropMoneyMethod == null || __instance == null)
                return;

            // Total money drops = mult; vanilla already did 1.
            var remaining = mult - 1f;
            var whole = (int)Math.Floor(remaining);
            var frac = remaining - whole;

            _extraRollDepth++;
            try
            {
                for (var i = 0; i < whole; i++)
                    Reflection.Invoke(DropMoneyMethod, __instance, new object[] { closestPlayer });
                if (frac > 0.001f && Main.rand.NextFloat() < frac)
                    Reflection.Invoke(DropMoneyMethod, __instance, new object[] { closestPlayer });
            }
            catch (Exception ex)
            {
                ActiveLog?.Error("LootRates DropMoney extra roll failed", ex);
            }
            finally
            {
                _extraRollDepth--;
            }
        }
    }
}
