namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional interface for mods that need an explicit server-logic activate/deactivate
    /// (typically <see cref="TimfSide.Both"/>). Pure <see cref="TimfSide.Server"/> mods
    /// can rely on delayed <see cref="IMod.Load"/> / <see cref="IMod.Unload"/> instead.
    /// </summary>
    public interface IServerMod
    {
        /// <summary>
        /// Called when this session allows server-authoritative logic for this mod
        /// (SP / host / dedicated, or multiplayer client after a successful handshake).
        /// </summary>
        void OnServerActivate(IModContext context);

        /// <summary>Called when leaving the world / disconnecting / dedicated shutdown.</summary>
        void OnServerDeactivate();
    }
}
