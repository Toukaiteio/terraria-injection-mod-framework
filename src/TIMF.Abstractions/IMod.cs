using Microsoft.Xna.Framework;

namespace TIMF.Abstractions
{
    /// <summary>
    /// TIMF mod entry point. Implement a public class in your mod DLL.
    ///
    /// Declare capability markers; the loader infers <see cref="TimfSide"/> from them:
    /// <list type="bullet">
    /// <item><see cref="IClientMod"/> — client UI / local hooks</item>
    /// <item><see cref="IAuthorityMod"/> — world logic; both together give <see cref="TimfSide.Both"/></item>
    /// </list>
    /// Whether joining peers need matching code is the separate <see cref="TimfNetProfile"/>
    /// axis, set via <see cref="TimfModAttribute.Net"/> and defaulting to vanilla-compatible.
    /// <see cref="TimfModAttribute"/> also pins id / dependencies.
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
