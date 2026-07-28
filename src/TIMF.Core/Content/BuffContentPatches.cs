using System;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    internal static class BuffContentPatches
    {
        private static ContentManager _content;
        private static ILogger _log;
        internal static void Bind(ContentManager content, ILogger log) { _content = content; _log = log; }
        internal static void Install(Harmony harmony, ILogger log)
        {
            try
            {
                var update = AccessTools.Method(typeof(Player), nameof(Player.UpdateBuffs), new[] { typeof(int) });
                if (update != null) harmony.Patch(update,
                    prefix: new HarmonyMethod(typeof(BuffContentPatches), nameof(BeforeUpdateBuffs)),
                    postfix: new HarmonyMethod(typeof(BuffContentPatches), nameof(AfterUpdateBuffs)));
                var name = AccessTools.Method(typeof(Lang), nameof(Lang.GetBuffName), new[] { typeof(int) });
                var desc = AccessTools.Method(typeof(Lang), nameof(Lang.GetBuffDescription), new[] { typeof(int) });
                if (name != null) harmony.Patch(name, prefix: new HarmonyMethod(typeof(BuffContentPatches), nameof(BeforeGetName)));
                if (desc != null) harmony.Patch(desc, prefix: new HarmonyMethod(typeof(BuffContentPatches), nameof(BeforeGetDescription)));
                log.Info("Content: custom buff update/name/description bridges installed");
            }
            catch (Exception ex) { log.Error("Content: buff bridge install failed", ex); }
        }

        private static void BeforeUpdateBuffs(Player __instance)
        {
            _content?.EnsurePlayerArrayCapacity(__instance);
        }

        private static void AfterUpdateBuffs(Player __instance)
        {
            if (__instance?.buffType == null) return;
            for (var i = 0; i < __instance.buffType.Length; i++)
            {
                var def = _content?.GetBuff(__instance.buffType[i]);
                if (def == null || !_content.IsSessionAllowed(def.ModId) || __instance.buffTime[i] <= 0) continue;
                try { def.Update(__instance, ref i); }
                catch (Exception ex) { _log?.Error("Content: buff update failed for " + def.ContentKey, ex); }
            }
        }

        private static bool BeforeGetName(int __0, ref string __result)
        {
            var def = _content?.GetBuff(__0);
            if (def == null) return true;
            __result = def.DisplayName ?? "";
            return false;
        }

        private static bool BeforeGetDescription(int __0, ref string __result)
        {
            var def = _content?.GetBuff(__0);
            if (def == null) return true;
            __result = def.Description ?? "";
            return false;
        }
    }
}
