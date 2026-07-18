using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoAim
{
    internal sealed class AutoAimConfig
    {
        public bool Enabled = false;         // off by default — automation opt-in
        public bool AutoClick = true;        // hold attack when a target is found
        public string ToggleKey = "OemTilde"; // ` key

        // Target-type filters (broad categories).
        public bool TargetHostile = true;    // normal enemies
        public bool TargetBosses = true;
        public bool TargetCritters = false;
        public bool TargetTownNpcs = false;

        // If false, target must be reachable (Collision.CanHit line of sight not blocked by tiles).
        // If true, ignore walls entirely.
        public bool IgnoreWalls = false;

        public float Range = 700f;           // pixels

        public static AutoAimConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new AutoAimConfig();
                c.Save(path);
                return c;
            }

            var cfg = new AutoAimConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.AutoClick = ReadBool(t, "AutoClick", cfg.AutoClick);
                cfg.ToggleKey = ReadString(t, "ToggleKey", cfg.ToggleKey);
                cfg.TargetHostile = ReadBool(t, "TargetHostile", cfg.TargetHostile);
                cfg.TargetBosses = ReadBool(t, "TargetBosses", cfg.TargetBosses);
                cfg.TargetCritters = ReadBool(t, "TargetCritters", cfg.TargetCritters);
                cfg.TargetTownNpcs = ReadBool(t, "TargetTownNpcs", cfg.TargetTownNpcs);
                cfg.IgnoreWalls = ReadBool(t, "IgnoreWalls", cfg.IgnoreWalls);
                cfg.Range = ReadFloat(t, "Range", cfg.Range);
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
            sb.AppendLine("  \"Enabled\": " + B(Enabled) + ",");
            sb.AppendLine("  \"AutoClick\": " + B(AutoClick) + ",");
            sb.AppendLine("  \"ToggleKey\": \"" + Escape(ToggleKey ?? "OemTilde") + "\",");
            sb.AppendLine("  \"TargetHostile\": " + B(TargetHostile) + ",");
            sb.AppendLine("  \"TargetBosses\": " + B(TargetBosses) + ",");
            sb.AppendLine("  \"TargetCritters\": " + B(TargetCritters) + ",");
            sb.AppendLine("  \"TargetTownNpcs\": " + B(TargetTownNpcs) + ",");
            sb.AppendLine("  \"IgnoreWalls\": " + B(IgnoreWalls) + ",");
            sb.AppendLine("  \"Range\": " + Range.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string B(bool b) => b ? "true" : "false";
        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

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

        private static string ReadString(string json, string key, string fallback)
        {
            var token = "\"" + key + "\"";
            var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return fallback;
            i = json.IndexOf(':', i);
            if (i < 0) return fallback;
            i = json.IndexOf('"', i + 1);
            if (i < 0) return fallback;
            var j = json.IndexOf('"', i + 1);
            if (j < 0) return fallback;
            return json.Substring(i + 1, j - i - 1);
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
