using Terraria;

namespace TIMF.Content
{
    /// <summary>The vanilla miscellaneous equipment slot occupied by a pet item.</summary>
    public enum TimfPetSlot
    {
        Pet,
        LightPet,
    }

    /// <summary>
    /// Stable item-side API for pets backed by a vanilla pet or light-pet buff. This avoids
    /// allocating unsafe custom buff/projectile ids before those content pipelines exist.
    /// </summary>
    public abstract class TimfPetItem : TimfItem
    {
        /// <summary>The vanilla pet or light-pet buff activated by this item.</summary>
        public abstract int PetBuffType { get; }

        /// <summary>
        /// Selects the original pet equipment slot. The framework publishes the matching
        /// Main.vanityPet/Main.lightPet flag and always writes Item.buffType, so the item can
        /// be shift-equipped and dragged into the same slot as a vanilla pet summon.
        /// </summary>
        public virtual TimfPetSlot PetSlot => TimfPetSlot.Pet;

        /// <summary>
        /// Projectile used to represent the pet in the world. Return zero when the selected
        /// vanilla buff owns all spawning itself. Declaring this value lets the framework
        /// reproduce the original pet-slot behaviour and also supports framework projectiles.
        /// </summary>
        public virtual int PetProjectileType => 0;

        /// <summary>Duration refreshed by one use. Vanilla pet buffs normally refresh themselves.</summary>
        public virtual int PetBuffDuration => 3600;

        public override void OnUseItem(Player player)
        {
            if (player == null || PetBuffType <= 0) return;
            player.AddBuff(PetBuffType, PetBuffDuration);
            OnPetActivated(player);
        }

        /// <summary>Optional one-shot callback after the pet buff has been applied.</summary>
        public virtual void OnPetActivated(Player player) { }
    }
}
