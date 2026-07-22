using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    /// <summary>
    /// Persists per-mod enabled flags under config/enabled-mods.json.
    /// Missing entries default to enabled.
    /// </summary>
    internal sealed class ModEnablementStore
    {
        private readonly ILogger _log;
        private readonly string _path;
        private readonly Dictionary<string, bool> _enabled =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public ModEnablementStore(ILogger log, string configDir)
        {
            _log = log;
            _path = Path.Combine(configDir ?? "", "enabled-mods.json");
            Load();
        }

        public bool IsEnabled(string id)
        {
            if (string.IsNullOrEmpty(id))
                return true;
            bool v;
            if (_enabled.TryGetValue(id, out v))
                return v;
            return true;
        }

        public void SetEnabled(string id, bool enabled)
        {
            if (string.IsNullOrEmpty(id))
                return;
            _enabled[id] = enabled;
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                var text = File.ReadAllText(_path);
                // Minimal JSON object parser: "Id": true/false
                var i = 0;
                while (i < text.Length)
                {
                    var q1 = text.IndexOf('"', i);
                    if (q1 < 0) break;
                    var q2 = text.IndexOf('"', q1 + 1);
                    if (q2 < 0) break;
                    var key = text.Substring(q1 + 1, q2 - q1 - 1);
                    var colon = text.IndexOf(':', q2 + 1);
                    if (colon < 0) break;
                    var rest = text.Substring(colon + 1).TrimStart();
                    bool val;
                    if (rest.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                        val = true;
                    else if (rest.StartsWith("false", StringComparison.OrdinalIgnoreCase))
                        val = false;
                    else
                    {
                        i = q2 + 1;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(key))
                        _enabled[key.Trim()] = val;
                    i = colon + 1;
                }

                _log.Info("Mod enablement loaded (" + _enabled.Count + " override(s)) from " + _path);
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to read enabled-mods.json: " + ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                var first = true;
                foreach (var kv in _enabled)
                {
                    if (!first) sb.AppendLine(",");
                    first = false;
                    sb.Append("  \"").Append(Escape(kv.Key)).Append("\": ")
                      .Append(kv.Value ? "true" : "false");
                }

                if (!first) sb.AppendLine();
                sb.AppendLine("}");
                File.WriteAllText(_path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to write enabled-mods.json: " + ex.Message);
            }
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
