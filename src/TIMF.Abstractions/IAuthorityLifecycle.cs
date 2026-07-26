namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional activate / deactivate callbacks for a mod's authority half.
    ///
    /// This is a <em>lifecycle</em> interface, not a capability marker: implementing it does
    /// not make a mod authority-capable, and it never affects <see cref="TimfSide"/> inference.
    /// Declare the capability with <see cref="IAuthorityMod"/>; add this only when you need to
    /// know when the authority half comes and goes. Pure deferred mods can rely on
    /// <see cref="IMod.Load"/> / <see cref="IMod.Unload"/> alone.
    /// </summary>
    public interface IAuthorityLifecycle
    {
        /// <summary>
        /// The session granted authority for this mod: singleplayer, host, dedicated server,
        /// or — for <see cref="TimfNetProfile.Optional"/> / <see cref="TimfNetProfile.Required"/>
        /// mods — a multiplayer client after a successful handshake.
        ///
        /// Being activated is not the same as being authoritative. On a mirrored multiplayer
        /// client this fires while <see cref="IAuthorityServices.IsAuthoritative"/> is false;
        /// gate world writes on that, not on this callback.
        /// </summary>
        void OnAuthorityActivate(IModContext context);

        /// <summary>Leaving the world / disconnect / dedicated shutdown.</summary>
        void OnAuthorityDeactivate();
    }
}
