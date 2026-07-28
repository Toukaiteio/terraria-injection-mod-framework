using Terraria;

namespace TIMF.Content
{
    /// <summary>Framework-owned custom projectile definition.</summary>
    public abstract class TimfProjectile
    {
        public Projectile Projectile { get; internal set; }
        public int Type { get; internal set; }
        public string ModId { get; internal set; }
        public virtual string InternalName => GetType().Name;
        public string ContentKey => ModId + "/" + InternalName;
        public virtual string DisplayName => InternalName;
        public virtual string Texture => "Content/" + InternalName;
        public virtual int FrameCount => 1;

        /// <summary>Continue through Terraria's aiStyle dispatcher after <see cref="AI"/>.</summary>
        public virtual bool RunVanillaAI => false;

        public virtual void SetStaticDefaults() { }
        public virtual void SetDefaults() { }
        public virtual void AI() { }
        public virtual void OnHitNpc(NPC target) { }
        public virtual void OnHitPlayer(Player target) { }
        public virtual void OnKill() { }
    }
}
