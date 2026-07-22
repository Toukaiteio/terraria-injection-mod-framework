using Microsoft.Xna.Framework;

namespace TIMF.Abstractions
{
    /// <summary>
    /// TIMF mod entry point. Implement this on a public class in your mod DLL. Use [TimfMod(Side=...)] for Client/Server/Both.
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
        /// Safe place for screen-space overlays and immediate-mode UI widgets.
        /// </summary>
        void PostDraw(GameTime gameTime);
    }
}
