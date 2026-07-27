using System;
using System.Globalization;
using System.Text;
using TIMF.Abstractions.Storage;
using Microsoft.Xna.Framework;

namespace LowHealthWarning
{
    internal sealed class LowHealthWarningConfig
    {
        public bool Enabled = true;
        public string ToggleKey = "Home";

        /// <summary>Begin vignette when life / maxLife &lt;= this (0–1).</summary>
        public float ThresholdRatio = 0.25f;

        /// <summary>Vignette reaches full strength at or below this ratio.</summary>
        public float FullStrengthRatio = 0.08f;

        /// <summary>Max border thickness in screen pixels (outer edge only).</summary>
        public float MaxEdgeThickness = 72f;

        /// <summary>Peak edge alpha at full strength (center stays clear).</summary>
        public float MaxOpacity = 0.42f;

        public float PulseSpeed = 2.2f;
        public float PulseAmount = 0.18f;

        /// <summary>Bands used to fade inward so the middle of the screen stays open.</summary>
        public int GradientBands = 12;

        public int ColorR = 220;
        public int ColorG = 20;
        public int ColorB = 20;

        public Color TintColor
        {
            get
            {
                return new Color(ClampByte(ColorR), ClampByte(ColorG), ClampByte(ColorB), 255);
            }
        }

        public static LowHealthWarningConfig LoadOrCreate(IModStorage storage, string name)
        {
            if (!storage.ConfigExists(name))
            {
                var c = new LowHealthWarningConfig();
                c.Save(storage, name);
                return c;
            }

            var cfg = new LowHealthWarningConfig();
            try
            {
                var text = storage.ReadConfigText(name);
                cfg.Enabled = ReadBool(text, "Enabled", cfg.Enabled);
                cfg.ToggleKey = ReadString(text, "ToggleKey", cfg.ToggleKey);
                cfg.ThresholdRatio = ReadFloat(text, "ThresholdRatio", cfg.ThresholdRatio);
                cfg.FullStrengthRatio = ReadFloat(text, "FullStrengthRatio", cfg.FullStrengthRatio);
                cfg.MaxEdgeThickness = ReadFloat(text, "MaxEdgeThickness", cfg.MaxEdgeThickness);
                cfg.MaxOpacity = ReadFloat(text, "MaxOpacity", cfg.MaxOpacity);
                cfg.PulseSpeed = ReadFloat(text, "PulseSpeed", cfg.PulseSpeed);
                cfg.PulseAmount = ReadFloat(text, "PulseAmount", cfg.PulseAmount);
                cfg.GradientBands = Math.Max(2, ReadInt(text, "GradientBands", cfg.GradientBands));
                cfg.ColorR = ReadInt(text, "ColorR", cfg.ColorR);
                cfg.ColorG = ReadInt(text, "ColorG", cfg.ColorG);
                cfg.ColorB = ReadInt(text, "ColorB", cfg.ColorB);
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
            sb.AppendLine("  \"ToggleKey\": \"" + Escape(ToggleKey ?? "Home") + "\",");
            sb.AppendLine("  \"ThresholdRatio\": " + ThresholdRatio.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"FullStrengthRatio\": " + FullStrengthRatio.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"MaxEdgeThickness\": " + MaxEdgeThickness.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"MaxOpacity\": " + MaxOpacity.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"PulseSpeed\": " + PulseSpeed.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"PulseAmount\": " + PulseAmount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"GradientBands\": " + GradientBands + ",");
            sb.AppendLine("  \"ColorR\": " + ColorR + ",");
            sb.AppendLine("  \"ColorG\": " + ColorG + ",");
            sb.AppendLine("  \"ColorB\": " + ColorB);
            sb.AppendLine("}");
            storage.WriteConfigText(name, sb.ToString());
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static byte ClampByte(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
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

        private static int ReadInt(string json, string key, int fallback)
        {
            var s = ReadRaw(json, key);
            if (s == null) return fallback;
            s = s.Trim().Trim(',').Trim().Trim('"');
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
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
