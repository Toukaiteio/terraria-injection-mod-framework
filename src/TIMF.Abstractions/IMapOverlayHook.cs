using Microsoft.Xna.Framework;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Parameters captured from the vanilla map draw pass (MapOverlayDrawContext).
    /// Convert a world position (pixels) to a map-screen position with <see cref="WorldToMap"/>.
    /// </summary>
    public struct MapOverlayInfo
    {
        /// <summary>Top-left tile of the visible map region (tile coords).</summary>
        public Vector2 MapPosition;

        /// <summary>Screen-space offset the map is drawn at (pixels).</summary>
        public Vector2 MapOffset;

        /// <summary>Clip rect for the minimap; null for fullscreen.</summary>
        public Rectangle? ClippingRect;

        /// <summary>Tiles-to-pixels scale on the map.</summary>
        public float MapScale;

        /// <summary>Icon draw scale hint.</summary>
        public float DrawScale;

        /// <summary>Overlay alpha 0..1.</summary>
        public float Alpha;

        /// <summary>True when the fullscreen map is open (else minimap/overlay).</summary>
        public bool Fullscreen;

        /// <summary>
        /// Map a world-space position (pixels) to a position on the map surface (pixels),
        /// matching how vanilla places its own icons.
        /// </summary>
        public Vector2 WorldToMap(Vector2 worldPixels)
        {
            var tile = worldPixels / 16f;
            return (tile - MapPosition) * MapScale + MapOffset;
        }

        /// <summary>True if a map-space point is inside the visible map (respects minimap clip).</summary>
        public bool Contains(Vector2 mapPos)
        {
            if (!ClippingRect.HasValue)
                return true;
            return ClippingRect.Value.Contains((int)mapPos.X, (int)mapPos.Y);
        }
    }

    /// <summary>
    /// Hook invoked while the game draws map icons (fullscreen map, minimap and overlay).
    /// Runs inside the vanilla open SpriteBatch, so draw directly on Main.spriteBatch using the
    /// coordinate transform from <see cref="MapOverlayInfo"/> — do NOT call Begin/End.
    ///
    /// Register into <see cref="IModContext.Services"/> via <see cref="IMapOverlayHookRegistry"/>.
    /// Set <paramref name="hoverText"/> to show a tooltip when the mouse is over your icon.
    /// </summary>
    public interface IMapOverlayHook
    {
        void OnDrawMap(MapOverlayInfo info, ref string hoverText);
    }
}
