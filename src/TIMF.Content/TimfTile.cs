namespace TIMF.Content
{
    /// <summary>
    /// Definition of a custom world tile. One shared instance exists for each definition.
    /// Configure Terraria's id-indexed tile sets from <see cref="SetStaticDefaults"/> after
    /// <see cref="Type"/> has been assigned and all vanilla tile arrays have been expanded.
    /// </summary>
    public abstract class TimfTile
    {
        /// <summary>Allocated tile id. Zero until content registration is finalized.</summary>
        public int Type { get; internal set; }

        /// <summary>Id of the mod that registered this definition.</summary>
        public string ModId { get; internal set; }

        /// <summary>
        /// Stable name inside the owning mod. Renaming it disconnects existing world-sidecar
        /// entries, so treat it as permanent once released.
        /// </summary>
        public virtual string InternalName => GetType().Name;

        /// <summary>Content key: <c>ModId/InternalName</c>.</summary>
        public string ContentKey => ModId + "/" + InternalName;

        /// <summary>Name used by diagnostics and content lookup.</summary>
        public virtual string DisplayName => InternalName;

        /// <summary>
        /// Texture path relative to the mod directory, without extension. Tile sheets use
        /// vanilla's 16-pixel cells and may contain normal frame padding where appropriate.
        /// </summary>
        public virtual string Texture => "Content/" + InternalName;

        /// <summary>
        /// Item type dropped when this tile is successfully destroyed. Return zero (the
        /// default) for no drop. The value may be either a vanilla item id or the allocated
        /// <see cref="TimfItem.Type"/> of a custom item.
        /// </summary>
        public virtual int ItemDrop => 0;

        /// <summary>Number of <see cref="ItemDrop"/> items produced per destroyed tile.</summary>
        public virtual int ItemDropStack => 1;

        /// <summary>
        /// Vanilla tile whose TileObjectData placement anchors should be cloned for this tile.
        /// Use -1 (default) for an ordinary single-cell block. For example, return TileID.Torches
        /// to obtain floor, side, and wall torch anchors while retaining this tile's own id.
        /// </summary>
        public virtual int PlacementTemplateTile => -1;

        /// <summary>
        /// Add light emitted by this tile. Components normally range from 0 to 1. The framework
        /// combines them with ambient/vanilla light using the brightest component.
        /// </summary>
        public virtual void ModifyLight(int i, int j, ref float red, ref float green, ref float blue) { }

        /// <summary>
        /// Called once after id allocation and tile-array expansion. Set entries such as
        /// <c>Main.tileSolid[Type]</c>, <c>Main.tileFrameImportant[Type]</c>, and
        /// <c>Main.tileLighted[Type]</c> here.
        /// </summary>
        public virtual void SetStaticDefaults() { }
    }
}
