using System;
using System.Text;
using TIMF.Abstractions.Storage;

namespace IHaveMyPhoneAnyway
{
    internal sealed class PhoneConfig
    {
        public bool Enabled = true;

        public bool Clock = true;
        public bool PositionAndDepth = true;
        public bool Weather = true;
        public bool Fishing = true;
        public bool MoonAndEvents = true;
        public bool RareCreatures = true;
        public bool Detection = true;
        public bool Movement = true;

        public static PhoneConfig LoadOrCreate(IModStorage storage, string name)
        {
            if (!storage.ConfigExists(name))
            {
                var c = new PhoneConfig();
                c.Save(storage, name);
                return c;
            }

            var cfg = new PhoneConfig();
            try
            {
                var t = storage.ReadConfigText(name);
                cfg.Enabled = ReadBool(t, "Enabled", cfg.Enabled);
                cfg.Clock = ReadBool(t, "Clock", cfg.Clock);
                cfg.PositionAndDepth = ReadBool(t, "PositionAndDepth", cfg.PositionAndDepth);
                cfg.Weather = ReadBool(t, "Weather", cfg.Weather);
                cfg.Fishing = ReadBool(t, "Fishing", cfg.Fishing);
                cfg.MoonAndEvents = ReadBool(t, "MoonAndEvents", cfg.MoonAndEvents);
                cfg.RareCreatures = ReadBool(t, "RareCreatures", cfg.RareCreatures);
                cfg.Detection = ReadBool(t, "Detection", cfg.Detection);
                cfg.Movement = ReadBool(t, "Movement", cfg.Movement);
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
            sb.AppendLine("  \"Enabled\": " + B(Enabled) + ",");
            sb.AppendLine("  \"Clock\": " + B(Clock) + ",");
            sb.AppendLine("  \"PositionAndDepth\": " + B(PositionAndDepth) + ",");
            sb.AppendLine("  \"Weather\": " + B(Weather) + ",");
            sb.AppendLine("  \"Fishing\": " + B(Fishing) + ",");
            sb.AppendLine("  \"MoonAndEvents\": " + B(MoonAndEvents) + ",");
            sb.AppendLine("  \"RareCreatures\": " + B(RareCreatures) + ",");
            sb.AppendLine("  \"Detection\": " + B(Detection) + ",");
            sb.AppendLine("  \"Movement\": " + B(Movement));
            sb.AppendLine("}");
            storage.WriteConfigText(name, sb.ToString());
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
