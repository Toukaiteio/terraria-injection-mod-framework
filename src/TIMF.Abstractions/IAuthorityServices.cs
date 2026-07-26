namespace TIMF.Abstractions
{
    /// <summary>
    /// World-authority helpers. Always present on the context; check
    /// <see cref="IsAuthoritative"/> before applying server-side effects.
    /// </summary>
    public interface IAuthorityServices
    {
        /// <summary>
        /// True when this process owns the world simulation
        /// (singleplayer, listen-server host, or dedicated server — not a multiplayer client).
        /// </summary>
        bool IsAuthoritative { get; }

        /// <summary>
        /// Unified weather registry / apply API (vanilla channels + optional plugin channels).
        /// Writes should only be performed when <see cref="IsAuthoritative"/> is true.
        /// </summary>
        IWeatherService Weather { get; }

        /// <summary>
        /// Registry of best prefix per item type. Auto-detects vanilla best prefixes;
        /// mods may register overrides for custom items.
        /// </summary>
        IPrefixService Prefix { get; }
    }
}
