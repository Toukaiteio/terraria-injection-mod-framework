using System.Collections.Generic;
using Terraria;

namespace TIMF.Content
{
    public sealed class TimfShopEntry
    {
        public int ItemType { get; set; }
        public int Stack { get; set; } = 1;
        public int? CustomPrice { get; set; }
        public System.Func<Player, bool> Condition { get; set; }
    }

    public sealed class TimfQuestReward
    {
        public int ItemType { get; set; }
        public int Stack { get; set; } = 1;
    }

    /// <summary>A buff or debuff applied after an authoritative quest completion.</summary>
    public sealed class TimfQuestStatusEffect
    {
        public int BuffType { get; set; }
        public int Duration { get; set; } = 3600;
    }

    public sealed class TimfDailyQuest
    {
        public string InternalName { get; set; }
        public string Description { get; set; }
        public int RequiredItemType { get; set; }
        public int RequiredStack { get; set; } = 1;
        public IReadOnlyList<TimfQuestReward> Rewards { get; set; }
        public IReadOnlyList<TimfQuestStatusEffect> StatusEffects { get; set; }
    }

    /// <summary>Definition of a framework-owned custom NPC.</summary>
    public abstract class TimfNpc
    {
        public NPC Npc { get; internal set; }
        public int Type { get; internal set; }
        public string ModId { get; internal set; }
        public virtual string InternalName => GetType().Name;
        public string ContentKey => ModId + "/" + InternalName;
        public virtual string DisplayName => InternalName;
        public virtual string Texture => "Content/" + InternalName;
        public virtual int FrameCount => 1;
        public virtual bool IsTownNpc => false;
        /// <summary>
        /// Whether active instances are persisted in the framework world sidecar. Numeric mod
        /// NPC ids are never written to the vanilla .wld file. Town NPCs persist by default.
        /// </summary>
        public virtual bool SaveToWorld => IsTownNpc;
        public virtual bool RunVanillaAI => false;
        public virtual bool RunVanillaFrame => false;

        public virtual void SetStaticDefaults() { }
        public virtual void SetDefaults() { }
        public virtual void AI() { }
        public virtual void FindFrame(int frameHeight) { }
        public virtual string GetChat(Player player) => DisplayName;
        public virtual IReadOnlyList<TimfShopEntry> GetShop(Player player) => null;
        /// <summary>Angler-style daily rotation. Core chooses one entry per world quest-day.</summary>
        public virtual IReadOnlyList<TimfDailyQuest> GetDailyQuests(Player player) => null;
    }
}
