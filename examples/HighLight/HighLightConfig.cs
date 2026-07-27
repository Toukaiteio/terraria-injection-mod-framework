using System;
using System.Globalization;
using System.Text;
using TIMF.Abstractions.Storage;
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

        /// <summary>true = draw a rectangle snapped to the entity Hitbox (default); false = old circle style.</summary>
        public bool HitboxStyle = true;

        /// <summary>Outline thickness of the hitbox rectangle, in pixels.</summary>
        public int HitboxThickness = 2;

        /// <summary>Fill the hitbox with a faint tint so small boxes stay visible.</summary>
        public bool FillHitbox = true;

        /// <summary>Fill alpha as a fraction of outline alpha (0..1).</summary>
        public float FillOpacity = 0.18f;

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

        public static HighLightConfig LoadOrCreate(IModStorage storage, string name)
        {
            if (!storage.ConfigExists(name))
            {
                var c = new HighLightConfig();
                c.Save(storage, name);
                return c;
            }

            var cfg = new HighLightConfig();
            try
            {
                var text = storage.ReadConfigText(name);
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
                cfg.HitboxStyle = ReadBool(text, "HitboxStyle", cfg.HitboxStyle);
                cfg.HitboxThickness = Math.Max(1, ReadInt(text, "HitboxThickness", cfg.HitboxThickness));
                cfg.FillHitbox = ReadBool(text, "FillHitbox", cfg.FillHitbox);
                cfg.FillOpacity = ReadFloat(text, "FillOpacity", cfg.FillOpacity);
                cfg.DrawEveryNFrames = Math.Max(1, ReadInt(text, "DrawEveryNFrames", cfg.DrawEveryNFrames));
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
            sb.AppendLine("  \"HitboxStyle\": " + (HitboxStyle ? "true" : "false") + ",");
            sb.AppendLine("  \"HitboxThickness\": " + HitboxThickness + ",");
            sb.AppendLine("  \"FillHitbox\": " + (FillHitbox ? "true" : "false") + ",");
            sb.AppendLine("  \"FillOpacity\": " + FillOpacity.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"DrawEveryNFrames\": " + DrawEveryNFrames);
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
