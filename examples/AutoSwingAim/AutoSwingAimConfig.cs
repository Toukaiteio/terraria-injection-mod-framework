using System;
using System.IO;
using System.Text;

namespace AutoSwingAim
{
    internal sealed class AutoSwingAimConfig
    {
        public bool Enabled = true;

        // Face mouse on every frame while holding attack (smoother mid-swing turn).
        // If false, only re-face when a swing starts / between auto-reuse swings.
        public bool ContinuousTurn = true;

        // Also force useTurn-like mid-swing turning for A/D movement while auto-swinging.
        public bool AllowMoveTurnWhileSwinging = true;

        public static AutoSwingAimConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new AutoSwingAimConfig();
                c.Save(path);
                return c;
            }

            var cfg = new AutoSwingAimConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.ContinuousTurn = ReadBool(t, "ContinuousTurn", cfg.ContinuousTurn);
                cfg.AllowMoveTurnWhileSwinging = ReadBool(t, "AllowMoveTurnWhileSwinging", cfg.AllowMoveTurnWhileSwinging);
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
            sb.AppendLine("  \"ContinuousTurn\": " + B(ContinuousTurn) + ",");
            sb.AppendLine("  \"AllowMoveTurnWhileSwinging\": " + B(AllowMoveTurnWhileSwinging));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
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
    }
}
