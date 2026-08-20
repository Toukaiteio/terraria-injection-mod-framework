using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.Localization;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace WeatherControl
{
    /// <summary>
    /// Applies weather / moon / wind on the authority process and optionally locks it
    /// via a postfix on <c>Main.UpdateWeather</c>.
    /// </summary>
    internal static class WeatherApplier
    {
        private static WeatherControlConfig _config;
        private static ILogger _log;
        private static IModPatchService _patches;
        private static bool _patched;
        private static bool _applying;
        private static int _syncCooldown;
        private static ITerrariaReflection _reflection;

        public static void Bind(WeatherControlConfig config, ILogger log, ITerrariaReflection reflection, IModPatchService patches)
        {
            _config = config;
            _log = log;
            _reflection = reflection;
            _patches = patches;
        }

        public static void InstallLockHook()
        {
            if (_patched)
                return;
            try
            {
                var updateWeather = AccessTools.Method(
                    typeof(Main),
                    "UpdateWeather",
                    new[] { typeof(GameTime), typeof(int) });
                if (updateWeather == null)
                {
                    // Fallback: UpdateTime also mutates rain.
                    updateWeather = AccessTools.Method(typeof(Main), "UpdateTime", Type.EmptyTypes);
                }

                if (updateWeather != null)
                {
                    _patches.PatchPostfix(updateWeather, typeof(WeatherApplier).GetMethod(
                        nameof(OnWeatherTick), BindingFlags.NonPublic | BindingFlags.Static));
                    _patched = true;
                    _log?.Info("WeatherControl lock hook on " + updateWeather.Name);
                }
                else
                    _log?.Error("WeatherControl: UpdateWeather/UpdateTime not found");
            }
            catch (Exception ex)
            {
                _log?.Error("WeatherControl InstallLockHook failed", ex);
            }
        }

        public static void Uninstall()
        {
            try
            {
                _patches?.UnpatchAll();
            }
            catch { /* ignore */ }
            _patched = false;
            _config = null;
            _log = null;
        }

        private static void OnWeatherTick()
        {
            if (_config == null || !_config.Enabled || !_config.LockWeather)
                return;
            if (!IsAuthority())
                return;
            // Re-assert locked state; throttle world-data sync.
            Apply(_config, syncNetwork: false);
            if (++_syncCooldown >= 120)
            {
                _syncCooldown = 0;
                SyncWorldData();
            }
        }

        public static void Apply(WeatherControlConfig cfg, bool syncNetwork = true)
        {
            if (cfg == null || !cfg.Enabled)
                return;
            if (!IsAuthority())
                return;
            if (_applying)
                return;

            _applying = true;
            try
            {
                if (cfg.Preset != WeatherPreset.Unchanged)
                    ApplyPreset(cfg.Preset);

                if (cfg.ApplyWind)
                    ApplyWind(cfg.WindSpeed);

                if (cfg.ApplyMoonPhase)
                    ApplyMoon(cfg.MoonPhase);

                if (cfg.ApplySpecialEvents)
                    ApplySpecial(cfg);

                if (syncNetwork)
                    SyncWorldData();
            }
            catch (Exception ex)
            {
                _log?.Error("WeatherControl Apply failed", ex);
            }
            finally
            {
                _applying = false;
            }
        }

        private static void ApplyPreset(WeatherPreset preset)
        {
            switch (preset)
            {
                case WeatherPreset.Clear:
                    ClearAtmosphere();
                    SetClouds(40);
                    break;

                case WeatherPreset.Cloudy:
                    ClearAtmosphere();
                    SetClouds(180);
                    Main.cloudAlpha = 0.35f;
                    break;

                case WeatherPreset.LightRain:
                    StopSandstormSafe();
                    StopSlimeSafe();
                    StartRainStrength(0.25f);
                    SetClouds(140);
                    break;

                case WeatherPreset.Rain:
                    StopSandstormSafe();
                    StopSlimeSafe();
                    StartRainStrength(0.5f);
                    SetClouds(160);
                    break;

                case WeatherPreset.HeavyRain:
                    StopSandstormSafe();
                    StopSlimeSafe();
                    StartRainStrength(0.9f);
                    SetClouds(200);
                    break;

                case WeatherPreset.Storm:
                    StopSandstormSafe();
                    StopSlimeSafe();
                    StartRainStrength(0.95f);
                    SetClouds(200);
                    ApplyWind(Math.Max(0.75f, Math.Abs(Main.windSpeedTarget)) * (Main.windSpeedTarget < 0 ? -1f : 1f));
                    if (Math.Abs(Main.windSpeedTarget) < 0.75f)
                        ApplyWind(0.85f);
                    break;

                case WeatherPreset.Blizzard:
                    // Vanilla has no global "blizzard" flag — snow biomes render rain as snow.
                    // Heavy rain + strong wind reads as blizzard in tundra/snow.
                    StopSandstormSafe();
                    StopSlimeSafe();
                    StartRainStrength(1f);
                    SetClouds(200);
                    ApplyWind(0.9f);
                    break;

                case WeatherPreset.Sandstorm:
                    StopSlimeSafe();
                    Main.StopRain(true);
                    // Sandstorm requires sufficient wind.
                    ApplyWind(0.8f);
                    InvokeSandstorm("StartSandstorm");
                    break;

                case WeatherPreset.SlimeRain:
                    StopSandstormSafe();
                    try { Main.StartSlimeRain(true); }
                    catch (Exception ex) { _log?.Error("StartSlimeRain failed", ex); }
                    break;

                case WeatherPreset.Windy:
                    ClearAtmosphere();
                    SetClouds(80);
                    ApplyWind(0.7f);
                    break;
            }
        }

        private static void ClearAtmosphere()
        {
            try { Main.StopRain(true); } catch { /* ignore */ }
            StopSandstormSafe();
            StopSlimeSafe();
            Main.raining = false;
            Main.maxRaining = 0f;
            Main.cloudAlpha = 0f;
            Main.rainTime = 0;
        }

        private static void StartRainStrength(float strength)
        {
            strength = MathHelper.Clamp(strength, 0.05f, 1f);
            try
            {
                // StartRain(instant, strengthOverride, coinRain)
                Main.StartRain(true, strength, false);
            }
            catch
            {
                // Older signature fallback via fields.
                Main.raining = true;
                Main.maxRaining = strength;
                Main.cloudAlpha = strength;
                Main.rainTime = 3600 * 6;
            }
            Main.raining = true;
            Main.maxRaining = strength;
            Main.cloudAlpha = strength;
            if (Main.rainTime < 60)
                Main.rainTime = 3600 * 8;
        }

        private static void ApplyWind(float speed)
        {
            speed = MathHelper.Clamp(speed, -1.5f, 1.5f);
            Main.windSpeedTarget = speed;
            Main.windSpeedCurrent = speed;
            try { Main.ResetWindCounter(true); }
            catch { /* optional */ }
        }

        private static void ApplyMoon(int phase)
        {
            if (phase < 0) phase = 0;
            if (phase > 7) phase = 7;
            Main.moonPhase = phase;
        }

        private static void ApplySpecial(WeatherControlConfig cfg)
        {
            Main.bloodMoon = cfg.BloodMoon;
            // Exclusive event moons — only one invasion-style moon at a time.
            if (cfg.PumpkinMoon)
            {
                Main.pumpkinMoon = true;
                Main.snowMoon = false;
            }
            else if (cfg.FrostMoon)
            {
                Main.snowMoon = true;
                Main.pumpkinMoon = false;
            }
            else
            {
                Main.pumpkinMoon = false;
                Main.snowMoon = false;
            }

            try
            {
                // Manual lantern nights.
                if (cfg.LanternNight)
                {
                    var field = typeof(LanternNight).GetField("ManualLanterns", BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                        _reflection.SetFieldValue(field, null, true);
                    else
                        LanternNight.ToggleManualLanterns();
                }
                else
                {
                    var field = typeof(LanternNight).GetField("ManualLanterns", BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                        _reflection.SetFieldValue(field, null, false);
                }
            }
            catch (Exception ex)
            {
                _log?.Error("LanternNight toggle failed", ex);
            }
        }

        private static void SetClouds(int count)
        {
            if (count < 0) count = 0;
            if (count > 200) count = 200;
            Main.numClouds = count;
            Main.numCloudsTemp = count;
            Main.resetClouds = true;
        }

        private static void StopSandstormSafe()
        {
            try
            {
                if (Sandstorm.Happening)
                    InvokeSandstorm("StopSandstorm");
            }
            catch { /* ignore */ }
        }

        private static void InvokeSandstorm(string methodName)
        {
            try
            {
                var m = typeof(Sandstorm).GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null)
                    _reflection.Invoke(m, null, null);
                else
                    _log?.Warn("Sandstorm." + methodName + " not found");
            }
            catch (Exception ex)
            {
                _log?.Error("Sandstorm." + methodName + " failed", ex);
            }
        }

        private static void StopSlimeSafe()
        {
            try
            {
                if (Main.slimeRain)
                    Main.StopSlimeRain(false);
            }
            catch { /* ignore */ }
        }

        private static void SyncWorldData()
        {
            try
            {
                // MessageID.WorldData = 7 — broadcast world flags (rain, moon, time, …).
                if (Main.netMode == 2)
                    NetMessage.SendData(7);
            }
            catch (Exception ex)
            {
                _log?.Error("WeatherControl world sync failed", ex);
            }
        }

        private static bool IsAuthority()
        {
            try
            {
                if (Main.dedServ)
                    return true;
                return Main.netMode != 1;
            }
            catch
            {
                return false;
            }
        }

        public static string DescribeCurrent()
        {
            try
            {
                var rain = Main.raining ? ("rain=" + Main.maxRaining.ToString("0.00")) : "clear";
                var sand = Sandstorm.Happening ? " sandstorm" : "";
                var slime = Main.slimeRain ? " slime" : "";
                var wind = " wind=" + Main.windSpeedTarget.ToString("0.00");
                var moon = " moon=" + Main.moonPhase;
                return rain + sand + slime + wind + moon;
            }
            catch
            {
                return "?";
            }
        }

        public static string MoonPhaseName(int phase)
        {
            switch (phase)
            {
                case 0: return "Full";
                case 1: return "Waning Gibbous";
                case 2: return "Third Quarter";
                case 3: return "Waning Crescent";
                case 4: return "New";
                case 5: return "Waxing Crescent";
                case 6: return "First Quarter";
                case 7: return "Waxing Gibbous";
                default: return "#" + phase;
            }
        }
    }
}
