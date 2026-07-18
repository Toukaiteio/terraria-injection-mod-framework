using Microsoft.Xna.Framework;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional frame driver for UI libraries. Core calls NewFrame before mod PostDraw
    /// and Render after all mod PostDraw, so widgets built during PostDraw are flushed correctly.
    /// </summary>
    public interface IUiHost
    {
        void NewFrame(GameTime gameTime);
        void Render();

        /// <summary>
        /// Block vanilla mouse click-through for TIMF windows that were open last frame.
        /// Must run before the game consumes the click (e.g. DrawMenu Prefix / early Update).
        /// Draw-time capture alone is too late on the main menu.
        /// </summary>
        void EarlyBlockGameInput();
    }
}
