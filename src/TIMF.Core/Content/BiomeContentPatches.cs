using System;
using System.Collections.Generic;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    internal static class BiomeContentPatches
    {
        private static ContentManager _content;
        private static ILogger _log;
        private static readonly Dictionary<Player, HashSet<string>> Active =
            new Dictionary<Player, HashSet<string>>();

        internal static void Bind(ContentManager content, ILogger log) { _content = content; _log = log; }

        internal static void Install(Harmony harmony, ILogger log)
        {
            try
            {
                var update = AccessTools.Method(typeof(Player), "UpdateBiomes", Type.EmptyTypes);
                if (update == null) { log.Warn("Content: Player.UpdateBiomes not found — biome lifecycle hooks disabled"); return; }
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(BiomeContentPatches), nameof(AfterUpdateBiomes)));
                log.Info("Content: custom biome lifecycle bridge installed");
            }
            catch (Exception ex) { log.Error("Content: biome lifecycle bridge install failed", ex); }
        }

        private static void AfterUpdateBiomes(Player __instance)
        {
            if (__instance == null || _content == null || !_content.IsActivated) return;
            // Main._playerSceneMetrics describes only the locally rendered player. Reusing it
            // for remote/server players would silently evaluate the wrong area.
            if (Main.dedServ || __instance.whoAmI != Main.myPlayer) return;
            HashSet<string> previous;
            if (!Active.TryGetValue(__instance, out previous))
                Active[__instance] = previous = new HashSet<string>(StringComparer.Ordinal);
            var current = new HashSet<string>(StringComparer.Ordinal);
            foreach (var biome in _content.RegisteredBiomes)
            {
                try
                {
                    if (!_content.IsBiomeActive(biome, __instance)) continue;
                    current.Add(biome.ContentKey);
                    if (!previous.Contains(biome.ContentKey)) biome.OnEnter(__instance);
                    biome.Update(__instance);
                }
                catch (Exception ex) { _log?.Error("Content: biome update failed for " + biome.ContentKey, ex); }
            }
            foreach (var key in previous)
            {
                if (current.Contains(key)) continue;
                foreach (var biome in _content.RegisteredBiomes)
                    if (biome.ContentKey == key)
                    {
                        try { biome.OnLeave(__instance); }
                        catch (Exception ex) { _log?.Error("Content: biome leave failed for " + key, ex); }
                        break;
                    }
            }
            Active[__instance] = current;
        }
    }
}
