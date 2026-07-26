using System.Collections.Generic;

namespace TIMF.Abstractions
{
    /// <summary>High-level grouping for weather UI and filtering.</summary>
    public enum WeatherCategory
    {
        Atmosphere = 0,
        Wind = 1,
        Moon = 2,
        Event = 3,
        Other = 4,
    }

    /// <summary>How a channel stores its value.</summary>
    public enum WeatherValueKind
    {
        /// <summary>On/off (blood moon, sandstorm happening, raining…).</summary>
        Toggle = 0,

        /// <summary>Continuous scalar (rain intensity 0–1, wind −1.5–1.5).</summary>
        Scalar = 1,

        /// <summary>Integer discrete (moon phase 0–7).</summary>
        Integer = 2,

        /// <summary>Named choice among <see cref="IWeatherChannel.Choices"/>.</summary>
        Choice = 3,
    }

    /// <summary>Portable value for read/write on a weather channel.</summary>
    public struct WeatherValue
    {
        public bool? BoolValue;
        public float? FloatValue;
        public int? IntValue;
        public string StringValue;

        public static WeatherValue FromBool(bool v)
        {
            return new WeatherValue { BoolValue = v };
        }

        public static WeatherValue FromFloat(float v)
        {
            return new WeatherValue { FloatValue = v };
        }

        public static WeatherValue FromInt(int v)
        {
            return new WeatherValue { IntValue = v };
        }

        public static WeatherValue FromString(string v)
        {
            return new WeatherValue { StringValue = v };
        }

        public override string ToString()
        {
            if (BoolValue.HasValue) return BoolValue.Value ? "true" : "false";
            if (FloatValue.HasValue) return FloatValue.Value.ToString("0.###");
            if (IntValue.HasValue) return IntValue.Value.ToString();
            if (!string.IsNullOrEmpty(StringValue)) return StringValue;
            return "";
        }
    }

    /// <summary>Options for a single channel write or a bundle apply.</summary>
    public sealed class WeatherSetOptions
    {
        /// <summary>Skip fade-ins when the channel supports it (e.g. instant rain).</summary>
        public bool Instant = true;

        /// <summary>Broadcast <c>MessageID.WorldData</c> so vanilla clients update.</summary>
        public bool SyncNetwork = true;
    }

    /// <summary>
    /// Composite weather change used by host tools. Null fields mean "leave unchanged".
    /// </summary>
    public sealed class WeatherBundle
    {
        /// <summary>Atmosphere preset channel value, e.g. <c>clear</c>, <c>rain</c>, <c>sandstorm</c>.</summary>
        public string AtmospherePreset;

        /// <summary>Optional rain intensity override 0–1 when starting rain-like presets.</summary>
        public float? RainIntensity;

        public float? WindSpeed;
        public int? MoonPhase;

        /// <summary>
        /// Event channel ids to force on
        /// (e.g. <see cref="WeatherChannelIds.BloodMoon"/> = <c>vanilla.event.blood_moon</c>).
        /// </summary>
        public List<string> EnableEvents = new List<string>();

        /// <summary>
        /// Event channel ids to force off
        /// (e.g. <see cref="WeatherChannelIds.LanternNight"/>).
        /// </summary>
        public List<string> DisableEvents = new List<string>();

        public bool Instant = true;
        public bool SyncNetwork = true;
    }

    /// <summary>Point-in-time world atmosphere snapshot.</summary>
    public sealed class WeatherSnapshot
    {
        public float WindSpeed;
        public int MoonPhase;
        public bool Raining;
        public float RainIntensity;
        public bool Sandstorm;
        public bool SlimeRain;
        public bool BloodMoon;
        public bool PumpkinMoon;
        public bool FrostMoon;
        public bool LanternNight;
        public int CloudCount;

        /// <summary>Per-channel readings keyed by <see cref="IWeatherChannel.Id"/>.</summary>
        public Dictionary<string, WeatherValue> Channels = new Dictionary<string, WeatherValue>();

        /// <summary>Short human-readable summary.</summary>
        public string Summary;
    }
}
