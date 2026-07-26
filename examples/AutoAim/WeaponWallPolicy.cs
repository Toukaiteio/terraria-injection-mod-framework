using System;
using Terraria;
using Terraria.ID;
using TIMF.Abstractions;

namespace AutoAim
{
    /// <summary>
    /// Whether the held weapon's shot can travel through solid tiles for AutoAim LOS.
    ///
    /// Conservative by design (false positives made "ignore all walls"):
    /// - Melee swing projectiles: tileCollide=false but solid NPCs still need CanHit.
    /// - Whips: ProjectileID.Sets.IsAWhip, chain proj ignores tiles, still need LOS.
    /// - Minion staves: shoot spawns a pet, not a through-wall bolt.
    /// Only magic/ranged (and noMelee proj weapons) with default tileCollide=false qualify.
    /// </summary>
    internal sealed class WeaponWallPolicy
    {
        private readonly ILogger _log;
        private int _cachedItemType = -1;
        private int _cachedShoot = int.MinValue;
        private bool _cachedPasses;
        private Projectile _probe;

        public WeaponWallPolicy(ILogger log)
        {
            _log = log;
        }

        public bool HeldWeaponPassesThroughWalls(Player player)
        {
            if (player == null)
                return false;

            Item item;
            try { item = player.HeldItem; }
            catch { return false; }

            if (item == null || item.IsAir)
                return false;

            var type = item.type;
            var shoot = item.shoot;
            if (type == _cachedItemType && shoot == _cachedShoot)
                return _cachedPasses;

            _cachedItemType = type;
            _cachedShoot = shoot;
            _cachedPasses = Compute(item);
            return _cachedPasses;
        }

        private bool Compute(Item item)
        {
            if (item.shoot <= 0)
                return false;

            // Whips first — summon + IsAWhip (Leather Whip, Morning Star, Kaleidoscope, …).
            if (IsWhipShoot(item.shoot))
                return false;

            // Minion / pet staves: shoot is the minion, not a fired bolt.
            if (IsMinionOrPetSpawn(item))
                return false;

            // Contact melee (swords…): swing projs ignore tiles; solid NPCs need CanHit.
            if (IsContactMelee(item))
                return false;

            // Summon class left after whip/minion exclusion should not skip LOS.
            try
            {
                if (item.summon)
                    return false;
            }
            catch { /* ignore */ }

            // Only flying-shot weapons (magic / ranged / noMelee proj).
            if (!IsFlyingShotWeapon(item))
                return false;

            try
            {
                if (_probe == null)
                    _probe = new Projectile();

                _probe.SetDefaults(0);
                _probe.SetDefaults(item.shoot);

                if (IsWhipShoot(_probe.type) || IsMinionProjectile(_probe))
                    return false;

                // Bullets/arrows: tileCollide=true → need LOS.
                // Lasers / many magic bolts: tileCollide=false → skip LOS.
                return !_probe.tileCollide;
            }
            catch (Exception ex)
            {
                _log?.Error("WeaponWallPolicy probe failed for shoot=" + item.shoot, ex);
                return false;
            }
        }

        private static bool IsWhipShoot(int shoot)
        {
            try
            {
                return shoot > 0
                    && ProjectileID.Sets.IsAWhip != null
                    && shoot < ProjectileID.Sets.IsAWhip.Length
                    && ProjectileID.Sets.IsAWhip[shoot];
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMinionOrPetSpawn(Item item)
        {
            try
            {
                if (item.shoot <= 0)
                    return false;

                // Typical minion staff: summon + buff + not a whip.
                if (item.summon && item.buffType > 0 && !IsWhipShoot(item.shoot))
                    return true;

                if (Main.projPet != null
                    && item.shoot < Main.projPet.Length
                    && Main.projPet[item.shoot])
                    return true;
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool IsMinionProjectile(Projectile p)
        {
            try
            {
                if (p == null)
                    return false;
                if (p.minion)
                    return true;
                if (Main.projPet != null
                    && p.type > 0
                    && p.type < Main.projPet.Length
                    && Main.projPet[p.type])
                    return true;
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool IsContactMelee(Item item)
        {
            try
            {
                if (item.noMelee)
                    return false;
                if (item.melee && !item.magic && !item.ranged && !item.summon)
                    return true;
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool IsFlyingShotWeapon(Item item)
        {
            try
            {
                if (item.magic || item.ranged)
                    return true;
                // Sword beams / pure proj "melee" with contact disabled.
                if (item.noMelee && item.shoot > 0 && !item.summon)
                    return true;
            }
            catch { /* ignore */ }

            return false;
        }

        public void Invalidate()
        {
            _cachedItemType = -1;
            _cachedShoot = int.MinValue;
            _cachedPasses = false;
        }
    }
}
