using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.GameContent;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Keeps ID-indexed arrays on newly constructed/deserialized game objects in sync with
    /// TIMF's widened static ID spaces. The update prefixes are intentional safety nets for
    /// game versions whose load path replaces an array after the constructor postfix ran.
    /// </summary>
    internal static class ContentInstanceArrayPatches
    {
        private static ContentManager _content;
        private static ILogger _log;

        internal static void Bind(ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;
        }

        internal static void Install(Harmony harmony, ILogger log)
        {
            try
            {
                PatchPostfix(harmony, AccessTools.Constructor(typeof(Player), Type.EmptyTypes), nameof(AfterPlayerConstructed));
                PatchPrefix(harmony, AccessTools.Method(typeof(Player), nameof(Player.Update), new[] { typeof(int) }), nameof(BeforePlayerUse));
                PatchPrefix(harmony, AccessTools.Method(typeof(Player), nameof(Player.UpdateBuffs), new[] { typeof(int) }), nameof(BeforePlayerUse));
                PatchNamedMethods(harmony, typeof(Player), nameof(Player.AddBuff), nameof(BeforePlayerUse));
                PatchNamedMethods(harmony, typeof(Player), nameof(Player.FindBuffIndex), nameof(BeforePlayerUse));
                PatchNamedMethods(harmony, typeof(Player), "UpdateNearbyCraftingTiles", nameof(BeforePlayerUse));
                var npcCollision = AccessTools.Method(typeof(Player), "Update_NPCCollision", Type.EmptyTypes);
                if (npcCollision != null)
                    harmony.Patch(npcCollision,
                        finalizer: new HarmonyMethod(typeof(ContentInstanceArrayPatches), nameof(AfterNpcCollisionException)));

                PatchPostfix(harmony, AccessTools.Constructor(typeof(NPC), Type.EmptyTypes), nameof(AfterNpcConstructed));
                PatchPrefix(harmony, AccessTools.Method(typeof(NPC), nameof(NPC.UpdateNPC), new[] { typeof(int) }), nameof(BeforeNpcUse));
                PatchNamedMethods(harmony, typeof(NPC), nameof(NPC.SetDefaults), nameof(BeforeNpcUse));
                PatchNamedMethods(harmony, typeof(NPC), nameof(NPC.AddBuff), nameof(BeforeNpcUse));
                PatchNamedMethods(harmony, typeof(NPC), nameof(NPC.FindBuffIndex), nameof(BeforeNpcUse));

                PatchPostfix(harmony, AccessTools.Constructor(typeof(TownRoomManager), Type.EmptyTypes), nameof(AfterTownRoomManagerConstructed));
                PatchNamedMethods(harmony, typeof(TownRoomManager), "HasRoomQuick", nameof(BeforeTownRoomManagerUse));
                PatchNamedMethods(harmony, typeof(TownRoomManager), "HasRoom", nameof(BeforeTownRoomManagerUse));
                PatchNamedMethods(harmony, typeof(TownRoomManager), "SetRoom", nameof(BeforeTownRoomManagerUse));
                PatchNamedMethods(harmony, typeof(TownRoomManager), "KickOut", nameof(BeforeTownRoomManagerUse));
                PatchNamedMethods(harmony, typeof(TownRoomManager), "GetHouseholdStatus", nameof(BeforeTownRoomManagerUse));
                PatchNamedMethods(harmony, typeof(TownRoomManager), "Load", nameof(BeforeTownRoomManagerUse));

                log.Info("Content: player/NPC instance-array lifecycle guards installed");
            }
            catch (Exception ex)
            {
                log.Error("Content: instance-array lifecycle guard installation failed", ex);
            }
        }

        private static void PatchNamedMethods(Harmony harmony, Type type, string name, string prefix)
        {
            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m => m.Name == name))
                PatchPrefix(harmony, method, prefix);
        }

        private static void PatchPrefix(Harmony harmony, MethodBase original, string patch)
        {
            if (original != null)
                harmony.Patch(original, prefix: new HarmonyMethod(typeof(ContentInstanceArrayPatches), patch));
        }

        private static void PatchPostfix(Harmony harmony, MethodBase original, string patch)
        {
            if (original != null)
                harmony.Patch(original, postfix: new HarmonyMethod(typeof(ContentInstanceArrayPatches), patch));
        }

        private static void AfterPlayerConstructed(Player __instance) { _content?.EnsurePlayerArrayCapacity(__instance); }
        private static void BeforePlayerUse(Player __instance) { _content?.EnsurePlayerArrayCapacity(__instance); }
        private static void AfterNpcConstructed(NPC __instance) { _content?.EnsureNpcArrayCapacity(__instance); }
        private static void BeforeNpcUse(NPC __instance) { _content?.EnsureNpcArrayCapacity(__instance); }
        private static void AfterTownRoomManagerConstructed(TownRoomManager __instance) { _content?.EnsureTownRoomArrayCapacity(__instance); }
        private static void BeforeTownRoomManagerUse(TownRoomManager __instance) { _content?.EnsureTownRoomArrayCapacity(__instance); }

        private static Exception AfterNpcCollisionException(Exception __exception)
        {
            if (__exception != null)
                _log?.Error("Content: exception escaped Player.Update_NPCCollision", __exception);
            return __exception;
        }
    }
}
