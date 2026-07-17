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
    }
}
