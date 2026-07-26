using System.Collections.Generic;
using Terraria;

namespace TIMF.Content
{
    /// <summary>
    /// Definition of a custom item.
    ///
    /// One instance exists per definition type, shared by every in-world item of that type.
    /// <see cref="SetDefaults"/> is invoked with <see cref="Item"/> pointing at whichever
    /// item is currently being initialised, so treat this object as stateless: write to
    /// <see cref="Item"/>, never store per-item state in fields.
    /// </summary>
    public abstract class TimfItem
    {
        /// <summary>
        /// The item being configured. Only meaningful inside <see cref="SetDefaults"/>
        /// and the hooks called from it — never cache it.
        /// </summary>
        public Item Item { get; internal set; }

        /// <summary>Allocated item id. Zero until the framework finishes id allocation.</summary>
        public int Type { get; internal set; }

        /// <summary>Id of the mod that registered this definition.</summary>
        public string ModId { get; internal set; }

        /// <summary>
        /// Name identifying this content within its mod. Combined with <see cref="ModId"/>
        /// it forms the content key that keeps ids stable across launches, so
        /// <b>renaming it invalidates existing saves</b> — treat it as permanent once shipped.
        /// </summary>
        public virtual string InternalName => GetType().Name;

        /// <summary>Content key: <c>ModId/InternalName</c>.</summary>
        public string ContentKey => ModId + "/" + InternalName;

        /// <summary>Name shown in-game. Defaults to <see cref="InternalName"/>.</summary>
        public virtual string DisplayName => InternalName;

        /// <summary>
        /// Texture path relative to the mod's content directory, without extension.
        /// Defaults to <c>Content/&lt;InternalName&gt;</c>; a <c>.png</c> is appended when loading.
        /// </summary>
        public virtual string Texture => "Content/" + InternalName;

        /// <summary>Tooltip lines, or null for none.</summary>
        public virtual IReadOnlyList<string> Tooltip => null;

        /// <summary>
        /// Called once after this definition receives its id — register set entries, research
        /// costs and anything else keyed by item type here.
        /// </summary>
        public virtual void SetStaticDefaults() { }

        /// <summary>
        /// Called once after all items, tiles and walls have received ids and static defaults.
        /// Register recipes here with <see cref="TimfRecipe"/>.
        /// </summary>
        public virtual void AddRecipes() { }

        /// <summary>
        /// Configure <see cref="Item"/>. Called for every item that takes this type, exactly
        /// where vanilla would have filled in its own defaults.
        /// </summary>
        public virtual void SetDefaults() { }

        /// <summary>
        /// Apply this item's effect while it sits in an accessory slot. Called once per tick
        /// per equipped copy, from the same place vanilla applies its own accessory bonuses.
        ///
        /// Write to <paramref name="player"/> — the stat fields are reset every tick, so a
        /// bonus has to be re-applied here rather than added once.
        /// </summary>
        /// <param name="hideVisual">True when the slot is set to hide its visual effect.</param>
        public virtual void UpdateAccessory(Player player, bool hideVisual) { }
    }
}
