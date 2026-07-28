using System;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    internal static class ProjectileContentPatches
    {
        private static ContentManager _content;
        private static ILogger _log;
        [ThreadStatic] private static bool _runningVanillaDefaults;
        [ThreadStatic] private static Projectile _damagingProjectile;

        internal static void Bind(ContentManager content, ILogger log) { _content = content; _log = log; }

        internal static void Install(Harmony harmony, ILogger log)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Projectile), nameof(Projectile.SetDefaults), new[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(BeforeSetDefaults)));
                harmony.Patch(AccessTools.Method(typeof(Projectile), nameof(Projectile.AI), Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(BeforeAI)));
                harmony.Patch(AccessTools.Method(typeof(Projectile), nameof(Projectile.Kill), Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(BeforeKill)));
                var damage = AccessTools.Method(typeof(Projectile), nameof(Projectile.Damage), Type.EmptyTypes);
                harmony.Patch(damage,
                    prefix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(BeforeDamage)),
                    postfix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(AfterDamage)),
                    finalizer: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(DamageFinalizer)));
                // StrikeNPC also returns double and has the same unsafe 32-bit ABI fixup as
                // Player.Hurt. HitEffect is called after StrikeNPC commits damage and is void,
                // so it is the safe point for the custom projectile hit callback.
                var npcHitCommitted = AccessTools.Method(typeof(NPC), nameof(NPC.HitEffect),
                    new[] { typeof(int), typeof(double), typeof(bool) });
                if (npcHitCommitted == null)
                    npcHitCommitted = AccessTools.Method(typeof(NPC), nameof(NPC.HitEffect),
                        new[] { typeof(int), typeof(double) });
                if (npcHitCommitted != null) harmony.Patch(npcHitCommitted,
                    postfix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(AfterNpcHitEffect)));
                // Do not patch Player.Hurt. On 32-bit Terraria, Harmony's ABI fixup for this
                // double-returning method eventually corrupts the call and raises an
                // AccessViolationException. PlayHurtSound is reached only after Hurt commits
                // real damage and is a stable void hook while Projectile.Damage is on-stack.
                var hurtCommitted = AccessTools.Method(typeof(Player), nameof(Player.PlayHurtSound), Type.EmptyTypes);
                if (hurtCommitted != null) harmony.Patch(hurtCommitted,
                    postfix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(AfterPlayerHurtSound)));
                var name = AccessTools.Method(typeof(Lang), nameof(Lang.GetProjectileName), new[] { typeof(int) });
                if (name != null) harmony.Patch(name,
                    prefix: new HarmonyMethod(typeof(ProjectileContentPatches), nameof(BeforeGetName)));
                log.Info("Content: custom projectile defaults/AI/hit/kill bridges installed");
            }
            catch (Exception ex) { log.Error("Content: projectile bridge install failed", ex); }
        }

        private static bool BeforeSetDefaults(Projectile __instance, int Type)
        {
            // Re-entry below is intentional: it runs Terraria's real generic reset/default body
            // against the already-expanded custom id, then this outer call adds mod defaults.
            if (_runningVanillaDefaults) return true;
            var def = _content?.GetProjectile(Type);
            if (def == null) return true;
            if (!_content.IsSessionAllowed(def.ModId)) { __instance.active = false; return false; }
            try
            {
                try
                {
                    _runningVanillaDefaults = true;
                    __instance.SetDefaults(Type);
                }
                finally { _runningVanillaDefaults = false; }

                def.Projectile = __instance;
                def.SetDefaults();

                // Projectile.NewProjectile reserves an array slot and relies on SetDefaults to
                // leave the instance alive. The vanilla default body does that inside one of
                // its known-id branches; a synthetic id has no such branch, so explicitly
                // restore the lifecycle invariant after applying the mod definition.
                __instance.active = true;
            }
            catch (Exception ex) { __instance.active = false; _log?.Error("Content: projectile defaults failed for " + def.ContentKey, ex); }
            finally { def.Projectile = null; }
            return false;
        }

        private static bool BeforeAI(Projectile __instance)
        {
            var def = _content?.GetProjectile(__instance?.type ?? 0);
            if (def == null) return true;
            if (!_content.IsSessionAllowed(def.ModId)) { __instance.active = false; return false; }
            try { def.Projectile = __instance; def.AI(); }
            catch (Exception ex) { _log?.Error("Content: projectile AI failed for " + def.ContentKey, ex); }
            finally { def.Projectile = null; }
            return def.RunVanillaAI;
        }

        private static void BeforeKill(Projectile __instance)
        {
            var def = _content?.GetProjectile(__instance?.type ?? 0); if (def == null) return;
            try { def.Projectile = __instance; def.OnKill(); }
            catch (Exception ex) { _log?.Error("Content: projectile OnKill failed for " + def.ContentKey, ex); }
            finally { def.Projectile = null; }
        }

        private static void BeforeDamage(Projectile __instance)
        { _damagingProjectile = _content?.GetProjectile(__instance?.type ?? 0) == null ? null : __instance; }
        private static void AfterDamage() { _damagingProjectile = null; }
        private static Exception DamageFinalizer(Exception __exception) { _damagingProjectile = null; return __exception; }

        private static void AfterNpcHitEffect(NPC __instance)
        {
            if (_damagingProjectile == null) return;
            var def = _content?.GetProjectile(_damagingProjectile.type); if (def == null) return;
            try { def.Projectile = _damagingProjectile; def.OnHitNpc(__instance); }
            catch (Exception ex) { _log?.Error("Content: projectile OnHitNpc failed for " + def.ContentKey, ex); }
            finally { def.Projectile = null; }
        }

        private static void AfterPlayerHurtSound(Player __instance)
        {
            if (_damagingProjectile == null) return;
            var def = _content?.GetProjectile(_damagingProjectile.type); if (def == null) return;
            try { def.Projectile = _damagingProjectile; def.OnHitPlayer(__instance); }
            catch (Exception ex) { _log?.Error("Content: projectile OnHitPlayer failed for " + def.ContentKey, ex); }
            finally { def.Projectile = null; }
        }

        private static bool BeforeGetName(int __0, ref Terraria.Localization.LocalizedText __result)
        {
            var def = _content?.GetProjectile(__0);
            if (def == null) return true;
            var text = LocalizedTextFactory.Create("TimfProjectile." + def.ContentKey, def.DisplayName)
                       as Terraria.Localization.LocalizedText;
            if (text == null) return true;
            __result = text;
            return false;
        }
    }
}
