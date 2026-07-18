using System;
using System.IO;
using System.Text;

namespace AutoFishing
{
    internal sealed class AutoFishingConfig
    {
        public bool Enabled = true;
        public string ToggleKey = "OemBackslash";

        public bool ShowMessages = true;
        public bool ShowIcons = true;
        public bool ShareToChat = false;

        /// <summary>Frames to wait after a cast before allowing the next auto action.</summary>
        public int RecastDelay = 8;

        public static AutoFishingConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new AutoFishingConfig();
                c.Save(path);
                return c;
            }

            var cfg = new AutoFishingConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.ToggleKey = ReadString(t, "ToggleKey", cfg.ToggleKey);
                cfg.ShowMessages = ReadBool(t, "ShowMessages", cfg.ShowMessages);
                cfg.ShowIcons = ReadBool(t, "ShowIcons", cfg.ShowIcons);
                cfg.ShareToChat = ReadBool(t, "ShareToChat", cfg.ShareToChat);
                cfg.RecastDelay = Math.Max(1, ReadInt(t, "RecastDelay", cfg.RecastDelay));
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
            sb.AppendLine("  \"ToggleKey\": \"" + Escape(ToggleKey ?? "OemBackslash") + "\",");
            sb.AppendLine("  \"ShowMessages\": " + B(ShowMessages) + ",");
            sb.AppendLine("  \"ShowIcons\": " + B(ShowIcons) + ",");
            sb.AppendLine("  \"ShareToChat\": " + B(ShareToChat) + ",");
            sb.AppendLine("  \"RecastDelay\": " + RecastDelay);
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
            return int.TryParse(s, out v) ? v : fallback;
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
