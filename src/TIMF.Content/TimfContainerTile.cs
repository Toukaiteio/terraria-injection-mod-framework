namespace TIMF.Content
{
    /// <summary>
    /// Base class for a chest-like custom tile. The framework persists both the container and
    /// its contents outside the vanilla world file using stable content keys.
    /// </summary>
    public abstract class TimfContainerTile : TimfTile
    {
        /// <summary>Clone the ordinary vanilla chest's 2x2 placement and creation hook.</summary>
        public override int PlacementTemplateTile => Terraria.ID.TileID.Containers;
    }
}
