using Microsoft.Xna.Framework;

namespace TIMF.Abstractions
{
    /// <summary>
    /// TIMF mod entry point. Implement a public class in your mod DLL.
    ///
    /// Prefer capability markers for automatic side classification:
    /// <list type="bullet">
    /// <item><see cref="IClientMod"/> — client UI / local hooks</item>
    /// <item><see cref="IAuthorityMod"/> — world authority (handshake Server)</item>
    /// <item><see cref="IVanillaPlugin"/> — vanilla-join-compatible host plugin</item>
    /// </list>
    /// Optional <see cref="TimfModAttribute"/> can pin id / side / dependencies.
    /// </summary>
    public interface IMod
    {
        /// <summary>Display name and default dependency id.</summary>
        string Name { get; }

        string Version { get; }

        void Load(IModContext context);
        void Unload();

        /// <summary>
        /// Called each frame after the game finishes drawing (Main.OnPostDraw).
        /// No-op on dedicated servers. Prefer implementing this only on <see cref="IClientMod"/>.
        /// </summary>
        void PostDraw(GameTime gameTime);
    }
}
