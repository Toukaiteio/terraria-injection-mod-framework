namespace TIMF.Abstractions
{
    /// <summary>
    /// Capability marker for vanilla-compatible host plugins.
    /// Forces <see cref="TimfSide.Plugin"/>: authority-only, no handshake catalog,
    /// never RequiredOnJoin, never activated on multiplayer clients.
    ///
    /// Implement this for drop rates, economy, and host balance that keep vanilla net packets valid.
    /// </summary>
    public interface IVanillaPlugin : IAuthorityMod
    {
    }
}
