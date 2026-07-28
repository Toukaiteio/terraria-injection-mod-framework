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

        /// <summary>Register an NPC definition with a public parameterless constructor.</summary>
        void AddNpc<TNpc>() where TNpc : TimfNpc, new();
        /// <summary>Register an already-constructed NPC definition.</summary>
        void AddNpc(TimfNpc npc);
        /// <summary>Register a runtime biome predicate.</summary>
        void AddBiome<TBiome>() where TBiome : TimfBiome, new();
        /// <summary>Register an already-constructed runtime biome predicate.</summary>
        void AddBiome(TimfBiome biome);
        /// <summary>Register a projectile definition.</summary>
        void AddProjectile<TProjectile>() where TProjectile : TimfProjectile, new();
        void AddProjectile(TimfProjectile projectile);
        /// <summary>Register a player buff or debuff definition.</summary>
        void AddBuff<TBuff>() where TBuff : TimfBuff, new();
        void AddBuff(TimfBuff buff);
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

        /// <summary>Allocated wall id for a registered definition type, or 0.</summary>
        int WallType<TWall>() where TWall : TimfWall;
        /// <summary>Allocated wall id by stable content key, or 0.</summary>
        int WallType(string contentKey);
        /// <summary>Definition behind an allocated wall id, or null.</summary>
        TimfWall GetWall(int type);
        /// <summary>Allocated NPC id for a registered definition type, or 0.</summary>
        int NpcType<TNpc>() where TNpc : TimfNpc;
        /// <summary>Allocated NPC id by stable content key, or 0.</summary>
        int NpcType(string contentKey);
        /// <summary>Definition behind an allocated NPC id, or null.</summary>
        TimfNpc GetNpc(int type);
        /// <summary>Evaluate a registered biome for the current local SceneMetrics.</summary>
        bool IsBiomeActive<TBiome>(Terraria.Player player) where TBiome : TimfBiome;
        int ProjectileType<TProjectile>() where TProjectile : TimfProjectile;
        int ProjectileType(string contentKey);
        TimfProjectile GetProjectile(int type);
        int BuffType<TBuff>() where TBuff : TimfBuff;
        int BuffType(string contentKey);
        TimfBuff GetBuff(int type);

        /// <summary>First item id above the vanilla range. Ids below this are vanilla.</summary>
        int VanillaItemCount { get; }

        /// <summary>First count of vanilla tile ids observed before TIMF expands the arrays.</summary>
        int VanillaTileCount { get; }

        int VanillaWallCount { get; }
        int VanillaNpcCount { get; }
        int VanillaProjectileCount { get; }
        int VanillaBuffCount { get; }

        /// <summary>Every registered definition, in allocation order.</summary>
        System.Collections.Generic.IReadOnlyList<TimfItem> RegisteredItems { get; }

        /// <summary>Every registered tile definition, in allocation order.</summary>
        System.Collections.Generic.IReadOnlyList<TimfTile> RegisteredTiles { get; }

        System.Collections.Generic.IReadOnlyList<TimfWall> RegisteredWalls { get; }
        System.Collections.Generic.IReadOnlyList<TimfNpc> RegisteredNpcs { get; }
        System.Collections.Generic.IReadOnlyList<TimfBiome> RegisteredBiomes { get; }
        System.Collections.Generic.IReadOnlyList<TimfProjectile> RegisteredProjectiles { get; }
        System.Collections.Generic.IReadOnlyList<TimfBuff> RegisteredBuffs { get; }

        /// <summary>
        /// Human-readable state of the content subsystem: id range, how many vanilla arrays
        /// were grown, texture results. Written for diagnosing a live game, since most of this
        /// can only be observed from inside a running Terraria.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<string> Report();
    }
}
