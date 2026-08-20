using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TIMF.Abstractions;

namespace TIMF.Core.Localization
{
    /// <summary>
    /// Loads flat JSON string maps from a mod's Localization/ folder.
    /// File names: {culture}.json  e.g. en-US.json, zh-Hans.json
    /// </summary>
    internal sealed class ModLocalization : IModLocalization
    {
        private readonly ILogger _log;
        private readonly string _locDir;
        private readonly ILanguageService _lang;
        private readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private string _loadedFor;
        private bool _scanned;

        public ModLocalization(ILogger log, string modDirectory, ILanguageService lang)
        {
            _log = log;
            _lang = lang;
            _locDir = Path.Combine(modDirectory ?? "", "Localization");
            if (_lang != null)
                _lang.LanguageChanged += OnLanguageChanged;
        }

        public string CurrentLanguage
        {
            get { return _lang != null ? _lang.CurrentLanguage : "en-US"; }
        }

        public string Get(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key))
                return fallback ?? "";

            EnsureLoaded();

            string value;
            foreach (var culture in CultureChain(CurrentLanguage))
            {
                Dictionary<string, string> table;
                if (_tables.TryGetValue(culture, out table) && table.TryGetValue(key, out value)
                    && !string.IsNullOrEmpty(value))
                    return value;
            }

            return fallback ?? key;
        }

        public string Format(string key, params object[] args)
        {
            var fmt = Get(key, key);
            if (args == null || args.Length == 0)
                return fmt;
            try
            {
                return string.Format(fmt, args);
            }
            catch
            {
                return fmt;
            }
        }

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            EnsureLoaded();
            foreach (var culture in CultureChain(CurrentLanguage))
            {
                Dictionary<string, string> table;
                if (_tables.TryGetValue(culture, out table) && table.ContainsKey(key))
                    return true;
            }
            return false;
        }

        private void OnLanguageChanged()
        {
            // Tables stay cached; Get() re-resolves via culture chain.
            _loadedFor = CurrentLanguage;
        }

        private void EnsureLoaded()
        {
            if (_scanned)
                return;
            _scanned = true;
            ScanFiles();
            _loadedFor = CurrentLanguage;
        }

        private void ScanFiles()
        {
            try
            {
                if (!Directory.Exists(_locDir))
                    return;

                foreach (var file in Directory.GetFiles(_locDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var culture = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(culture))
                        continue;

                    try
                    {
                        var text = File.ReadAllText(file, Encoding.UTF8);
                        var map = ParseFlatJsonStrings(text);
                        if (map.Count > 0)
                            _tables[culture] = map;
                    }
                    catch (Exception ex)
                    {
            _log?.Warn("Failed to load localization " + Path.GetFileName(file) + ": " + ex.GetType().Name);
                    }
                }
            }
            catch (Exception ex)
            {
            _log?.Warn("Localization scan failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// Culture fallback chain, e.g. zh-Hans → zh → en-US → en.
        /// </summary>
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

            if (!culture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                yield return "en-US";
                yield return "en";
            }
            else if (!string.Equals(culture, "en-US", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
            {
                yield return "en-US";
                yield return "en";
            }
        }

        /// <summary>
        /// Minimal flat JSON object parser: { "Key": "Value", ... }
        /// Supports escaped quotes and unicode \uXXXX. Nested objects ignored.
        /// </summary>
        internal static Dictionary<string, string> ParseFlatJsonStrings(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json))
                return result;

            var i = 0;
            // skip to first {
            while (i < json.Length && json[i] != '{') i++;
            if (i >= json.Length) return result;
            i++; // past {

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
                    // skip non-string values (numbers, bools, nested)
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
                                if (int.TryParse(s.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
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
