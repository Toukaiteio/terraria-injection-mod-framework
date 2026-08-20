using System;
using System.Globalization;
using System.Text;
using TIMF.Abstractions.Storage;

namespace BossCursor
{
    internal sealed class BossCursorConfig
    {
        public bool Enabled = true;
        public float CursorSize = 1.0f;
        public float CursorDistance = 64f;
        public bool HideOnScreen = false;
        public bool BlackListPillars = false;
        /// <summary>Draw the boss head icon next to the arrow.</summary>
        public bool ShowHead = true;
        /// <summary>Multiplier applied to the distance-scaled boss head icon.</summary>
        public float HeadSize = 1.0f;
        /// <summary>XNA Keys enum name. Default Insert (F8 is vanilla debug menu).</summary>
        public string ToggleKey = "Insert";

        public static BossCursorConfig LoadOrCreate(IModStorage storage, string name)
        {
            if (!storage.ConfigExists(name))
            {
                var c = new BossCursorConfig();
                c.Save(storage, name);
                return c;
            }

            var cfg = new BossCursorConfig();
            try
            {
                var text = storage.ReadConfigText(name);
                cfg.Enabled = ReadBool(text, "Enabled", cfg.Enabled);
                cfg.CursorSize = ReadFloat(text, "CursorSize", cfg.CursorSize);
                cfg.CursorDistance = ReadFloat(text, "CursorDistance", cfg.CursorDistance);
                cfg.HideOnScreen = ReadBool(text, "HideOnScreen", cfg.HideOnScreen);
                cfg.BlackListPillars = ReadBool(text, "BlackListPillars", cfg.BlackListPillars);
                cfg.ShowHead = ReadBool(text, "ShowHead", cfg.ShowHead);
                cfg.HeadSize = ReadFloat(text, "HeadSize", cfg.HeadSize);
                cfg.ToggleKey = ReadString(text, "ToggleKey", cfg.ToggleKey);
            }
            catch
            {
                // keep defaults
            }
            return cfg;
        }

        public void Save(IModStorage storage, string name)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Enabled\": " + (Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"CursorSize\": " + CursorSize.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"CursorDistance\": " + CursorDistance.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"HideOnScreen\": " + (HideOnScreen ? "true" : "false") + ",");
            sb.AppendLine("  \"BlackListPillars\": " + (BlackListPillars ? "true" : "false") + ",");
            sb.AppendLine("  \"ShowHead\": " + (ShowHead ? "true" : "false") + ",");
            sb.AppendLine("  \"HeadSize\": " + HeadSize.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"ToggleKey\": \"" + (ToggleKey ?? "F8") + "\"");
            sb.AppendLine("}");
            storage.WriteConfigText(name, sb.ToString());
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
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
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
