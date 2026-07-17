using Microsoft.Xna.Framework;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Client-side TIMF mod entry point. Implement this on a public class in your mod DLL.
    /// </summary>
    public interface IMod
    {
        string Name { get; }
        string Version { get; }

        void Load(IModContext context);
        void Unload();

        /// <summary>
        /// Called each frame after the game finishes drawing (Main.OnPostDraw).
        /// Safe place for screen-space overlays.
        /// </summary>
        void PostDraw(GameTime gameTime);
    }
}
