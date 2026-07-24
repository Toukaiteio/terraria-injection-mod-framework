using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LootRates
{
    internal sealed class LootRatesConfig
    {
        public bool Enabled = true;
        /// <summary>Extra full item-drop rolls after the vanilla NPCLoot_DropItems (0 = vanilla only).</summary>
        public int ExtraItemRolls = 0;
        /// <summary>Coin drop multiplier applied as additional DropMoney calls (1 = vanilla).</summary>
        public float CoinMultiplier = 2f;

        public static LootRatesConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new LootRatesConfig();
                c.Save(path);
                return c;
            }

            var cfg = new LootRatesConfig();
            try
            {
                var t = File.ReadAllText(path);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.ExtraItemRolls = ClampRolls(ReadInt(t, "ExtraItemRolls", cfg.ExtraItemRolls));
                cfg.CoinMultiplier = ClampCoin(ReadFloat(t, "CoinMultiplier", cfg.CoinMultiplier));
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

            ExtraItemRolls = ClampRolls(ExtraItemRolls);
            CoinMultiplier = ClampCoin(CoinMultiplier);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Enabled\": " + (Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"ExtraItemRolls\": " + ExtraItemRolls.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"CoinMultiplier\": " + CoinMultiplier.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static int ClampRolls(int v)
        {
            if (v < 0) return 0;
            if (v > 20) return 20;
            return v;
        }

        public static float ClampCoin(float v)
        {
            if (v < 1f) return 1f;
            if (v > 50f) return 50f;
            return v;
        }

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

        private static int ReadInt(string json, string key, int fallback)
        {
            return (int)Math.Round(ReadFloat(json, key, fallback));
        }
    }
}
