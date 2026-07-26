using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace LootRates
{
    /// <summary>
    /// Example <see cref="IAuthorityMod"/>: coin / item loot multipliers on the host only.
    ///
    /// Best-practice shape:
    /// <list type="bullet">
    /// <item>Implement <see cref="IAuthorityMod"/> → loader infers <see cref="TimfSide.Authority"/>.</item>
    /// <item>Leave <c>Net</c> at the default <see cref="TimfNetProfile.Vanilla"/>: no handshake
    /// catalog entry, so pure vanilla clients can still join the host.</item>
    /// <item>Install Harmony hooks only when <see cref="IAuthorityServices.IsAuthoritative"/>.</item>
    /// <item>Keep vanilla item/money packets so remote clients stay compatible.</item>
    /// </list>
    /// </summary>
    [TimfMod(Id = "LootRates")]
    public sealed class LootRatesMod : IAuthorityMod, IModSettings, IAuthorityLifecycle
    {
        private IModContext _ctx;
        private LootRatesConfig _config;
        private Harmony _harmony;
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

        public void Load(IModContext context)
        {
            _ctx = context;
            _config = LootRatesConfig.LoadOrCreate(Path.Combine(context.ConfigDirectory, "LootRates.json"));
            ActiveConfig = _config;
            ActiveLog = context.Log;

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

        public void Unload()
        {
            UninstallHooks();
            ActiveConfig = null;
            ActiveLog = null;
            DropItemsMethod = null;
            DropMoneyMethod = null;
            _ctx = null;
        }

        public void OnAuthorityActivate(IModContext context)
        {
            ActiveConfig = _config;
            ActiveLog = context.Log ?? _ctx?.Log;
            // Best practice: only install authority hooks when the process owns the world.
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
                ui.TextColored("Enter a world to activate.", new Color(200, 180, 120));
                return;
            }

            var dirty = false;
            var L = _ctx.L;

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

            if (dirty)
            {
                try
                {
                    _config.CoinMultiplier = LootRatesConfig.ClampCoin(_config.CoinMultiplier);
                    _config.Save(Path.Combine(_ctx.ConfigDirectory, "LootRates.json"));
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
                _harmony = new Harmony("timf.plugin.LootRates");
                if (DropItemsMethod != null)
                {
                    _harmony.Patch(
                        DropItemsMethod,
                        postfix: new HarmonyMethod(typeof(LootRatesMod), nameof(DropItems_Postfix)));
                }
                if (DropMoneyMethod != null)
                {
                    _harmony.Patch(
                        DropMoneyMethod,
                        postfix: new HarmonyMethod(typeof(LootRatesMod), nameof(DropMoney_Postfix)));
                }
                _hooksLive = true;
                ActiveLog?.Info("LootRates Harmony patches installed");
            }
            catch (Exception ex)
            {
                ActiveLog?.Error("LootRates InstallHooks failed", ex);
                try { _harmony?.UnpatchAll("timf.plugin.LootRates"); } catch { /* ignore */ }
                _harmony = null;
                _hooksLive = false;
            }
        }

        private void UninstallHooks()
        {
            if (!_hooksLive && _harmony == null)
                return;
            try
            {
                if (_harmony != null)
                    _harmony.UnpatchAll("timf.plugin.LootRates");
            }
            catch { /* ignore */ }
            _harmony = null;
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
                    DropItemsMethod.Invoke(__instance, new object[] { closestPlayer });
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
                    DropMoneyMethod.Invoke(__instance, new object[] { closestPlayer });
                if (frac > 0.001f && Main.rand.NextFloat() < frac)
                    DropMoneyMethod.Invoke(__instance, new object[] { closestPlayer });
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
