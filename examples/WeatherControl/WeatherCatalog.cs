using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Terraria;
using TIMF.Abstractions;

namespace WeatherControl
{
    /// <summary>
    /// Discovers weather-related Main/event fields and Start/Stop APIs via reflection
    /// so the UI can list everything the game exposes (and survive minor API renames).
    /// TIMF is vanilla-only, so this won't find tModLoader weather mods — but it covers
    /// all stock atmosphere systems and any future fields on the same types.
    /// </summary>
    internal sealed class WeatherCatalog
    {
        public struct Entry
        {
            public string Group;
            public string Name;
            public string Kind; // field / method / property
            public string Detail;
        }

        private readonly ILogger _log;
        private readonly List<Entry> _entries = new List<Entry>();
        private bool _built;

        public WeatherCatalog(ILogger log)
        {
            _log = log;
        }

        public IReadOnlyList<Entry> Entries => _entries;
        public bool IsBuilt => _built;

        public void EnsureBuilt()
        {
            if (_built)
                return;
            _built = true;
            try
            {
                ScanType(typeof(Main), "Main");
                ScanTypeByName("Terraria.GameContent.Events.Sandstorm", "Sandstorm");
                ScanTypeByName("Terraria.GameContent.Events.LanternNight", "LanternNight");
                ScanTypeByName("Terraria.GameContent.Events.BirthdayParty", "BirthdayParty");
                _entries.Sort((a, b) =>
                {
                    var c = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                _log?.Info("WeatherCatalog: " + _entries.Count + " atmosphere APIs discovered");
            }
            catch (Exception ex)
            {
                _log?.Error("WeatherCatalog build failed", ex);
            }
        }

        private void ScanTypeByName(string fullName, string group)
        {
            try
            {
                var t = typeof(Main).Assembly.GetType(fullName);
                if (t != null)
                    ScanType(t, group);
            }
            catch { /* ignore */ }
        }

        private void ScanType(Type t, string group)
        {
            if (t == null)
                return;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            foreach (var f in t.GetFields(flags))
            {
                if (!IsWeatherName(f.Name))
                    continue;
                _entries.Add(new Entry
                {
                    Group = group,
                    Name = f.Name,
                    Kind = "field",
                    Detail = f.FieldType.Name + (f.IsLiteral ? " const" : ""),
                });
            }

            foreach (var p in t.GetProperties(flags))
            {
                if (!IsWeatherName(p.Name))
                    continue;
                _entries.Add(new Entry
                {
                    Group = group,
                    Name = p.Name,
                    Kind = "prop",
                    Detail = p.PropertyType.Name,
                });
            }

            foreach (var m in t.GetMethods(flags))
            {
                if (m.IsSpecialName || m.DeclaringType != t)
                    continue;
                if (!IsWeatherMethod(m.Name))
                    continue;
                var ps = m.GetParameters();
                var sb = new StringBuilder();
                sb.Append(m.ReturnType.Name).Append('(');
                for (var i = 0; i < ps.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ps[i].ParameterType.Name);
                }
                sb.Append(')');
                _entries.Add(new Entry
                {
                    Group = group,
                    Name = m.Name,
                    Kind = "method",
                    Detail = sb.ToString(),
                });
            }
        }

        private static bool IsWeatherName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            return Contains(n, "rain") || Contains(n, "wind") || Contains(n, "cloud")
                || Contains(n, "storm") || Contains(n, "sand") || Contains(n, "snow")
                || Contains(n, "moon") || Contains(n, "slime") || Contains(n, "lantern")
                || Contains(n, "blizzard") || Contains(n, "weather");
        }

        private static bool IsWeatherMethod(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            if (n.StartsWith("get_", StringComparison.Ordinal) || n.StartsWith("set_", StringComparison.Ordinal))
                return false;
            return Contains(n, "Rain") || Contains(n, "Wind") || Contains(n, "Cloud")
                || Contains(n, "Storm") || Contains(n, "Sand") || Contains(n, "Slime")
                || Contains(n, "Lantern") || Contains(n, "Weather") || Contains(n, "Moon");
        }

        private static bool Contains(string hay, string needle)
        {
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
