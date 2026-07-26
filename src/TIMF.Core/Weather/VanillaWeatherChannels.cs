using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Events;
using TIMF.Abstractions;

namespace TIMF.Core.Weather
{
    /// <summary>Factory for stock Terraria weather channels.</summary>
    internal static class VanillaWeatherChannels
    {
        public static IEnumerable<IWeatherChannel> CreateAll()
        {
            yield return new AtmospherePresetChannel();
            yield return new BoolFieldChannel(
                WeatherChannelIds.RainActive, "Raining", WeatherCategory.Atmosphere,
                () => Main.raining,
                v =>
                {
                    if (v) AtmospherePresetChannel.ForceRain(0.5f, true);
                    else AtmospherePresetChannel.ForceClearRain(true);
                });
            yield return new ScalarChannel(
                WeatherChannelIds.RainIntensity, "Rain intensity", WeatherCategory.Atmosphere,
                0f, 1f,
                () => Main.maxRaining,
                v =>
                {
                    v = MathHelper.Clamp(v, 0f, 1f);
                    if (v <= 0.001f)
                        AtmospherePresetChannel.ForceClearRain(true);
                    else
                        AtmospherePresetChannel.ForceRain(v, true);
                });
            yield return new BoolFieldChannel(
                WeatherChannelIds.Sandstorm, "Sandstorm", WeatherCategory.Atmosphere,
                () => Sandstorm.Happening,
                v =>
                {
                    if (v)
                    {
                        if (Math.Abs(Main.windSpeedTarget) < 0.6f)
                        {
                            Main.windSpeedTarget = 0.8f;
                            Main.windSpeedCurrent = 0.8f;
                        }
                        InvokeSandstorm("StartSandstorm");
                    }
                    else
                        InvokeSandstorm("StopSandstorm");
                });
            yield return new BoolFieldChannel(
                WeatherChannelIds.SlimeRain, "Slime rain", WeatherCategory.Atmosphere,
                () => Main.slimeRain,
                v =>
                {
                    if (v) Main.StartSlimeRain(true);
                    else if (Main.slimeRain) Main.StopSlimeRain(false);
                });
            yield return new IntegerChannel(
                WeatherChannelIds.CloudCount, "Cloud count", WeatherCategory.Atmosphere,
                0, 200,
                () => Main.numClouds,
                v =>
                {
                    Main.numClouds = v;
                    Main.numCloudsTemp = v;
                    Main.resetClouds = true;
                });

            yield return new ScalarChannel(
                WeatherChannelIds.WindSpeed, "Wind speed", WeatherCategory.Wind,
                -1.5f, 1.5f,
                () => Main.windSpeedTarget,
                v =>
                {
                    Main.windSpeedTarget = v;
                    Main.windSpeedCurrent = v;
                    try { Main.ResetWindCounter(true); } catch { /* optional */ }
                });

            yield return new IntegerChannel(
                WeatherChannelIds.MoonPhase, "Moon phase", WeatherCategory.Moon,
                0, 7,
                () => Main.moonPhase,
                v =>
                {
                    if (v < 0) v = 0;
                    if (v > 7) v = 7;
                    Main.moonPhase = v;
                });

            yield return new BoolFieldChannel(
                WeatherChannelIds.BloodMoon, "Blood Moon", WeatherCategory.Event,
                () => Main.bloodMoon, v => Main.bloodMoon = v);
            yield return new BoolFieldChannel(
                WeatherChannelIds.PumpkinMoon, "Pumpkin Moon", WeatherCategory.Event,
                () => Main.pumpkinMoon,
                v =>
                {
                    Main.pumpkinMoon = v;
                    if (v) Main.snowMoon = false;
                });
            yield return new BoolFieldChannel(
                WeatherChannelIds.FrostMoon, "Frost Moon", WeatherCategory.Event,
                () => Main.snowMoon,
                v =>
                {
                    Main.snowMoon = v;
                    if (v) Main.pumpkinMoon = false;
                });
            yield return new BoolFieldChannel(
                WeatherChannelIds.LanternNight, "Lantern Night", WeatherCategory.Event,
                () =>
                {
                    try
                    {
                        var f = typeof(LanternNight).GetField("ManualLanterns", BindingFlags.Public | BindingFlags.Static);
                        if (f != null) return (bool)f.GetValue(null);
                    }
                    catch { /* ignore */ }
                    return LanternNight.LanternsUp;
                },
                v =>
                {
                    try
                    {
                        var f = typeof(LanternNight).GetField("ManualLanterns", BindingFlags.Public | BindingFlags.Static);
                        if (f != null)
                        {
                            f.SetValue(null, v);
                            return;
                        }
                        if (v != LanternNight.LanternsUp)
                            LanternNight.ToggleManualLanterns();
                    }
                    catch { /* ignore */ }
                });
        }

        private static void InvokeSandstorm(string name)
        {
            var m = typeof(Sandstorm).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            m?.Invoke(null, null);
        }

        // --- Channel implementations ---

        internal sealed class AtmospherePresetChannel : IWeatherChannel
        {
            private static readonly string[] _choices =
            {
                WeatherChannelIds.AtmospherePresets.Clear,
                WeatherChannelIds.AtmospherePresets.Cloudy,
                WeatherChannelIds.AtmospherePresets.LightRain,
                WeatherChannelIds.AtmospherePresets.Rain,
                WeatherChannelIds.AtmospherePresets.HeavyRain,
                WeatherChannelIds.AtmospherePresets.Storm,
                WeatherChannelIds.AtmospherePresets.Blizzard,
                WeatherChannelIds.AtmospherePresets.Sandstorm,
                WeatherChannelIds.AtmospherePresets.Windy,
                WeatherChannelIds.AtmospherePresets.SlimeRain,
            };

            public string Id => WeatherChannelIds.AtmospherePreset;
            public string DisplayName => "Atmosphere preset";
            public WeatherCategory Category => WeatherCategory.Atmosphere;
            public WeatherValueKind ValueKind => WeatherValueKind.Choice;
            public IReadOnlyList<string> Choices => _choices;
            public float? Min => null;
            public float? Max => null;
            public bool CanWrite => true;

            public WeatherValue Read()
            {
                return WeatherValue.FromString(DetectPreset());
            }

            public bool TryWrite(WeatherValue value, WeatherSetOptions options, out string error)
            {
                error = null;
                var key = value.StringValue;
                if (string.IsNullOrEmpty(key))
                {
                    error = "Atmosphere preset requires a string choice.";
                    return false;
                }

                try
                {
                    ApplyPreset(key, options == null || options.Instant, value.FloatValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            private static string DetectPreset()
            {
                if (Main.slimeRain) return WeatherChannelIds.AtmospherePresets.SlimeRain;
                if (Sandstorm.Happening) return WeatherChannelIds.AtmospherePresets.Sandstorm;
                if (Main.raining)
                {
                    if (Main.maxRaining >= 0.85f && Math.Abs(Main.windSpeedTarget) >= 0.7f)
                        return WeatherChannelIds.AtmospherePresets.Storm;
                    if (Main.maxRaining >= 0.85f)
                        return WeatherChannelIds.AtmospherePresets.HeavyRain;
                    if (Main.maxRaining >= 0.4f)
                        return WeatherChannelIds.AtmospherePresets.Rain;
                    return WeatherChannelIds.AtmospherePresets.LightRain;
                }
                if (Math.Abs(Main.windSpeedTarget) >= 0.55f)
                    return WeatherChannelIds.AtmospherePresets.Windy;
                if (Main.numClouds >= 150)
                    return WeatherChannelIds.AtmospherePresets.Cloudy;
                return WeatherChannelIds.AtmospherePresets.Clear;
            }

            private static void ApplyPreset(string key, bool instant, float? intensityOverride)
            {
                switch (key)
                {
                    case WeatherChannelIds.AtmospherePresets.Clear:
                        ClearAtmosphere(instant);
                        SetClouds(40);
                        break;
                    case WeatherChannelIds.AtmospherePresets.Cloudy:
                        ClearAtmosphere(instant);
                        SetClouds(180);
                        Main.cloudAlpha = 0.35f;
                        break;
                    case WeatherChannelIds.AtmospherePresets.LightRain:
                        StopSandstorm();
                        StopSlime();
                        ForceRain(intensityOverride ?? 0.25f, instant);
                        SetClouds(140);
                        break;
                    case WeatherChannelIds.AtmospherePresets.Rain:
                        StopSandstorm();
                        StopSlime();
                        ForceRain(intensityOverride ?? 0.5f, instant);
                        SetClouds(160);
                        break;
                    case WeatherChannelIds.AtmospherePresets.HeavyRain:
                        StopSandstorm();
                        StopSlime();
                        ForceRain(intensityOverride ?? 0.9f, instant);
                        SetClouds(200);
                        break;
                    case WeatherChannelIds.AtmospherePresets.Storm:
                        StopSandstorm();
                        StopSlime();
                        ForceRain(intensityOverride ?? 0.95f, instant);
                        SetClouds(200);
                        SetWind(Math.Abs(Main.windSpeedTarget) >= 0.75f
                            ? Main.windSpeedTarget
                            : 0.85f);
                        break;
                    case WeatherChannelIds.AtmospherePresets.Blizzard:
                        // No global blizzard flag — heavy rain + wind; snow biomes render snow.
                        StopSandstorm();
                        StopSlime();
                        ForceRain(intensityOverride ?? 1f, instant);
                        SetClouds(200);
                        SetWind(0.9f);
                        break;
                    case WeatherChannelIds.AtmospherePresets.Sandstorm:
                        StopSlime();
                        ForceClearRain(instant);
                        SetWind(0.8f);
                        InvokeSandstorm("StartSandstorm");
                        break;
                    case WeatherChannelIds.AtmospherePresets.Windy:
                        ClearAtmosphere(instant);
                        SetClouds(80);
                        SetWind(0.7f);
                        break;
                    case WeatherChannelIds.AtmospherePresets.SlimeRain:
                        StopSandstorm();
                        Main.StartSlimeRain(true);
                        break;
                    default:
                        throw new ArgumentException("Unknown atmosphere preset: " + key);
                }
            }

            private static void ClearAtmosphere(bool instant)
            {
                ForceClearRain(instant);
                StopSandstorm();
                StopSlime();
            }

            /// <summary>Authoritative rain write used by preset + rain channels.</summary>
            internal static void ForceRain(float strength, bool instant)
            {
                strength = MathHelper.Clamp(strength, 0.05f, 1f);
                try
                {
                    // Always write intensity (bypasses StartRain coin-luck / announce path).
                    Main.ChangeRain(instant, strength);
                }
                catch
                {
                    // Fall through to field writes.
                }

                try
                {
                    if (!Main.IsRainingForever && Main.rainTime < 3600 * 4)
                        Main.rainTime = 3600 * 12;
                }
                catch
                {
                    if (Main.rainTime < 3600 * 4)
                        Main.rainTime = 3600 * 12;
                }

                Main.raining = true;
                Main.maxRaining = strength;
                if (instant || Main.cloudAlpha < strength * 0.5f)
                    Main.cloudAlpha = strength;
            }

            internal static void ForceClearRain(bool instant)
            {
                try { Main.StopRain(instant); }
                catch { /* ignore */ }
                Main.raining = false;
                Main.maxRaining = 0f;
                Main.cloudAlpha = 0f;
                Main.rainTime = 0;
            }

            private static void SetWind(float speed)
            {
                Main.windSpeedTarget = speed;
                Main.windSpeedCurrent = speed;
            }

            private static void SetClouds(int n)
            {
                Main.numClouds = n;
                Main.numCloudsTemp = n;
                Main.resetClouds = true;
            }

            private static void StopSandstorm()
            {
                if (Sandstorm.Happening)
                    InvokeSandstorm("StopSandstorm");
            }

            private static void StopSlime()
            {
                if (Main.slimeRain)
                    Main.StopSlimeRain(false);
            }

            private static void InvokeSandstorm(string name)
            {
                var m = typeof(Sandstorm).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                m?.Invoke(null, null);
            }
        }

        private sealed class BoolFieldChannel : IWeatherChannel
        {
            private readonly Func<bool> _read;
            private readonly Action<bool> _write;

            public BoolFieldChannel(string id, string name, WeatherCategory cat, Func<bool> read, Action<bool> write)
            {
                Id = id;
                DisplayName = name;
                Category = cat;
                _read = read;
                _write = write;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public WeatherCategory Category { get; }
            public WeatherValueKind ValueKind => WeatherValueKind.Toggle;
            public IReadOnlyList<string> Choices => Array.Empty<string>();
            public float? Min => null;
            public float? Max => null;
            public bool CanWrite => true;

            public WeatherValue Read() => WeatherValue.FromBool(_read());

            public bool TryWrite(WeatherValue value, WeatherSetOptions options, out string error)
            {
                error = null;
                if (!value.BoolValue.HasValue)
                {
                    error = Id + " requires a bool value.";
                    return false;
                }
                try
                {
                    _write(value.BoolValue.Value);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        private sealed class ScalarChannel : IWeatherChannel
        {
            private readonly Func<float> _read;
            private readonly Action<float> _write;

            public ScalarChannel(string id, string name, WeatherCategory cat, float min, float max, Func<float> read, Action<float> write)
            {
                Id = id;
                DisplayName = name;
                Category = cat;
                Min = min;
                Max = max;
                _read = read;
                _write = write;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public WeatherCategory Category { get; }
            public WeatherValueKind ValueKind => WeatherValueKind.Scalar;
            public IReadOnlyList<string> Choices => Array.Empty<string>();
            public float? Min { get; }
            public float? Max { get; }
            public bool CanWrite => true;

            public WeatherValue Read() => WeatherValue.FromFloat(_read());

            public bool TryWrite(WeatherValue value, WeatherSetOptions options, out string error)
            {
                error = null;
                if (!value.FloatValue.HasValue)
                {
                    error = Id + " requires a float value.";
                    return false;
                }
                try
                {
                    var v = value.FloatValue.Value;
                    if (Min.HasValue) v = Math.Max(Min.Value, v);
                    if (Max.HasValue) v = Math.Min(Max.Value, v);
                    _write(v);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        private sealed class IntegerChannel : IWeatherChannel
        {
            private readonly Func<int> _read;
            private readonly Action<int> _write;

            public IntegerChannel(string id, string name, WeatherCategory cat, int min, int max, Func<int> read, Action<int> write)
            {
                Id = id;
                DisplayName = name;
                Category = cat;
                Min = min;
                Max = max;
                _read = read;
                _write = write;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public WeatherCategory Category { get; }
            public WeatherValueKind ValueKind => WeatherValueKind.Integer;
            public IReadOnlyList<string> Choices => Array.Empty<string>();
            public float? Min { get; }
            public float? Max { get; }
            public bool CanWrite => true;

            public WeatherValue Read() => WeatherValue.FromInt(_read());

            public bool TryWrite(WeatherValue value, WeatherSetOptions options, out string error)
            {
                error = null;
                int v;
                if (value.IntValue.HasValue) v = value.IntValue.Value;
                else if (value.FloatValue.HasValue) v = (int)Math.Round(value.FloatValue.Value);
                else
                {
                    error = Id + " requires an int value.";
                    return false;
                }
                try
                {
                    if (Min.HasValue) v = Math.Max((int)Min.Value, v);
                    if (Max.HasValue) v = Math.Min((int)Max.Value, v);
                    _write(v);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }
    }
}
