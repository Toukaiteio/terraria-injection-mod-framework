using System.Collections.Generic;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Unified weather registry and apply API for the authority process.
    /// Resolve via <see cref="IAuthorityServices.Weather"/> or
    /// <c>context.Services.TryGetService(out IWeatherService weather)</c>.
    ///
    /// Core registers all stock Terraria atmosphere channels. Other TIMF plugins may
    /// <see cref="Register"/> additional channels (stable ids recommended: <c>modid.name</c>).
    /// </summary>
    public interface IWeatherService
    {
        /// <summary>All registered channels (vanilla + plugins), ordered by category then id.</summary>
        IReadOnlyList<IWeatherChannel> Channels { get; }

        /// <summary>Register or replace a channel by <see cref="IWeatherChannel.Id"/>.</summary>
        void Register(IWeatherChannel channel);

        /// <summary>Remove a previously registered channel. Returns false if id unknown.</summary>
        bool Unregister(string id);

        bool TryGet(string id, out IWeatherChannel channel);

        IReadOnlyList<IWeatherChannel> GetByCategory(WeatherCategory category);

        /// <summary>Capture current world atmosphere + all channel readings.</summary>
        WeatherSnapshot Capture();

        /// <summary>Write a single channel (authority only).</summary>
        bool TrySet(string channelId, WeatherValue value, WeatherSetOptions options, out string error);

        /// <summary>Apply a composite change (preset + wind + moon + events).</summary>
        bool TryApplyBundle(WeatherBundle bundle, out string error);

        /// <summary>
        /// While enabled, re-apply <paramref name="bundle"/> after each vanilla weather tick
        /// so the game cannot randomly change rain/wind away from the host setting.
        /// </summary>
        void SetLock(WeatherBundle bundle, bool enabled);

        bool IsLockEnabled { get; }

        /// <summary>Currently locked bundle, or null.</summary>
        WeatherBundle LockedBundle { get; }

        /// <summary>Broadcast world data so vanilla multiplayer clients update visuals.</summary>
        void SyncToClients();
    }

    /// <summary>Well-known vanilla channel ids registered by Core.</summary>
    public static class WeatherChannelIds
    {
        public const string AtmospherePreset = "vanilla.atmosphere.preset";
        public const string RainActive = "vanilla.atmosphere.raining";
        public const string RainIntensity = "vanilla.atmosphere.rain_intensity";
        public const string Sandstorm = "vanilla.atmosphere.sandstorm";
        public const string SlimeRain = "vanilla.atmosphere.slime_rain";
        public const string CloudCount = "vanilla.atmosphere.clouds";

        public const string WindSpeed = "vanilla.wind.speed";

        public const string MoonPhase = "vanilla.moon.phase";
        public const string BloodMoon = "vanilla.event.blood_moon";
        public const string PumpkinMoon = "vanilla.event.pumpkin_moon";
        public const string FrostMoon = "vanilla.event.frost_moon";
        public const string LanternNight = "vanilla.event.lantern_night";

        /// <summary>Choice values for <see cref="AtmospherePreset"/>.</summary>
        public static class AtmospherePresets
        {
            public const string Clear = "clear";
            public const string Cloudy = "cloudy";
            public const string LightRain = "light_rain";
            public const string Rain = "rain";
            public const string HeavyRain = "heavy_rain";
            public const string Storm = "storm";
            public const string Blizzard = "blizzard";
            public const string Sandstorm = "sandstorm";
            public const string Windy = "windy";
            public const string SlimeRain = "slime_rain";
        }
    }
}
