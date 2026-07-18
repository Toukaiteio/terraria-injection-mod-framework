using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoHeal
{
    internal sealed class AutoHealConfig
    {
        public bool AutoHeal = true;
        public bool AutoMana = true;

        // Trigger when current / max is at or below this ratio (0.01–1.0).
        // 0.5 = "low health/mana" at half; 1.0 = top up whenever anything is missing.
        public float HealBelowPercent = 0.5f;
        public float ManaBelowPercent = 0.5f;

        public static AutoHealConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new AutoHealConfig();
                c.Save(path);
                return c;
            }

            var cfg = new AutoHealConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.AutoHeal = ReadBool(t, "AutoHeal", cfg.AutoHeal);
                cfg.AutoMana = ReadBool(t, "AutoMana", cfg.AutoMana);
                cfg.HealBelowPercent = Clamp01(ReadFloat(t, "HealBelowPercent", cfg.HealBelowPercent));
                cfg.ManaBelowPercent = Clamp01(ReadFloat(t, "ManaBelowPercent", cfg.ManaBelowPercent));
            }
            catch
            {
                // keep defaults
            }

            return cfg;
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"AutoHeal\": " + B(AutoHeal) + ",");
            sb.AppendLine("  \"AutoMana\": " + B(AutoMana) + ",");
            sb.AppendLine("  \"HealBelowPercent\": " + HealBelowPercent.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"ManaBelowPercent\": " + ManaBelowPercent.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static float Clamp01(float v)
        {
            if (v < 0.01f) return 0.01f;
            if (v > 1f) return 1f;
            return v;
        }

        private static string B(bool b) => b ? "true" : "false";

        private static bool ReadBool(string json, string key, bool fallback)
        {
            var token = "\"" + key + "\"";
            var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return fallback;
            i = json.IndexOf(':', i);
            if (i < 0) return fallback;
            var j = i + 1;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            if (j >= json.Length) return fallback;
            var end = j;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r')
                end++;
            var s = json.Substring(j, end - j).Trim().Trim(',').Trim();
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static float ReadFloat(string json, string key, float fallback)
        {
            var token = "\"" + key + "\"";
            var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return fallback;
            i = json.IndexOf(':', i);
            if (i < 0) return fallback;
            var j = i + 1;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            if (j >= json.Length) return fallback;
            var end = j;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r')
                end++;
            var s = json.Substring(j, end - j).Trim().Trim(',').Trim();
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                return v;
            return fallback;
        }
    }
}
