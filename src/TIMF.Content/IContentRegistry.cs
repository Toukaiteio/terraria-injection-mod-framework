namespace TIMF.Content
{
    /// <summary>
    /// Registration surface handed to <see cref="IContentMod.AddContent"/>.
    /// </summary>
    public interface IContentRegistry
    {
        /// <summary>Id of the mod currently registering. Used to namespace content keys.</summary>
        string ModId { get; }

        /// <summary>
        /// Register an item definition. <typeparamref name="TItem"/> must have a public
        /// parameterless constructor; the framework creates one shared instance.
        /// </summary>
        void AddItem<TItem>() where TItem : TimfItem, new();

        /// <summary>Register an already-constructed item definition.</summary>
        void AddItem(TimfItem item);

        /// <summary>Register a tile definition with a public parameterless constructor.</summary>
        void AddTile<TTile>() where TTile : TimfTile, new();

        /// <summary>Register an already-constructed tile definition.</summary>
        void AddTile(TimfTile tile);

        /// <summary>Register a wall definition with a public parameterless constructor.</summary>
        void AddWall<TWall>() where TWall : TimfWall, new();

        /// <summary>Register an already-constructed wall definition.</summary>
        void AddWall(TimfWall wall);
    }

    /// <summary>
    /// Look-up of allocated content ids. Resolve from
    /// <c>IModContext.Services</c> after load; ids are not assigned during
    /// <see cref="IContentMod.AddContent"/>.
    /// </summary>
    public interface IContentLookup
    {
        /// <summary>
        /// Allocated item id for a registered definition type, or 0 when unknown.
        /// Use as you would a vanilla <c>ItemID</c> value.
        /// </summary>
        int ItemType<TItem>() where TItem : TimfItem;

        /// <summary>Allocated item id by content key (<c>ModId/InternalName</c>), or 0.</summary>
        int ItemType(string contentKey);

        /// <summary>Definition behind an allocated id, or null when the id is not ours.</summary>
        TimfItem GetItem(int type);

        /// <summary>Allocated tile id for a registered definition type, or 0.</summary>
        int TileType<TTile>() where TTile : TimfTile;

        /// <summary>Allocated tile id by content key (<c>ModId/InternalName</c>), or 0.</summary>
        int TileType(string contentKey);

        /// <summary>Definition behind an allocated tile id, or null.</summary>
        TimfTile GetTile(int type);

        int WallType<TWall>() where TWall : TimfWall;
        int WallType(string contentKey);
        TimfWall GetWall(int type);

        /// <summary>First item id above the vanilla range. Ids below this are vanilla.</summary>
        int VanillaItemCount { get; }

        /// <summary>First count of vanilla tile ids observed before TIMF expands the arrays.</summary>
        int VanillaTileCount { get; }

        int VanillaWallCount { get; }

        /// <summary>Every registered definition, in allocation order.</summary>
        System.Collections.Generic.IReadOnlyList<TimfItem> RegisteredItems { get; }

        /// <summary>Every registered tile definition, in allocation order.</summary>
        System.Collections.Generic.IReadOnlyList<TimfTile> RegisteredTiles { get; }

        System.Collections.Generic.IReadOnlyList<TimfWall> RegisteredWalls { get; }

        /// <summary>
        /// Human-readable state of the content subsystem: id range, how many vanilla arrays
        /// were grown, texture results. Written for diagnosing a live game, since most of this
        /// can only be observed from inside a running Terraria.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<string> Report();
    }
}
