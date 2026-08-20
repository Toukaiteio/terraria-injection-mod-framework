using System;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace WeatherControl
{
    /// <summary>
    /// Vanilla-compatible host plugin: set weather preset, wind, moon phase, and optional lock.
    /// Authority-only (SP / Host / dedicated). Pure vanilla clients receive normal world packets.
    /// </summary>
    [TimfMod(Id = "WeatherControl")]
    public sealed class WeatherControlMod : IAuthorityMod, IModSettings, IAuthorityLifecycle, IModFeatureToggle
    {
        private IModContext _ctx;
        private WeatherControlConfig _config;
        private WeatherCatalog _catalog;
        private bool _active;
        private bool _showCatalog;
        private ITerrariaReflection _reflection;

        public string Name => "Weather Control";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            _reflection = context.Services.GetService<ITerrariaReflection>();
            _config = WeatherControlConfig.LoadOrCreate(context.Storage, "WeatherControl.json");
            _catalog = new WeatherCatalog(context.Log);
            WeatherApplier.Bind(_config, context.Log, _reflection, context.Patches);
            context.Log.Info(
                "WeatherControl plugin Load. Enabled=" + _config.Enabled +
                " Lock=" + _config.LockWeather +
                " Preset=" + _config.Preset);
        }

        public void Unload()
        {
            WeatherApplier.Uninstall();
            _catalog = null;
            _config = null;
            _ctx = null;
            _active = false;
        }

        public void OnAuthorityActivate(IModContext context)
        {
            WeatherApplier.Bind(_config, context.Log ?? _ctx?.Log, _reflection, context.Patches);
            if (context.Authority == null || !context.Authority.IsAuthoritative)
            {
                context.Log.Warn("WeatherControl OnAuthorityActivate skipped — not authoritative");
                return;
            }

            WeatherApplier.InstallLockHook();
            _active = true;

            if (_config != null && _config.Enabled)
                WeatherApplier.Apply(_config, syncNetwork: true);

            context.Log.Info("WeatherControl active. World: " + WeatherApplier.DescribeCurrent());
        }

        public void OnAuthorityDeactivate()
        {
            WeatherApplier.Uninstall();
            _active = false;
            _ctx?.Log.Info("WeatherControl deactivated");
        }

        public void PostDraw(GameTime gameTime)
        {
            // No world draw; settings via hub / own UI not required each frame.
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            if (_config == null)
            {
                ui.TextColored("Config not ready — enter a world to activate the plugin.", new Color(200, 180, 120));
                return;
            }

            var L = _ctx.L;
            var dirty = false;

            ui.TextColored(
                L.Get("Settings.Title", "Host weather control (vanilla-compatible plugin)."),
                new Color(160, 200, 255));
            ui.TextColored(
                _active
                    ? L.Get("Settings.AuthOn", "Authority: ACTIVE")
                    : L.Get("Settings.AuthOff", "Authority: idle (enter SP/Host/dedicated)"),
                _active ? new Color(140, 220, 140) : new Color(160, 160, 160));
            ui.TextColored(
                L.Format("Settings.Live", WeatherApplier.DescribeCurrent()),
                new Color(200, 200, 160));
            ui.Separator();

            dirty |= ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);
            dirty |= ui.Checkbox(
                L.Get("Settings.Lock", "Lock weather (re-apply every weather tick)"),
                ref _config.LockWeather);

            ui.Spacing(4f);
            ui.TextColored(L.Get("Settings.PresetHeader", "Weather preset"), new Color(255, 230, 160));
            DrawPresetRow(ui, ref dirty);

            ui.Spacing(6f);
            ui.Separator();
            ui.TextColored(L.Get("Settings.WindHeader", "Wind"), new Color(255, 230, 160));
            dirty |= ui.Checkbox(L.Get("Settings.ApplyWind", "Apply wind when applying"), ref _config.ApplyWind);
            dirty |= ui.SliderFloat(L.Get("Settings.WindSpeed", "Wind speed (− = west, + = east)"), ref _config.WindSpeed, -1.2f, 1.2f);
            if (ui.Button(L.Get("Settings.WindCalm", "Calm")))
            {
                _config.WindSpeed = 0f;
                _config.ApplyWind = true;
                dirty = true;
            }
            ui.SameLine();
            if (ui.Button(L.Get("Settings.WindWest", "Strong West")))
            {
                _config.WindSpeed = -0.85f;
                _config.ApplyWind = true;
                dirty = true;
            }
            ui.SameLine();
            if (ui.Button(L.Get("Settings.WindEast", "Strong East")))
            {
                _config.WindSpeed = 0.85f;
                _config.ApplyWind = true;
                dirty = true;
            }

            ui.Spacing(6f);
            ui.Separator();
            ui.TextColored(L.Get("Settings.MoonHeader", "Moon phase"), new Color(255, 230, 160));
            dirty |= ui.Checkbox(L.Get("Settings.ApplyMoon", "Apply moon phase when applying"), ref _config.ApplyMoonPhase);
            var moonF = (float)_config.MoonPhase;
            if (ui.SliderFloat(L.Get("Settings.MoonPhase", "Phase 0–7"), ref moonF, 0f, 7f))
            {
                _config.MoonPhase = (int)Math.Round(moonF);
                dirty = true;
            }
            _config.MoonPhase = (int)Math.Round(MathHelper.Clamp(moonF, 0f, 7f));
            ui.TextColored(
                L.Format("Settings.MoonName", WeatherApplier.MoonPhaseName(_config.MoonPhase)),
                new Color(180, 180, 220));

            ui.Spacing(6f);
            ui.Separator();
            ui.TextColored(L.Get("Settings.SpecialHeader", "Special nights / events"), new Color(255, 230, 160));
            dirty |= ui.Checkbox(L.Get("Settings.ApplySpecial", "Apply special flags when applying"), ref _config.ApplySpecialEvents);
            dirty |= ui.Checkbox(L.Get("Settings.BloodMoon", "Blood Moon"), ref _config.BloodMoon);
            dirty |= ui.Checkbox(L.Get("Settings.PumpkinMoon", "Pumpkin Moon"), ref _config.PumpkinMoon);
            dirty |= ui.Checkbox(L.Get("Settings.FrostMoon", "Frost Moon"), ref _config.FrostMoon);
            dirty |= ui.Checkbox(L.Get("Settings.LanternNight", "Lantern Night (manual)"), ref _config.LanternNight);

            ui.Spacing(8f);
            if (ui.Button(L.Get("Settings.ApplyNow", "Apply now")))
            {
                SaveConfig();
                WeatherApplier.Bind(_config, _ctx.Log, _reflection, _ctx.Patches);
                WeatherApplier.Apply(_config, syncNetwork: true);
                try
                {
                    Main.NewText(_ctx.L.Get("Chat.Applied", "Weather applied: ") + WeatherApplier.DescribeCurrent(), 120, 200, 255);
                }
                catch { /* ignore */ }
            }
            ui.SameLine();
            if (ui.Button(L.Get("Settings.ClearNow", "Force clear")))
            {
                _config.Preset = WeatherPreset.Clear;
                _config.ApplyWind = true;
                _config.WindSpeed = 0f;
                _config.BloodMoon = false;
                _config.PumpkinMoon = false;
                _config.FrostMoon = false;
                dirty = true;
                SaveConfig();
                WeatherApplier.Apply(_config, syncNetwork: true);
            }

            ui.Spacing(8f);
            ui.Separator();
            if (ui.Checkbox(L.Get("Settings.ShowCatalog", "Show discovered atmosphere APIs"), ref _showCatalog))
            {
                // just toggle UI
            }

            if (_showCatalog)
            {
                _catalog.EnsureBuilt();
                ui.TextColored(
                    L.Format("Settings.CatalogCount", _catalog.Entries.Count.ToString()),
                    new Color(150, 150, 150));
                if (ui.BeginChild("wx.catalog", 160f))
                {
                    var list = _catalog.Entries;
                    for (var i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        ui.TextColored(
                            e.Group + "." + e.Name + "  [" + e.Kind + "]  " + e.Detail,
                            new Color(170, 170, 190));
                    }
                }
                ui.EndChild();
            }

            ui.Spacing(4f);
            ui.TextColored(
                L.Get("Settings.Note",
                    "Blizzard uses heavy rain + wind (snow biomes draw snow). Sandstorm needs wind. Vanilla clients sync via WorldData."),
                new Color(150, 150, 150));

            if (dirty)
                SaveConfig();
        }

        private void DrawPresetRow(IImmediateModeUi ui, ref bool dirty)
        {
            // Two rows of preset chips.
            dirty |= PresetButton(ui, "Clear", WeatherPreset.Clear);
            ui.SameLine();
            dirty |= PresetButton(ui, "Cloudy", WeatherPreset.Cloudy);
            ui.SameLine();
            dirty |= PresetButton(ui, "Light rain", WeatherPreset.LightRain);
            ui.SameLine();
            dirty |= PresetButton(ui, "Rain", WeatherPreset.Rain);

            dirty |= PresetButton(ui, "Heavy rain", WeatherPreset.HeavyRain);
            ui.SameLine();
            dirty |= PresetButton(ui, "Storm", WeatherPreset.Storm);
            ui.SameLine();
            dirty |= PresetButton(ui, "Blizzard", WeatherPreset.Blizzard);
            ui.SameLine();
            dirty |= PresetButton(ui, "Windy", WeatherPreset.Windy);

            dirty |= PresetButton(ui, "Sandstorm", WeatherPreset.Sandstorm);
            ui.SameLine();
            dirty |= PresetButton(ui, "Slime rain", WeatherPreset.SlimeRain);
            ui.SameLine();
            dirty |= PresetButton(ui, "Unchanged", WeatherPreset.Unchanged);

            ui.TextColored(
                _ctx.L.Format("Settings.PresetCurrent", _config.Preset.ToString()),
                new Color(180, 220, 180));
        }

        private bool PresetButton(IImmediateModeUi ui, string label, WeatherPreset preset)
        {
            var tag = _config.Preset == preset ? "[" + label + "]" : label;
            if (!ui.Button(tag))
                return false;
            _config.Preset = preset;
            return true;
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
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                _config.Save(_ctx.Storage, "WeatherControl.json");
                WeatherApplier.Bind(_config, _ctx.Log, _reflection, _ctx.Patches);
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("WeatherControl save failed", ex);
            }
        }
    }
}
