using System.Collections.Generic;

namespace TIMF.Abstractions
{
    /// <summary>
    /// One controllable / readable weather facet registered with <see cref="IWeatherService"/>.
    /// Vanilla channels are registered by Core; plugins may add more with unique ids.
    /// </summary>
    public interface IWeatherChannel
    {
        /// <summary>Stable id, e.g. <c>vanilla.atmosphere.preset</c>, <c>vanilla.wind.speed</c>.</summary>
        string Id { get; }

        /// <summary>UI label (English default; mods may localize via their own catalogs).</summary>
        string DisplayName { get; }

        WeatherCategory Category { get; }

        WeatherValueKind ValueKind { get; }

        /// <summary>For <see cref="WeatherValueKind.Choice"/> — allowed string values.</summary>
        IReadOnlyList<string> Choices { get; }

        /// <summary>For scalars: inclusive min (null = unbounded).</summary>
        float? Min { get; }

        /// <summary>For scalars: inclusive max (null = unbounded).</summary>
        float? Max { get; }

        /// <summary>Whether <see cref="TryWrite"/> is allowed on the authority process.</summary>
        bool CanWrite { get; }

        WeatherValue Read();

        bool TryWrite(WeatherValue value, WeatherSetOptions options, out string error);
    }
}
