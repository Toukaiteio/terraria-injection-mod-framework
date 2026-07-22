using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace IHaveMyPhoneAnyway
{
    /// <summary>
    /// Treats the player as if they always carry a Cell/Shellphone for the purpose of the
    /// informational displays (time, position/depth, weather, moon phase, fishing power, rare
    /// creatures, treasure/ore/enemy detection, DPS, etc.) WITHOUT granting the phone's teleport.
    ///
    /// Works by setting the local player's info-accessory flags via framework
    /// IInfoAccessoryHook (postfixes on Player.UpdateEquips every frame and
    /// Player.RefreshInfoAccs when inventory is open), after the game recomputes them.
    /// </summary>
    [TimfMod(Id = "I-Have-My-Phone-Anyway", Side = TimfSide.Client)]
    public sealed class PhoneAnywayMod : IMod, IModSettings, IInfoAccessoryHook
    {
        private IModContext _ctx;
        private PhoneConfig _config;
        private IInfoAccessoryHookRegistry _registry;

        public string Name => "I Have My Phone Anyway";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            var cfgPath = Path.Combine(context.ConfigDirectory, "IHaveMyPhoneAnyway.json");
            _config = PhoneConfig.LoadOrCreate(cfgPath);

            if (context.Services.TryGetService(out _registry) && _registry != null)
                _registry.Add(this);
            else
                context.Log.Error("IInfoAccessoryHookRegistry unavailable — info displays will not be granted");

            context.Log.Info("I-Have-My-Phone-Anyway loaded. Enabled=" + _config.Enabled);
        }

        public void Unload()
        {
            try { _registry?.Remove(this); }
            catch { /* ignore */ }
            _registry = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            // Nothing per-frame; all effect happens in the RefreshInfoAccs postfix.
        }

        // Called after Player.RefreshInfoAccs for the local player.
        public void OnRefreshInfoAccessories(object localPlayer)
        {
            if (_config == null || !_config.Enabled)
                return;

            var player = localPlayer as Player;
            if (player == null)
                return;

            // Watch tiers: 1=copper (hour), 2=silver (minute), 3=gold (exact time).
            if (_config.Clock && player.accWatch < 3)
                player.accWatch = 3;

            if (_config.PositionAndDepth)
            {
                player.accCompass = 1;   // horizontal position
                player.accDepthMeter = 1; // depth
            }

            if (_config.Weather)
                player.accWeatherRadio = true;   // weather / wind / rain

            if (_config.Fishing)
                player.accFishFinder = true;     // fishing power / bait power / line

            if (_config.MoonAndEvents)
                player.accCalendar = true;       // moon phase / events / invasion progress

            if (_config.RareCreatures)
                player.accCritterGuide = true;   // nearby rare creatures

            if (_config.Detection)
            {
                player.accThirdEye = true;   // nearby enemy count
                player.accJarOfSouls = true; // treasure / rare tiles nearby (Metal Detector-ish)
                player.accOreFinder = true;  // ore detection (Metal Detector)
                player.accDreamCatcher = true; // DPS meter
            }

            if (_config.Movement)
                player.accStopwatch = true;      // movement speed
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            var dirty = false;
            var L = _ctx.L;

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);
            ui.TextColored(L.Get("Settings.Hint", "Shows phone info displays. No teleport."), new Color(160, 200, 255));
            ui.Separator();
            ui.Text(L.Get("Settings.Categories", "Info categories:"));
            dirty |= ui.Checkbox(L.Get("Settings.Clock", "Clock (exact time)"), ref _config.Clock);
            dirty |= ui.Checkbox(L.Get("Settings.Position", "Position + depth"), ref _config.PositionAndDepth);
            dirty |= ui.Checkbox(L.Get("Settings.Weather", "Weather / wind"), ref _config.Weather);
            dirty |= ui.Checkbox(L.Get("Settings.Fishing", "Fishing power"), ref _config.Fishing);
            dirty |= ui.Checkbox(L.Get("Settings.Moon", "Moon phase / events"), ref _config.MoonAndEvents);
            dirty |= ui.Checkbox(L.Get("Settings.Rare", "Rare creatures nearby"), ref _config.RareCreatures);
            dirty |= ui.Checkbox(L.Get("Settings.Detection", "Detection (enemies/treasure/ore/DPS)"), ref _config.Detection);
            dirty |= ui.Checkbox(L.Get("Settings.Movement", "Movement speed"), ref _config.Movement);

            ui.Spacing();
            ui.TextColored(L.Get("Settings.Tip", "Tip: hover the info icons (top-left) for details."), new Color(150, 150, 150));

            if (dirty)
                SaveConfig();
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "IHaveMyPhoneAnyway.json"));
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("IHaveMyPhoneAnyway save config failed", ex);
            }
        }
    }
}
