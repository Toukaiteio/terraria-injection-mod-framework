using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;

namespace WeatherControl
{
    /// <summary>
    /// Vanilla-compatible host authority: weather via framework <see cref="IWeatherService"/>.
    /// Default <see cref="TimfNetProfile.Vanilla"/> — weather rides vanilla packets, so pure
    /// vanilla clients can still join.
    /// </summary>
    [TimfMod(Id = "WeatherControl")]
    public sealed class WeatherControlMod : IAuthorityMod, IModSettings, IAuthorityLifecycle
    {
        private IModContext _ctx;
        private IWeatherService _weather;
        private WeatherControlConfig _config;
        private bool _active;

        public string Name => "Weather Control";
        public string Version => "1.1.2";

        public void Load(IModContext context)
        {
            _ctx = context;
            _weather = ResolveWeather(context);
            _config = WeatherControlConfig.LoadOrCreate(Path.Combine(context.ConfigDirectory, "WeatherControl.json"));
            WeatherApplier.Bind(_weather, _config, context.Log);
            context.Log.Info(
                "WeatherControl Load. Enabled=" + _config.Enabled +
                " Hold=" + _config.LockWeather +
                " Preset=" + _config.Preset +
                " Api=" + (_weather != null ? "ok" : "missing"));
        }

        public void Unload()
        {
            WeatherApplier.Clear();
            _config = null;
            _weather = null;
            _ctx = null;
            _active = false;
        }

        public void OnAuthorityActivate(IModContext context)
        {
            _weather = ResolveWeather(context) ?? _weather;
            WeatherApplier.Bind(_weather, _config, context.Log ?? _ctx?.Log);

            if (context.Authority == null || !context.Authority.IsAuthoritative)
            {
                context.Log.Warn("WeatherControl skip activate — not authoritative");
                return;
            }

            if (_weather == null)
            {
                context.Log.Error("WeatherControl: IWeatherService unavailable");
                return;
            }

            _active = true;

            // Seed optional fields from the live world once so first apply doesn't clobber
            // wind/moon/events the player never set (legacy Apply* flags defaulted false).
            WeatherApplier.SeedUnsetFromWorld(_config);

            if (_config != null && _config.Enabled)
                WeatherApplier.Apply(_config, syncNetwork: true);
            else
                WeatherApplier.UpdateLock(_config);

            context.Log.Info("WeatherControl active: " + WeatherApplier.DescribeCurrent());
        }

        public void OnAuthorityDeactivate()
        {
            try
            {
                if (_weather != null && _weather.IsLockEnabled)
                    _weather.SetLock(null, false);
            }
            catch { /* ignore */ }

            _active = false;
            _ctx?.Log.Info("WeatherControl deactivated");
        }

        public void PostDraw(GameTime gameTime)
        {
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            if (_config == null)
            {
                ui.TextColored("Enter a world to activate.", new Color(200, 180, 120));
                return;
            }

            var L = _ctx.L;
            var dirty = false;

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);
            // Real lock: re-apply after vanilla UpdateWeather. Not the same as wind/moon/events.
            dirty |= ui.Checkbox(
                L.Get("Settings.Hold", "Hold weather (block vanilla random changes)"),
                ref _config.LockWeather);

            ui.Spacing(4f);
            ui.Text(L.Get("Settings.PresetHeader", "Preset"));
            DrawPresetRow(ui, ref dirty);

            ui.Spacing(4f);
            ui.Separator();
            dirty |= ui.SliderFloat(L.Get("Settings.WindSpeed", "Wind"), ref _config.WindSpeed, -1.2f, 1.2f);

            ui.Spacing(4f);
            ui.Separator();
            var moonF = (float)_config.MoonPhase;
            if (ui.SliderFloat(L.Get("Settings.MoonPhase", "Moon 0–7"), ref moonF, 0f, 7f))
            {
                _config.MoonPhase = (int)Math.Round(moonF);
                dirty = true;
            }
            _config.MoonPhase = (int)Math.Round(MathHelper.Clamp(moonF, 0f, 7f));

            ui.Spacing(4f);
            ui.Separator();
            dirty |= ui.Checkbox(L.Get("Settings.BloodMoon", "Blood Moon"), ref _config.BloodMoon);
            dirty |= ui.Checkbox(L.Get("Settings.PumpkinMoon", "Pumpkin Moon"), ref _config.PumpkinMoon);
            dirty |= ui.Checkbox(L.Get("Settings.FrostMoon", "Frost Moon"), ref _config.FrostMoon);
            dirty |= ui.Checkbox(L.Get("Settings.LanternNight", "Lantern Night"), ref _config.LanternNight);

            ui.Spacing(6f);
            if (ui.Button(L.Get("Settings.ApplyNow", "Apply")))
            {
                SaveConfig();
                TryApplyLive(syncNetwork: true, chat: true);
            }
            ui.SameLine();
            if (ui.Button(L.Get("Settings.ClearNow", "Clear")))
            {
                _config.Preset = WeatherPreset.Clear;
                _config.WindSpeed = 0f;
                _config.BloodMoon = false;
                _config.PumpkinMoon = false;
                _config.FrostMoon = false;
                dirty = true;
                SaveConfig();
                TryApplyLive(syncNetwork: true, chat: true);
            }

            if (dirty)
            {
                SaveConfig();
                if (_active && _config.Enabled)
                    TryApplyLive(syncNetwork: true, chat: false);
                else if (_active)
                    WeatherApplier.UpdateLock(_config);
            }
        }

        private void DrawPresetRow(IImmediateModeUi ui, ref bool dirty)
        {
            dirty |= PresetButton(ui, "Clear", WeatherPreset.Clear);
            ui.SameLine();
            dirty |= PresetButton(ui, "Cloudy", WeatherPreset.Cloudy);
            ui.SameLine();
            dirty |= PresetButton(ui, "Light", WeatherPreset.LightRain);
            ui.SameLine();
            dirty |= PresetButton(ui, "Rain", WeatherPreset.Rain);

            dirty |= PresetButton(ui, "Heavy", WeatherPreset.HeavyRain);
            ui.SameLine();
            dirty |= PresetButton(ui, "Storm", WeatherPreset.Storm);
            ui.SameLine();
            dirty |= PresetButton(ui, "Blizzard", WeatherPreset.Blizzard);
            ui.SameLine();
            dirty |= PresetButton(ui, "Windy", WeatherPreset.Windy);

            dirty |= PresetButton(ui, "Sand", WeatherPreset.Sandstorm);
            ui.SameLine();
            dirty |= PresetButton(ui, "Slime", WeatherPreset.SlimeRain);
            ui.SameLine();
            dirty |= PresetButton(ui, "—", WeatherPreset.Unchanged);
        }

        private bool PresetButton(IImmediateModeUi ui, string label, WeatherPreset preset)
        {
            var tag = _config.Preset == preset ? "[" + label + "]" : label;
            if (!ui.Button(tag))
                return false;
            _config.Preset = preset;
            return true;
        }

        private void TryApplyLive(bool syncNetwork, bool chat)
        {
            WeatherApplier.Bind(_weather, _config, _ctx?.Log);
            if (!_active)
            {
                if (chat)
                {
                    try { Main.NewText(_ctx.L.Get("Chat.NotActive", "Enter SP/Host first."), 255, 180, 100); }
                    catch { /* ignore */ }
                }
                return;
            }

            WeatherApplier.Apply(_config, syncNetwork);
            if (chat)
            {
                try
                {
                    Main.NewText(
                        _ctx.L.Get("Chat.Applied", "Weather: ") + WeatherApplier.DescribeCurrent(),
                        120, 200, 255);
                }
                catch { /* ignore */ }
            }
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(Path.Combine(_ctx.ConfigDirectory, "WeatherControl.json"));
                WeatherApplier.Bind(_weather, _config, _ctx.Log);
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("WeatherControl save failed", ex);
            }
        }

        private static IWeatherService ResolveWeather(IModContext context)
        {
            if (context?.Authority?.Weather != null)
                return context.Authority.Weather;

            IWeatherService weather;
            if (context?.Services != null && context.Services.TryGetService(out weather) && weather != null)
                return weather;

            return null;
        }
    }
}
