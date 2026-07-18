using System;
using System.Reflection;
using HarmonyLib;
using Terraria;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Prefix on Player.ItemCheck(): dispatches IPlayerUpdateHook for the local player
    /// immediately before the game processes item use.
    ///
    /// Must NOT be Player.Update Prefix — Update later runs ResetControls +
    /// PlayerInput.Triggers.Current.CopyInto which overwrites controlUseItem from real mouse.
    /// ItemCheck runs after that, so hooks can set controlUseItem / mouse aim and have them stick.
    ///
    /// Reentrancy guard: mods (e.g. AutoFishing) may invoke ItemCheck() themselves from a hook.
    /// </summary>
    [HarmonyPatch]
    internal static class PlayerUpdatePatch
    {
        private static PlayerUpdateHookRegistry _registry;
        [ThreadStatic]
        private static int _depth;

        internal static void SetRegistry(PlayerUpdateHookRegistry registry)
        {
            _registry = registry;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Player), "ItemCheck", Type.EmptyTypes);
        }

        private static void Prefix(Player __instance)
        {
            if (_depth > 0)
                return;

            try
            {
                if (_registry == null)
                    return;
                if (Main.dedServ)
                    return;
                if (__instance == null || !__instance.active)
                    return;
                if (__instance.whoAmI != Main.myPlayer)
                    return;

                _depth++;
                try
                {
                    _registry.Dispatch();
                }
                finally
                {
                    _depth--;
                }
            }
            catch
            {
                _depth = 0;
                // Never break item checks.
            }
        }
    }
}
