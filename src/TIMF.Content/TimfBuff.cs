using Terraria;

namespace TIMF.Content
{
    /// <summary>Framework-owned player buff or debuff definition.</summary>
    public abstract class TimfBuff
    {
        public int Type { get; internal set; }
        public string ModId { get; internal set; }
        public virtual string InternalName => GetType().Name;
        public string ContentKey => ModId + "/" + InternalName;
        public virtual string DisplayName => InternalName;
        public virtual string Description => "";
        public virtual string Texture => "Content/" + InternalName;
        public virtual bool IsDebuff => false;
        public virtual bool CanBeCleared => !IsDebuff;

        /// <summary>Whether this effect is persisted in the framework player sidecar.</summary>
        public virtual bool Save => true;

        public virtual void SetStaticDefaults() { }

        /// <summary>
        /// Called once per tick while active. The slot is by-ref because removing a buff compacts
        /// the array; decrement it after deleting the current slot.
        /// </summary>
        public virtual void Update(Player player, ref int buffIndex) { }
    }
}
