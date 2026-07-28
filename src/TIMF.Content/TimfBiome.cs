using Terraria;

namespace TIMF.Content
{
    /// <summary>Runtime biome definition. Biome membership is derived and never saved as an id.</summary>
    public abstract class TimfBiome
    {
        public string ModId { get; internal set; }
        public virtual string InternalName => GetType().Name;
        public string ContentKey => ModId + "/" + InternalName;
        public virtual string DisplayName => InternalName;

        /// <summary>Evaluate from player position and the current SceneMetrics tile counts.</summary>
        public abstract bool IsActive(Player player, SceneMetrics sceneMetrics, IContentLookup content);

        /// <summary>Called once when a player enters this biome.</summary>
        public virtual void OnEnter(Player player) { }

        /// <summary>Called once when a player leaves this biome.</summary>
        public virtual void OnLeave(Player player) { }

        /// <summary>Called after Terraria updates an active player's biome state.</summary>
        public virtual void Update(Player player) { }
    }
}
