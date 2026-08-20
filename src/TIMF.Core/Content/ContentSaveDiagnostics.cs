using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Reports how many modded items exist on a character at save time and again at load time.
    ///
    /// Modded items vanish across a restart and the cause could sit at either end — the item
    /// could already be gone before the file is written, or be dropped while reading it back.
    /// Those two have completely different fixes, and the save format gives no hint which it
    /// is, so this counts them on both sides instead of guessing.
    /// </summary>
    internal static class ContentSaveDiagnostics
    {
        private static ContentManager _content;
        private static ILogger _log;

        internal static void Install(Harmony harmony, ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;

            try
            {
                var save = AccessTools.Method(typeof(Player), "SavePlayer");
                if (save != null)
                    harmony.Patch(save, prefix: new HarmonyMethod(typeof(ContentSaveDiagnostics), nameof(BeforeSave)));
                else
                    log.Warn("Content diag: Player.SavePlayer not found");

                var load = AccessTools.Method(typeof(Player), "LoadPlayer");
                if (load != null)
                    harmony.Patch(load, postfix: new HarmonyMethod(typeof(ContentSaveDiagnostics), nameof(AfterLoad)));
                else
                    log.Warn("Content diag: Player.LoadPlayer not found");

                // The census only sees the finished Player. Watching netDefaults tells us
                // whether the loader even read a modded id off disk — which separates "the file
                // or the read filtered it out" from "it was read fine and something afterwards
                // discarded it". Those need opposite fixes.
                var netDefaults = AccessTools.Method(typeof(Item), "netDefaults", new[] { typeof(int) });
                if (netDefaults != null)
                    harmony.Patch(netDefaults,
                        prefix: new HarmonyMethod(typeof(ContentSaveDiagnostics), nameof(OnNetDefaults)));
                else
                    log.Warn("Content diag: Item.netDefaults(int) not found");

                log.Info("Content diag: save/load item census installed");
            }
            catch (Exception ex)
            {
                log.Error("Content diag: install failed", ex);
            }
        }

        private static int _netDefaultsReports;

        private static void OnNetDefaults(int type)
        {
            try
            {
                if (_content == null || type < _content.VanillaItemCount)
                    return;
                if (_netDefaultsReports >= 40)
                    return;
                _netDefaultsReports++;
                _log?.Info("Content diag: netDefaults(" + type + ") called — a modded id reached "
                           + "the item constructor (known=" + (_content.GetItem(type) != null) + ")");
            }
            catch { /* diagnostics must never break the game */ }
        }

        private static void BeforeSave(object playerFile)
        {
            try
            {
                var player = PlayerOf(playerFile);
                Census("SAVE", player);
            }
            catch (Exception ex)
            {
            _log?.Warn("Content diag: save census failed: " + ex.GetType().Name);
            }
        }

        private static void AfterLoad(object __result)
        {
            try
            {
                var player = PlayerOf(__result);
                Census("LOAD", player);
            }
            catch (Exception ex)
            {
            _log?.Warn("Content diag: load census failed: " + ex.GetType().Name);
            }
        }

        private static Player PlayerOf(object playerFileData)
        {
            if (playerFileData == null)
                return null;
            var prop = playerFileData.GetType().GetProperty("Player",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return prop?.GetValue(playerFileData) as Player;
        }

        private static void Census(string phase, Player player)
        {
            if (player == null || _content == null || !_content.HasContent)
                return;

            var found = new List<string>();
            var vanillaCount = _content.VanillaItemCount;

            Scan(player.inventory, "inventory", vanillaCount, found);
            Scan(player.armor, "armor", vanillaCount, found);
            Scan(player.miscEquips, "miscEquips", vanillaCount, found);
            Scan(player.bank?.item, "bank", vanillaCount, found);
            Scan(player.bank2?.item, "bank2", vanillaCount, found);
            Scan(player.bank3?.item, "bank3", vanillaCount, found);
            Scan(player.bank4?.item, "bank4", vanillaCount, found);

            _log.Info("Content diag [" + phase + "] player '" + (player.name ?? "?") + "': "
                      + found.Count + " modded item(s)"
                      + (found.Count > 0 ? " -> " + string.Join(", ", found.ToArray()) : "")
                      + "   (ItemID.Count=" + SafeCount() + ", vanilla=" + vanillaCount + ")");
        }

        /// <summary>
        /// Counts slots holding a modded type, including ones with stack 0. An item whose stack
        /// was zeroed still carries its type, and skipping those would make "the id survived but
        /// the stack was cleared" look identical to "the id was replaced with air".
        /// </summary>
        private static void Scan(Item[] items, string label, int vanillaCount, List<string> found)
        {
            if (items == null)
                return;
            for (var i = 0; i < items.Length; i++)
            {
                var it = items[i];
                if (it == null || it.type < vanillaCount)
                    continue;
                found.Add(label + "[" + i + "]=" + it.type + "x" + it.stack
                          + (it.stack <= 0 ? "(STACK ZEROED)" : ""));
            }
        }

        private static int SafeCount()
        {
            try { return Terraria.ID.ItemID.Count; }
            catch { return -1; }
        }
    }
}
