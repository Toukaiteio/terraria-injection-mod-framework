using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Terraria.Localization;
using Terraria.ModLoader;
using TIMF.Abstractions;

namespace TIMF.Bridge
{
    /// <summary>
    /// Per-mod localization backed by the owning tModLoader mod's packaged
    /// <c>Localization/{culture}.json</c> catalogs (flat "Key": "Value" maps), read via
    /// <see cref="Mod.GetFileBytes"/>. Mirrors the native TIMF ModLocalization semantics:
    /// culture fallback chain (zh-Hans → zh → en-US → en) and Get/Format/Has resolving to the key
    /// itself when no translation is found. Catalogs are loaded lazily and cached per culture.
    /// </summary>
    internal sealed class BridgeLocalization : IModLocalization
    {
        private readonly Mod _owner;
        private readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public BridgeLocalization(Mod owner)
        {
            _owner = owner;
        }

        public string CurrentLanguage
        {
            get
            {
                try { return LanguageManager.Instance.ActiveCulture.Name; }
                catch { return "en-US"; }
            }
        }

        public string Get(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key))
                return fallback ?? "";

            foreach (var culture in CultureChain(CurrentLanguage))
            {
                var table = LoadTable(culture);
                string value;
                if (table != null && table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                    return value;
            }

            return fallback ?? key;
        }

        public string Format(string key, params object[] args)
        {
            var fmt = Get(key, key);
            if (args == null || args.Length == 0)
                return fmt;
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            foreach (var culture in CultureChain(CurrentLanguage))
            {
                var table = LoadTable(culture);
                if (table != null && table.ContainsKey(key))
                    return true;
            }
            return false;
        }

        /// <summary>Lazily read + parse Localization/{culture}.json from the owning .tmod (cached).</summary>
        private Dictionary<string, string> LoadTable(string culture)
        {
            Dictionary<string, string> table;
            if (_tables.TryGetValue(culture, out table))
                return table;
            if (_tried.Contains(culture))
                return null;
            _tried.Add(culture);

            try
            {
                var name = "Localization/" + culture + ".json";
                if (_owner != null && _owner.FileExists(name))
                {
                    var bytes = _owner.GetFileBytes(name);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var text = Encoding.UTF8.GetString(bytes);
                        var map = ParseFlatJsonStrings(text);
                        if (map.Count > 0)
                        {
                            _tables[culture] = map;
                            return map;
                        }
                    }
                }
            }
            catch { /* missing/unreadable catalog: fall through to key echo */ }

            return null;
        }

        /// <summary>Culture fallback chain, e.g. zh-Hans → zh → en-US → en.</summary>
        internal static IEnumerable<string> CultureChain(string culture)
        {
            if (string.IsNullOrEmpty(culture))
                culture = "en-US";

            yield return culture;

            var dash = culture.IndexOf('-');
            if (dash > 0)
            {
                var bas = culture.Substring(0, dash);
                if (!string.Equals(bas, culture, StringComparison.OrdinalIgnoreCase))
                    yield return bas;
            }

            if (!string.Equals(culture, "en-US", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
            {
                yield return "en-US";
                yield return "en";
            }
        }

        /// <summary>
        /// Minimal flat JSON object parser: { "Key": "Value", ... }
        /// Supports escaped quotes and unicode \uXXXX; nested objects/arrays are skipped. BOM-tolerant.
        /// </summary>
        internal static Dictionary<string, string> ParseFlatJsonStrings(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json))
                return result;

            var i = 0;
            while (i < json.Length && json[i] != '{') i++;
            if (i >= json.Length) return result;
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] == '}')
                    break;
                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                string key;
                if (!TryReadString(json, ref i, out key))
                    break;

                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':')
                    break;
                i++;
                SkipWs(json, ref i);

                if (i >= json.Length)
                    break;

                if (json[i] == '"')
                {
                    string val;
                    if (!TryReadString(json, ref i, out val))
                        break;
                    if (!string.IsNullOrEmpty(key))
                        result[key] = val;
                }
                else
                {
                    SkipValue(json, ref i);
                }
            }

            return result;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static bool TryReadString(string s, ref int i, out string value)
        {
            value = null;
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"')
                return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }
                if (c == '\\' && i < s.Length)
                {
                    var e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, null, out code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return false;
        }

        private static void SkipValue(string s, ref int i)
        {
            if (i >= s.Length) return;
            if (s[i] == '{')
            {
                var depth = 0;
                do
                {
                    if (s[i] == '{') depth++;
                    else if (s[i] == '}') depth--;
                    i++;
                } while (i < s.Length && depth > 0);
                return;
            }
            if (s[i] == '[')
            {
                var depth = 0;
                do
                {
                    if (s[i] == '[') depth++;
                    else if (s[i] == ']') depth--;
                    i++;
                } while (i < s.Length && depth > 0);
                return;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != '\n')
                i++;
        }
    }
}
