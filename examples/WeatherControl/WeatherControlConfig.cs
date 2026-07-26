using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WeatherControl
{
    /// <summary>
    /// Preset atmospheric mode. Snowfall visuals in snow biomes use the rain pipeline
    /// (Blizzard = heavy rain + strong wind; looks like a blizzard in tundra).
    /// </summary>
    internal enum WeatherPreset
    {
        Unchanged = 0,
        Clear = 1,
        Cloudy = 2,
        LightRain = 3,
        Rain = 4,
        HeavyRain = 5,
        Storm = 6,       // heavy rain + strong wind
        Blizzard = 7,    // heavy rain + strong wind (snow biomes render as snow/blizzard)
        Sandstorm = 8,
        SlimeRain = 9,
        Windy = 10,      // clear + strong wind
    }

    internal sealed class WeatherControlConfig
    {
        public bool Enabled = true;
        /// <summary>Re-assert weather after vanilla UpdateWeather (recommended so rain sticks).</summary>
        public bool LockWeather = true;

        public WeatherPreset Preset = WeatherPreset.Unchanged;

        /// <summary>0..7 — Full / WaningGibbous / ThirdQuarter / …</summary>
        public int MoonPhase = 0;

        /// <summary>Target wind in vanilla units (~ -1.2 .. 1.2). Sign = direction.</summary>
        public float WindSpeed = 0f;

        public bool BloodMoon = false;
        public bool PumpkinMoon = false;
        public bool FrostMoon = false;
        public bool LanternNight = false;

        // Legacy JSON gates (read only for migration; UI removed — values always apply).
        public bool ApplyMoonPhase = true;
        public bool ApplyWind = true;
        public bool ApplySpecialEvents = true;

        public static WeatherControlConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new WeatherControlConfig();
                c.Save(path);
                return c;
            }

            var cfg = new WeatherControlConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.LockWeather = ReadBool(t, "LockWeather", cfg.LockWeather);
                cfg.Preset = (WeatherPreset)Clamp(ReadInt(t, "Preset", (int)cfg.Preset), 0, 10);
                cfg.MoonPhase = Clamp(ReadInt(t, "MoonPhase", cfg.MoonPhase), 0, 7);
                cfg.ApplyMoonPhase = ReadBool(t, "ApplyMoonPhase", cfg.ApplyMoonPhase);
                cfg.WindSpeed = ClampF(ReadFloat(t, "WindSpeed", cfg.WindSpeed), -1.5f, 1.5f);
                cfg.ApplyWind = ReadBool(t, "ApplyWind", cfg.ApplyWind);
                cfg.BloodMoon = ReadBool(t, "BloodMoon", cfg.BloodMoon);
                cfg.PumpkinMoon = ReadBool(t, "PumpkinMoon", cfg.PumpkinMoon);
                cfg.FrostMoon = ReadBool(t, "FrostMoon", cfg.FrostMoon);
                cfg.LanternNight = ReadBool(t, "LanternNight", cfg.LanternNight);
                cfg.ApplySpecialEvents = ReadBool(t, "ApplySpecialEvents", cfg.ApplySpecialEvents);
            }
            catch
            {
                // defaults
            }

            return cfg;
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            MoonPhase = Clamp(MoonPhase, 0, 7);
            WindSpeed = ClampF(WindSpeed, -1.5f, 1.5f);
            if ((int)Preset < 0 || (int)Preset > 10)
                Preset = WeatherPreset.Unchanged;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Enabled\": " + B(Enabled) + ",");
            sb.AppendLine("  \"LockWeather\": " + B(LockWeather) + ",");
            sb.AppendLine("  \"Preset\": " + ((int)Preset).ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"MoonPhase\": " + MoonPhase.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"ApplyMoonPhase\": " + B(ApplyMoonPhase) + ",");
            sb.AppendLine("  \"WindSpeed\": " + WindSpeed.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"ApplyWind\": " + B(ApplyWind) + ",");
            sb.AppendLine("  \"BloodMoon\": " + B(BloodMoon) + ",");
            sb.AppendLine("  \"PumpkinMoon\": " + B(PumpkinMoon) + ",");
            sb.AppendLine("  \"FrostMoon\": " + B(FrostMoon) + ",");
            sb.AppendLine("  \"LanternNight\": " + B(LanternNight) + ",");
            sb.AppendLine("  \"ApplySpecialEvents\": " + B(ApplySpecialEvents));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string B(bool b) => b ? "true" : "false";

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static float ClampF(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static bool ReadBool(string json, string key, bool fallback)
        {
            var s = ReadRaw(json, key);
            if (s == null) return fallback;
            s = s.Trim().Trim(',').Trim();
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static float ReadFloat(string json, string key, float fallback)
        {
            var s = ReadRaw(json, key);
            if (s == null) return fallback;
            s = s.Trim().Trim(',').Trim().Trim('"');
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static int ReadInt(string json, string key, int fallback)
        {
            return (int)Math.Round(ReadFloat(json, key, fallback));
        }

        private static string ReadRaw(string json, string key)
        {
            var token = "\"" + key + "\"";
            var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            var j = i + 1;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            if (j >= json.Length) return null;
            if (json[j] == '"')
            {
                var k = json.IndexOf('"', j + 1);
                if (k < 0) return null;
                return json.Substring(j, k - j + 1);
            }
            var end = j;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r')
                end++;
            return json.Substring(j, end - j);
        }
    }
}
