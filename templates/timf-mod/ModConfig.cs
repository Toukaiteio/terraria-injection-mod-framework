using System;
using System.Text;
using TIMF.Abstractions.Storage;

namespace MyMod
{
    /// <summary>
    /// Minimal JSON configuration persisted through the framework's confined
    /// <see cref="IModStorage"/>. It is hand-rolled on purpose: the mod needs no extra dependency
    /// and stays clean under TIMF's security audit (no direct System.IO). Add fields as you grow,
    /// mirroring the read/write pattern below.
    /// </summary>
    internal sealed class ModConfig
    {
        public bool Enabled = true;
        public string ToggleKey = "N";

        public static ModConfig LoadOrCreate(IModStorage storage, string name)
        {
            if (!storage.ConfigExists(name))
            {
                var created = new ModConfig();
                created.Save(storage, name);
                return created;
            }

            var cfg = new ModConfig();
            try
            {
                var text = storage.ReadConfigText(name);
                cfg.Enabled = ReadBool(text, "Enabled", cfg.Enabled);
                cfg.ToggleKey = ReadString(text, "ToggleKey", cfg.ToggleKey);
            }
            catch
            {
                // A corrupt config file falls back to safe defaults instead of failing the load.
            }
            return cfg;
        }

        public void Save(IModStorage storage, string name)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Enabled\": " + (Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"ToggleKey\": \"" + Escape(ToggleKey ?? "N") + "\"");
            sb.AppendLine("}");
            storage.WriteConfigText(name, sb.ToString());
        }

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static bool ReadBool(string json, string key, bool fallback)
        {
            var raw = ReadRaw(json, key);
            if (raw == null) return fallback;
            raw = raw.Trim().Trim(',').Trim();
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
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
            var end = j;
            while (end < json.Length && json[end] != ',' && json[end] != '}' &&
                   json[end] != '\n' && json[end] != '\r')
                end++;
            return json.Substring(j, end - j);
        }
    }
}
