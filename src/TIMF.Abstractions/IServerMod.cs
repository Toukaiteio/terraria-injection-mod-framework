namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional authority lifecycle: explicit activate / deactivate when the session
    /// allows server-side logic. Recommended for <see cref="TimfSide.Both"/> and
    /// useful for <see cref="TimfSide.Server"/> / <see cref="TimfSide.Plugin"/>.
    /// Pure deferred mods may rely on <see cref="IMod.Load"/> / <see cref="IMod.Unload"/> alone.
    /// </summary>
    public interface IServerMod
    {
        /// <summary>
        /// Session allows authority for this mod (SP / host / dedicated;
        /// or multiplayer client after handshake for Server/Both — never for Plugin).
        /// </summary>
        void OnServerActivate(IModContext context);

        /// <summary>Leaving the world / disconnect / dedicated shutdown.</summary>
        void OnServerDeactivate();
    }
}
