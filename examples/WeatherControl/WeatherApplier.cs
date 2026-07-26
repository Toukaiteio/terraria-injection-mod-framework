using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.Events;
using TIMF.Abstractions;

namespace WeatherControl
{
    /// <summary>
    /// Thin adapter: maps <see cref="WeatherControlConfig"/> onto the framework
    /// <see cref="IWeatherService"/> (stable channel registry + hold/lock + network sync).
    /// </summary>
    internal static class WeatherApplier
    {
        private static IWeatherService _weather;
        private static WeatherControlConfig _config;
        private static ILogger _log;
        private static bool _seededFromWorld;

        public static void Bind(IWeatherService weather, WeatherControlConfig config, ILogger log)
        {
            _weather = weather;
            _config = config;
            _log = log;
        }

        public static void Clear()
        {
            try
            {
                if (_weather != null && _weather.IsLockEnabled)
                    _weather.SetLock(null, false);
            }
            catch { /* ignore */ }

            _weather = null;
            _config = null;
            _log = null;
            _seededFromWorld = false;
        }

        /// <summary>
        /// When legacy configs left ApplyWind/Moon/Events off, fill those fields from the
        /// live world once so always-on apply does not force defaults (full moon, zero wind).
        /// </summary>
        public static void SeedUnsetFromWorld(WeatherControlConfig cfg)
        {
            if (cfg == null || _seededFromWorld)
                return;
            _seededFromWorld = true;

            try
            {
                // Only seed when the legacy gate was false (never intentionally managed).
                if (!cfg.ApplyWind)
                    cfg.WindSpeed = Main.windSpeedTarget;
                if (!cfg.ApplyMoonPhase)
                    cfg.MoonPhase = Main.moonPhase;
                if (!cfg.ApplySpecialEvents)
                {
                    cfg.BloodMoon = Main.bloodMoon;
                    cfg.PumpkinMoon = Main.pumpkinMoon;
                    cfg.FrostMoon = Main.snowMoon;
                    try { cfg.LanternNight = LanternNight.LanternsUp; }
                    catch { cfg.LanternNight = false; }
                }

                // From now on, UI values always participate in apply.
                cfg.ApplyWind = true;
                cfg.ApplyMoonPhase = true;
                cfg.ApplySpecialEvents = true;
            }
            catch (Exception ex)
            {
                _log?.Warn("WeatherControl SeedUnsetFromWorld: " + ex.Message);
            }
        }

        /// <summary>Build a framework bundle from plugin config.</summary>
        public static WeatherBundle ToBundle(WeatherControlConfig cfg)
        {
            if (cfg == null)
                return null;

            var bundle = new WeatherBundle
            {
                Instant = true,
                SyncNetwork = true,
                EnableEvents = new List<string>(),
                DisableEvents = new List<string>(),
            };

            if (cfg.Preset != WeatherPreset.Unchanged)
                bundle.AtmospherePreset = PresetToChannelValue(cfg.Preset);

            // Wind / moon / events always follow the current UI values (no separate "include" gates).
            bundle.WindSpeed = cfg.WindSpeed;
            bundle.MoonPhase = cfg.MoonPhase;
            SetEvent(bundle, WeatherChannelIds.BloodMoon, cfg.BloodMoon);
            SetEvent(bundle, WeatherChannelIds.PumpkinMoon, cfg.PumpkinMoon);
            SetEvent(bundle, WeatherChannelIds.FrostMoon, cfg.FrostMoon);
            SetEvent(bundle, WeatherChannelIds.LanternNight, cfg.LanternNight);

            return bundle;
        }

        public static void Apply(WeatherControlConfig cfg, bool syncNetwork = true)
        {
            if (cfg == null || !cfg.Enabled)
                return;
            if (_weather == null)
            {
                _log?.Warn("WeatherControl: IWeatherService not bound");
                return;
            }

            var bundle = ToBundle(cfg);
            if (bundle == null)
                return;
            bundle.SyncNetwork = syncNetwork;

            string error;
            if (!_weather.TryApplyBundle(bundle, out error))
            {
                if (!string.IsNullOrEmpty(error))
                    _log?.Warn("WeatherControl Apply failed: " + error);
                else
                    _log?.Warn("WeatherControl Apply failed (unknown)");
                return;
            }

            _log?.Info("WeatherControl applied: " + DescribeCurrent()
                       + " preset=" + (bundle.AtmospherePreset ?? "-"));

            UpdateLock(cfg);
        }

        public static void UpdateLock(WeatherControlConfig cfg)
        {
            if (_weather == null || cfg == null)
                return;

            if (cfg.Enabled && cfg.LockWeather)
            {
                var locked = ToBundle(cfg);
                if (locked != null)
                {
                    locked.SyncNetwork = false;
                    _weather.SetLock(locked, true);
                }
            }
            else if (_weather.IsLockEnabled)
            {
                _weather.SetLock(null, false);
            }
        }

        public static string DescribeCurrent()
        {
            if (_weather == null)
                return "?";
            try
            {
                var snap = _weather.Capture();
                return snap != null && !string.IsNullOrEmpty(snap.Summary) ? snap.Summary : "?";
            }
            catch
            {
                return "?";
            }
        }

        public static string PresetToChannelValue(WeatherPreset preset)
        {
            switch (preset)
            {
                case WeatherPreset.Clear: return WeatherChannelIds.AtmospherePresets.Clear;
                case WeatherPreset.Cloudy: return WeatherChannelIds.AtmospherePresets.Cloudy;
                case WeatherPreset.LightRain: return WeatherChannelIds.AtmospherePresets.LightRain;
                case WeatherPreset.Rain: return WeatherChannelIds.AtmospherePresets.Rain;
                case WeatherPreset.HeavyRain: return WeatherChannelIds.AtmospherePresets.HeavyRain;
                case WeatherPreset.Storm: return WeatherChannelIds.AtmospherePresets.Storm;
                case WeatherPreset.Blizzard: return WeatherChannelIds.AtmospherePresets.Blizzard;
                case WeatherPreset.Sandstorm: return WeatherChannelIds.AtmospherePresets.Sandstorm;
                case WeatherPreset.SlimeRain: return WeatherChannelIds.AtmospherePresets.SlimeRain;
                case WeatherPreset.Windy: return WeatherChannelIds.AtmospherePresets.Windy;
                default: return null;
            }
        }

        private static void SetEvent(WeatherBundle bundle, string channelId, bool enable)
        {
            if (enable)
                bundle.EnableEvents.Add(channelId);
            else
                bundle.DisableEvents.Add(channelId);
        }
    }
}
