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
    }
}
