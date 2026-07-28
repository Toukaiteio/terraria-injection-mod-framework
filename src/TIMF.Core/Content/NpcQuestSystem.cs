using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    internal static class NpcQuestSystem
    {
        private static ILogger _log;
        private static long _daySerial;
        private static readonly Dictionary<Player, Dictionary<string, long>> CompletedByPlayer =
            new Dictionary<Player, Dictionary<string, long>>();
        private static readonly Dictionary<string, long> UnboundCompleted =
            new Dictionary<string, long>(StringComparer.Ordinal);

        internal static void Install(Harmony harmony, ILogger log)
        {
            _log = log;
            try
            {
                var swap = AccessTools.Method(typeof(Main), "AnglerQuestSwap", Type.EmptyTypes);
                if (swap != null) harmony.Patch(swap, postfix: new HarmonyMethod(typeof(NpcQuestSystem), nameof(AfterQuestDayChanged)));
                var worldSave = AccessTools.Method(typeof(WorldFile), "InternalSaveWorld", new[] { typeof(bool), typeof(bool), typeof(bool) });
                var worldLoad = AccessTools.Method(typeof(WorldFile), "LoadWorld", Type.EmptyTypes);
                if (worldSave != null) harmony.Patch(worldSave, postfix: new HarmonyMethod(typeof(NpcQuestSystem), nameof(AfterWorldSave)));
                if (worldLoad != null) harmony.Patch(worldLoad, postfix: new HarmonyMethod(typeof(NpcQuestSystem), nameof(AfterWorldLoad)));
                var playerSave = AccessTools.Method(typeof(Player), "SavePlayer");
                var playerLoad = AccessTools.Method(typeof(Player), "LoadPlayer");
                if (playerSave != null) harmony.Patch(playerSave, postfix: new HarmonyMethod(typeof(NpcQuestSystem), nameof(AfterPlayerSave)));
                if (playerLoad != null) harmony.Patch(playerLoad, postfix: new HarmonyMethod(typeof(NpcQuestSystem), nameof(AfterPlayerLoad)));
            }
            catch (Exception ex) { log.Error("Content: NPC daily quest persistence install failed", ex); }
        }

        private static void AfterQuestDayChanged() { _daySerial = unchecked(_daySerial + 1); }
        private static string WorldPath() => (Main.ActiveWorldFileData?.Path ?? Main.worldPathName) + ".timf-questday";
        private static void AfterWorldSave() { AtomicWrite(WorldPath(), _daySerial.ToString(CultureInfo.InvariantCulture)); }
        private static void AfterWorldLoad()
        {
            _daySerial = 0;
            try { long.TryParse(File.ReadAllText(WorldPath()), NumberStyles.Integer, CultureInfo.InvariantCulture, out _daySerial); }
            catch { }
        }

        private static void AfterPlayerSave(object playerFile)
        {
            var path = PathOf(playerFile); if (path == null) return;
            var completed = CompletedFor(PlayerOf(playerFile));
            var sb = new StringBuilder("timf-quests\t1\n");
            foreach (var kv in completed) sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AtomicWrite(path + ".timfquests", sb.ToString());
        }

        private static void AfterPlayerLoad(object __result)
        {
            var completed = CompletedFor(PlayerOf(__result));
            completed.Clear(); var path = PathOf(__result); if (path == null) return;
            try
            {
                foreach (var line in File.ReadAllLines(path + ".timfquests"))
                {
                    var p = line.Split('\t'); long day;
                    if (p.Length == 2 && long.TryParse(p[1], out day)) completed[p[0]] = day;
                }
            }
            catch { }
        }

        internal static TimfDailyQuest Current(TimfNpc npc, Player player)
        {
            var list = npc.GetDailyQuests(player);
            if (list == null || list.Count == 0) return null;
            var hash = 17;
            foreach (var c in npc.ContentKey) hash = unchecked(hash * 31 + c);
            var index = (int)((_daySerial + (hash & 0x7fffffff)) % list.Count);
            return list[index];
        }

        internal static string Status(TimfNpc npc, Player player)
        {
            var q = Current(npc, player); if (q == null) return null;
            long day;
            if (CompletedFor(player).TryGetValue(npc.ContentKey, out day) && day == _daySerial) return "Quest already completed today.";
            return q.Description ?? q.InternalName;
        }

        internal static string TryComplete(TimfNpc npc, Player player)
        {
            if (Main.netMode != 0) return "Daily quest submission is single-player only until the TIMF authority message is available.";
            var q = Current(npc, player); if (q == null) return "No daily quest is available.";
            var completed = CompletedFor(player);
            long day; if (completed.TryGetValue(npc.ContentKey, out day) && day == _daySerial) return "Already completed today.";
            var needed = Math.Max(1, q.RequiredStack); var have = 0;
            foreach (var item in player.inventory) if (item != null && item.type == q.RequiredItemType) have += item.stack;
            if (have < needed) return (q.Description ?? "Quest") + " (need " + needed + ")";
            for (var i = 0; i < player.inventory.Length && needed > 0; i++)
            {
                var item = player.inventory[i]; if (item == null || item.type != q.RequiredItemType) continue;
                var take = Math.Min(needed, item.stack); item.stack -= take; needed -= take; if (item.stack <= 0) item.SetDefaults(0);
            }
            if (q.Rewards != null) foreach (var reward in q.Rewards) Give(player, reward.ItemType, reward.Stack);
            if (q.StatusEffects != null)
                foreach (var effect in q.StatusEffects)
                    if (effect != null && effect.BuffType > 0 && effect.Duration > 0)
                        player.AddBuff(effect.BuffType, effect.Duration);
            completed[npc.ContentKey] = _daySerial;
            return "Quest completed. Come back after the next dawn.";
        }

        private static void Give(Player p, int type, int stack)
        {
            if (type <= 0 || stack <= 0) return;
            p.QuickSpawnItem(p.GetItemSource_Misc(0), type, stack);
        }

        private static Dictionary<string, long> CompletedFor(Player player)
        {
            if (player == null) return UnboundCompleted;
            Dictionary<string, long> completed;
            if (!CompletedByPlayer.TryGetValue(player, out completed))
                CompletedByPlayer[player] = completed = new Dictionary<string, long>(StringComparer.Ordinal);
            return completed;
        }

        private static Player PlayerOf(object data) => data?.GetType().GetProperty("Player", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(data) as Player;
        private static string PathOf(object data) => data?.GetType().GetProperty("Path", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(data) as string;
        private static void AtomicWrite(string path, string text)
        {
            try
            {
                var tmp = path + ".tmp"; File.WriteAllText(tmp, text, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
                else File.Move(tmp, path);
            }
            catch (Exception ex) { _log?.Error("Content: quest sidecar write failed", ex); }
        }
    }
}
