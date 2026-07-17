using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;

namespace HighLight
{
    /// <summary>
    /// JSON config for HighLight (hand-rolled parser, same style as BossCursor).
    /// </summary>
    internal sealed class HighLightConfig
    {
        public bool Enabled = true;
        public string ToggleKey = "P";

        public int CircleR = 255;
        public int CircleG;
        public int CircleB;
        public int CircleA = 255;

        public float Opacity = 0.9f;
        public float CircleScale = 1f;
        public float VelocityLineLengthMultiplier = 25f;
        public bool UseMaxScreenLengthForLine;
        public float VelocityLineThicknessMultiplier = 0.8f;
        public int MaxVelocityLineThickness = 6;
        public bool FadeLineEnds = true;

        /// <summary>1 = every frame; 2 = original HighLight interval.</summary>
        public int DrawEveryNFrames = 1;

        public Color CircleColor
        {
            get
            {
                return new Color(
                    ClampByte(CircleR),
                    ClampByte(CircleG),
                    ClampByte(CircleB),
                    ClampByte(CircleA));
            }
        }

        public static HighLightConfig LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var c = new HighLightConfig();
                c.Save(path);
                return c;
            }

            var cfg = new HighLightConfig();
            try
            {
                var text = File.ReadAllText(path);
                cfg.Enabled = ReadBool(text, "Enabled", cfg.Enabled);
                cfg.ToggleKey = ReadString(text, "ToggleKey", cfg.ToggleKey);
                cfg.CircleR = ReadInt(text, "CircleR", cfg.CircleR);
                cfg.CircleG = ReadInt(text, "CircleG", cfg.CircleG);
                cfg.CircleB = ReadInt(text, "CircleB", cfg.CircleB);
                cfg.CircleA = ReadInt(text, "CircleA", cfg.CircleA);
                cfg.Opacity = ReadFloat(text, "Opacity", cfg.Opacity);
                cfg.CircleScale = ReadFloat(text, "CircleScale", cfg.CircleScale);
                cfg.VelocityLineLengthMultiplier = ReadFloat(text, "VelocityLineLengthMultiplier", cfg.VelocityLineLengthMultiplier);
                cfg.UseMaxScreenLengthForLine = ReadBool(text, "UseMaxScreenLengthForLine", cfg.UseMaxScreenLengthForLine);
                cfg.VelocityLineThicknessMultiplier = ReadFloat(text, "VelocityLineThicknessMultiplier", cfg.VelocityLineThicknessMultiplier);
                cfg.MaxVelocityLineThickness = ReadInt(text, "MaxVelocityLineThickness", cfg.MaxVelocityLineThickness);
                cfg.FadeLineEnds = ReadBool(text, "FadeLineEnds", cfg.FadeLineEnds);
                cfg.DrawEveryNFrames = Math.Max(1, ReadInt(text, "DrawEveryNFrames", cfg.DrawEveryNFrames));
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
            sb.AppendLine("  \"Enabled\": " + (Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"ToggleKey\": \"" + Escape(ToggleKey ?? "P") + "\",");
            sb.AppendLine("  \"CircleR\": " + CircleR + ",");
            sb.AppendLine("  \"CircleG\": " + CircleG + ",");
            sb.AppendLine("  \"CircleB\": " + CircleB + ",");
            sb.AppendLine("  \"CircleA\": " + CircleA + ",");
            sb.AppendLine("  \"Opacity\": " + Opacity.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"CircleScale\": " + CircleScale.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"VelocityLineLengthMultiplier\": " + VelocityLineLengthMultiplier.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"UseMaxScreenLengthForLine\": " + (UseMaxScreenLengthForLine ? "true" : "false") + ",");
            sb.AppendLine("  \"VelocityLineThicknessMultiplier\": " + VelocityLineThicknessMultiplier.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"MaxVelocityLineThickness\": " + MaxVelocityLineThickness + ",");
            sb.AppendLine("  \"FadeLineEnds\": " + (FadeLineEnds ? "true" : "false") + ",");
            sb.AppendLine("  \"DrawEveryNFrames\": " + DrawEveryNFrames);
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
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
