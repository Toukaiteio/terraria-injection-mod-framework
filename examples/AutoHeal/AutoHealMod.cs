using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace AutoHeal
{
    /// <summary>
    /// Client-side automatic Quick Heal / Quick Mana (behavior-level port of Mašina's
    /// "Automatic Quick Heal" / AutoHealMod — source is hideCode).
    ///
    /// When local HP or mana drops to or below a configurable ratio of max, calls the
    /// vanilla <see cref="Player.QuickHeal"/> / <see cref="Player.QuickMana"/> paths so
    /// all consumable heal/mana items work the same as the quick-use hotkeys (including
    /// potion sickness / best-potion selection). No teleport or non-info side effects.
    ///
    /// Driven from <see cref="IPlayerUpdateHook"/> (Player.ItemCheck prefix).
    /// </summary>
    [TimfMod(Id = "AutoHeal", Side = TimfSide.Client)]
    public sealed class AutoHealMod : IMod, IModSettings, IPlayerUpdateHook
    {
        private IModContext _ctx;
        private AutoHealConfig _config;
        private IPlayerUpdateHookRegistry _hookRegistry;

        public string Name => "Auto Heal";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "AutoHeal.json");
            _config = AutoHealConfig.LoadOrCreate(cfgPath);

            if (context.Services.TryGetService(out _hookRegistry) && _hookRegistry != null)
                _hookRegistry.Add(this);
            else
                context.Log.Error("IPlayerUpdateHookRegistry unavailable — auto heal will not run");

            context.Log.Info(
                "AutoHeal loaded. AutoHeal=" + _config.AutoHeal +
                " AutoMana=" + _config.AutoMana +
                " HealBelow=" + _config.HealBelowPercent +
                " ManaBelow=" + _config.ManaBelowPercent);
        }

        public void Unload()
        {
            try { _hookRegistry?.Remove(this); }
            catch { /* ignore */ }
            _hookRegistry = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            // no per-frame draw
        }

        // Player.ItemCheck prefix for the local player.
        public void OnPreUpdate()
        {
            if (_config == null)
                return;
            if (!_config.AutoHeal && !_config.AutoMana)
                return;

            try
            {
                if (Main.gameMenu || Main.dedServ)
                    return;

                var player = Main.LocalPlayer;
                if (player == null || !player.active || player.dead)
                    return;

                // Don't fire while chatting / typing or in inventory UI.
                if (Main.drawingPlayerChat || Main.editSign || Main.editChest)
                    return;

                if (_config.AutoHeal)
                    TryAutoHeal(player);

                if (_config.AutoMana)
                    TryAutoMana(player);
            }
            catch (Exception ex)
            {
                _ctx?.Log.Error("AutoHeal OnPreUpdate error", ex);
            }
        }

        private void TryAutoHeal(Player player)
        {
            var max = player.statLifeMax2;
            if (max <= 0)
                return;

            var ratio = (float)player.statLife / max;
            if (ratio > _config.HealBelowPercent)
                return;

            // Vanilla QuickHeal already no-ops on full HP, potionDelay, cursed/CCed/dead,
            // and when no suitable potion is in inventory / void bag.
            player.QuickHeal();
        }

        private void TryAutoMana(Player player)
        {
            var max = player.statManaMax2;
            if (max <= 0)
                return;

            var ratio = (float)player.statMana / max;
            if (ratio > _config.ManaBelowPercent)
                return;

            // Vanilla QuickMana already no-ops when full / no item / potion-delay on potion mana pots.
            player.QuickMana();
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;
            var L = _ctx.L;

            ui.Text(L.Get("Settings.Title", "Auto Quick Heal / Mana (client-side)."));
            ui.TextColored(L.Get("Settings.Hint", "Uses vanilla QuickHeal / QuickMana item selection."), new Color(160, 200, 255));
            ui.Separator();

            dirty |= ui.Checkbox(L.Get("Settings.AutoHeal", "Auto Quick Heal"), ref _config.AutoHeal);
            dirty |= ui.SliderFloat(L.Get("Settings.HealBelow", "Heal when HP ≤ %"), ref _config.HealBelowPercent, 0.05f, 1f);
            ui.TextColored(
                L.Format("Settings.HealThreshold", Pct(_config.HealBelowPercent)),
                new Color(150, 150, 150));

            ui.Spacing();
            dirty |= ui.Checkbox(L.Get("Settings.AutoMana", "Auto Quick Mana"), ref _config.AutoMana);
            dirty |= ui.SliderFloat(L.Get("Settings.ManaBelow", "Mana when MP ≤ %"), ref _config.ManaBelowPercent, 0.05f, 1f);
            ui.TextColored(
                L.Format("Settings.ManaThreshold", Pct(_config.ManaBelowPercent)),
                new Color(150, 150, 150));

            ui.Spacing();
            ui.TextColored(L.Get("Settings.Respects", "Respects potion sickness & inventory/void-bag items."), new Color(150, 150, 150));

            if (dirty)
                SaveConfig();
        }

        private static string Pct(float ratio)
        {
            var p = (int)Math.Round(MathHelper.Clamp(ratio, 0.01f, 1f) * 100f);
            return p + "%";
        }

        private void SaveConfig()
        {
            try
            {
                // Clamp after slider edits.
                if (_config.HealBelowPercent < 0.05f) _config.HealBelowPercent = 0.05f;
                if (_config.HealBelowPercent > 1f) _config.HealBelowPercent = 1f;
                if (_config.ManaBelowPercent < 0.05f) _config.ManaBelowPercent = 0.05f;
                if (_config.ManaBelowPercent > 1f) _config.ManaBelowPercent = 1f;

                _config.Save(Path.Combine(_ctx.ConfigDirectory, "AutoHeal.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("AutoHeal save config failed", ex);
            }
        }
    }
}
