using System;
using System.Reflection;
using HarmonyLib;
using Terraria;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Dispatches info-accessory hooks for the local player AFTER the game has rebuilt acc*
    /// flags for the current frame.
    ///
    /// Important Terraria 1.4.5 detail:
    /// - Per-frame path is ResetEffects() (zeros acc*) → UpdateEquips() (rebuild from items).
    /// - Player.RefreshInfoAccs() is only called while the inventory UI is open, and it
    ///   recomputes from scratch — so we must postfix BOTH entry points or the inventory
    ///   refresh wipes the per-frame grant.
    /// </summary>
    [HarmonyPatch]
    internal static class InfoAccessoryGrantPatch
    {
        private static InfoAccessoryHookRegistry _registry;

        internal static void SetRegistry(InfoAccessoryHookRegistry registry)
        {
            _registry = registry;
        }

        // Harmony multi-target: both methods rebuild info-acc flags.
        private static MethodBase[] TargetMethods()
        {
            return new MethodBase[]
            {
                AccessTools.Method(typeof(Player), "UpdateEquips", new[] { typeof(int) }),
                AccessTools.Method(typeof(Player), "RefreshInfoAccs", Type.EmptyTypes),
            };
        }

        private static void Postfix(Player __instance)
        {
            try
            {
                if (_registry == null || Main.dedServ)
                    return;
                if (__instance == null || !__instance.active)
                    return;
                if (__instance.whoAmI != Main.myPlayer)
                    return;

                _registry.Dispatch(__instance);
            }
            catch
            {
                // Never break equipment / inventory refresh.
            }
        }
    }

    /// <summary>Legacy name used by GameHooks.SetRegistry wiring.</summary>
    internal static class RefreshInfoAccsPatch
    {
        internal static void SetRegistry(InfoAccessoryHookRegistry registry)
        {
            InfoAccessoryGrantPatch.SetRegistry(registry);
        }
    }
}
