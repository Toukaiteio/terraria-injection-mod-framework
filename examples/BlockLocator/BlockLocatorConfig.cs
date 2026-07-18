using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlockLocator
{
    internal sealed class BlockLocatorConfig
    {
        public bool Enabled = true;
        public string ToggleKey = "OemCloseBrackets"; // ] key

        /// <summary>Tile ids to locate. Default: chests(21) and ore-ish common targets are user-set.</summary>
        public List<int> TargetTileTypes = new List<int> { 21 }; // 21 = Chest

        public int SearchRadiusTiles = 120;  // half-extent around player, in tiles
        public int RescanEveryFrames = 15;   // throttle the tile scan
        public float ArrowDistance = 72f;    // ring radius around player (screen px)
        public float ArrowSize = 1.0f;
        public bool HideWhenOnScreen = false;

        public static BlockLocatorConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new BlockLocatorConfig();
                c.Save(path);
                return c;
            }

            var cfg = new BlockLocatorConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.ToggleKey = ReadString(t, "ToggleKey", cfg.ToggleKey);
                cfg.SearchRadiusTiles = ReadInt(t, "SearchRadiusTiles", cfg.SearchRadiusTiles);
                cfg.RescanEveryFrames = Math.Max(1, ReadInt(t, "RescanEveryFrames", cfg.RescanEveryFrames));
                cfg.ArrowDistance = ReadFloat(t, "ArrowDistance", cfg.ArrowDistance);
                cfg.ArrowSize = ReadFloat(t, "ArrowSize", cfg.ArrowSize);
                cfg.HideWhenOnScreen = ReadBool(t, "HideWhenOnScreen", cfg.HideWhenOnScreen);
                var list = ReadIntList(t, "TargetTileTypes");
                if (list != null)
                    cfg.TargetTileTypes = list;
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
            sb.AppendLine("  \"ToggleKey\": \"" + Escape(ToggleKey ?? "OemCloseBrackets") + "\",");
            sb.AppendLine("  \"TargetTileTypes\": [" + string.Join(", ", TargetTileTypes) + "],");
            sb.AppendLine("  \"SearchRadiusTiles\": " + SearchRadiusTiles + ",");
            sb.AppendLine("  \"RescanEveryFrames\": " + RescanEveryFrames + ",");
            sb.AppendLine("  \"ArrowDistance\": " + ArrowDistance.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"ArrowSize\": " + ArrowSize.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"HideWhenOnScreen\": " + B(HideWhenOnScreen));
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

        private static int ReadInt(string json, string key, int fallback)
        {
            var s = ReadRaw(json, key);
            if (s == null) return fallback;
            s = s.Trim().Trim(',').Trim().Trim('"');
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static float ReadFloat(string json, string key, float fallback)
        {
            var s = ReadRaw(json, key);
            if (s == null) return fallback;
            s = s.Trim().Trim(',').Trim().Trim('"');
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static List<int> ReadIntList(string json, string key)
        {
            var token = "\"" + key + "\"";
            var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var lb = json.IndexOf('[', i);
            if (lb < 0) return null;
            var rb = json.IndexOf(']', lb);
            if (rb < 0) return null;

            var inner = json.Substring(lb + 1, rb - lb - 1);
            var list = new List<int>();
            foreach (var part in inner.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    list.Add(v);
            }
            return list;
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
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r' && json[end] != ']')
                end++;
            return json.Substring(j, end - j);
        }
    }
}
