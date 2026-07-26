using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace WeatherControl
{
    /// <summary>
    /// Lists weather channels registered with the framework <see cref="IWeatherService"/>.
    /// This is the stable discovery path for host UIs (vanilla + any plugin-registered channels).
    /// </summary>
    internal sealed class WeatherCatalog
    {
        public struct Entry
        {
            public string Group;
            public string Name;
            public string Kind;
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

        public void EnsureBuilt(IWeatherService weather)
        {
            if (_built)
                return;
            _built = true;
            try
            {
                if (weather == null)
                {
                    _log?.Warn("WeatherCatalog: IWeatherService unavailable");
                    return;
                }

                foreach (var ch in weather.Channels)
                {
                    if (ch == null)
                        continue;
                    _entries.Add(new Entry
                    {
                        Group = ch.Category.ToString(),
                        Name = ch.Id,
                        Kind = ch.ValueKind.ToString(),
                        Detail = FormatChannelDetail(ch),
                    });
                }

                _entries.Sort((a, b) =>
                {
                    var c = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                _log?.Info("WeatherCatalog: " + _entries.Count + " registered weather channels");
            }
            catch (Exception ex)
            {
                _log?.Error("WeatherCatalog build failed", ex);
            }
        }

        /// <summary>Force rebuild after plugins register extra channels mid-session.</summary>
        public void Invalidate()
        {
            _built = false;
            _entries.Clear();
        }

        private static string FormatChannelDetail(IWeatherChannel ch)
        {
            var write = ch.CanWrite ? "rw" : "ro";
            switch (ch.ValueKind)
            {
                case WeatherValueKind.Choice:
                    var choices = ch.Choices != null && ch.Choices.Count > 0
                        ? string.Join("|", ch.Choices)
                        : "";
                    return write + "  " + ch.DisplayName + (choices.Length > 0 ? "  [" + choices + "]" : "");
                case WeatherValueKind.Scalar:
                case WeatherValueKind.Integer:
                    var range = "";
                    if (ch.Min.HasValue || ch.Max.HasValue)
                        range = "  " + (ch.Min.HasValue ? ch.Min.Value.ToString() : "") + ".." +
                                (ch.Max.HasValue ? ch.Max.Value.ToString() : "");
                    return write + "  " + ch.DisplayName + range;
                default:
                    return write + "  " + ch.DisplayName;
            }
        }
    }
}
