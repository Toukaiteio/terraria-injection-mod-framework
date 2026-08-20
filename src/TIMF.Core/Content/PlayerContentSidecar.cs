using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Keeps modded items out of the vanilla player file and stores them alongside it instead.
    ///
    /// Writing modded ids straight into the .plr does not work: vanilla's loader guards every
    /// slot with <c>if (type &gt;= ItemID.Count) -&gt; air</c>, and that comparison is compiled
    /// against a <c>static readonly short</c>. The JIT folds such a field into a constant, so
    /// widening <c>ItemID.Count</c> by reflection never reaches the already-compiled check —
    /// measured directly: <c>Item.netDefaults</c> was never once called with a modded id while
    /// loading a character that had four of them saved.
    ///
    /// So the modded slots are blanked to air for the duration of the vanilla write and their
    /// real contents go into a sidecar keyed by content key rather than by number. Three things
    /// fall out of that:
    /// <list type="bullet">
    /// <item>the .plr stays a valid vanilla file — it opens without TIMF, just without the modded items;</item>
    /// <item>identity is a string, so the file survives id reassignment and moving between machines;</item>
    /// <item>vanilla's stale-constant guard never sees an id it would reject.</item>
    /// </list>
    /// </summary>
    internal static class PlayerContentSidecar
    {
        private const string Extension = ".timfitems";
        private const int FormatVersion = 1;

        private static ContentManager _content;
        private static ILogger _log;

        /// <summary>Slots blanked for the current save, awaiting restore in the postfix.</summary>
        private static readonly List<Stashed> Stash = new List<Stashed>();
        /// <summary>Entries whose owning content is temporarily unavailable.</summary>
        private static readonly List<Stashed> Unresolved = new List<Stashed>();

        private sealed class Stashed
        {
            public string Container;
            public int Index;
            public string Key;
            public int Stack;
            public byte Prefix;
            public bool Favorited;
        }

        internal static void Install(Harmony harmony, ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;

            try
            {
                var save = AccessTools.Method(typeof(Player), "SavePlayer");
                if (save == null)
                {
                    log.Error("Content sidecar: Player.SavePlayer not found — modded items will not persist");
                    return;
                }

                harmony.Patch(save,
                    prefix: new HarmonyMethod(typeof(PlayerContentSidecar), nameof(BeforeSave)),
                    postfix: new HarmonyMethod(typeof(PlayerContentSidecar), nameof(AfterSave)),
                    finalizer: new HarmonyMethod(typeof(PlayerContentSidecar), nameof(SaveFinalizer)));

                var load = AccessTools.Method(typeof(Player), "LoadPlayer");
                if (load == null)
                {
                    log.Error("Content sidecar: Player.LoadPlayer not found — modded items will not restore");
                    return;
                }

                harmony.Patch(load, postfix: new HarmonyMethod(typeof(PlayerContentSidecar), nameof(AfterLoad)));
                log.Info("Content sidecar: player item persistence installed (" + Extension + ")");
            }
            catch (Exception ex)
            {
                log.Error("Content sidecar: install failed", ex);
            }
        }

        // ---- save ----

        private static void BeforeSave(object playerFile)
        {
            Stash.Clear();
            try
            {
                var player = PlayerOf(playerFile);
                if (player == null || _content == null)
                    return;

                var containers = Containers(player);
                foreach (var container in containers)
                {
                    var items = container.Value;
                    for (var i = 0; i < items.Length; i++)
                    {
                        var it = items[i];
                        if (it == null || it.type < _content.VanillaItemCount)
                            continue;

                        var def = _content.GetItem(it.type);
                        if (def == null)
                            continue;

                        Stash.Add(new Stashed
                        {
                            Container = container.Key,
                            Index = i,
                            Key = def.ContentKey,
                            Stack = it.stack,
                            Prefix = it.prefix,
                            Favorited = it.favorited,
                        });

                        // Blank it only for the duration of the vanilla write.
                        it.SetDefaults(0);
                    }
                }

                // A removed mod leaves an air slot in the vanilla .plr. Preserve its content
                // key until the mod returns, unless the player has deliberately occupied that
                // slot in the meantime. The current character state always wins conflicts.
                for (var unresolvedIndex = Unresolved.Count - 1; unresolvedIndex >= 0; unresolvedIndex--)
                {
                    var unresolved = Unresolved[unresolvedIndex];
                    Item[] items;
                    if (!containers.TryGetValue(unresolved.Container, out items)
                        || unresolved.Index < 0 || unresolved.Index >= items.Length)
                        continue;
                    var current = items[unresolved.Index];
                    if (current != null && current.type > 0 && current.stack > 0)
                    {
                        Unresolved.RemoveAt(unresolvedIndex);
                        continue;
                    }
                    if (!Contains(Stash, unresolved.Container, unresolved.Index))
                        Stash.Add(unresolved);
                }
            }
            catch (Exception ex)
            {
                _log?.Error("Content sidecar: preparing save failed", ex);
            }
        }

        private static void AfterSave(object playerFile)
        {
            try
            {
                var player = PlayerOf(playerFile);
                if (player != null)
                    Restore(player, Stash, "save-restore");

                var path = PathOf(playerFile);
                if (path != null)
                    Write(path + Extension, Stash);
            }
            catch (Exception ex)
            {
                _log?.Error("Content sidecar: finishing save failed", ex);
            }
            finally
            {
                Stash.Clear();
            }
        }

        private static Exception SaveFinalizer(Exception __exception, object playerFile)
        {
            // Never leave the live character with air in every stashed slot when vanilla save
            // fails. Successful saves have already been restored and cleared by the postfix.
            if (__exception != null && Stash.Count > 0)
            {
                try
                {
                    var player = PlayerOf(playerFile);
                    if (player != null)
                        Restore(player, Stash, "failed-save-restore");
                }
                catch (Exception ex)
                {
                    _log?.Error("Content sidecar: restoring after failed save failed", ex);
                }
                finally
                {
                    Stash.Clear();
                }
            }
            return __exception;
        }

        // ---- load ----

        private static void AfterLoad(object __result)
        {
            try
            {
                var player = PlayerOf(__result);
                var path = PathOf(__result);
                if (player == null || path == null || _content == null)
                    return;

                var entries = Read(path + Extension);
                Unresolved.Clear();
                if (entries.Count == 0)
                    return;

                Restore(player, entries, "load");
            }
            catch (Exception ex)
            {
                _log?.Error("Content sidecar: restoring on load failed", ex);
            }
        }

        private static void Restore(Player player, List<Stashed> entries, string phase)
        {
            if (entries.Count == 0)
                return;

            var containers = Containers(player);
            var restored = 0;
            var missing = new List<string>();

            foreach (var e in entries)
            {
                Item[] items;
                if (!containers.TryGetValue(e.Container, out items) || e.Index < 0 || e.Index >= items.Length)
                    continue;

                var type = _content.ItemType(e.Key);
                if (type == 0)
                {
                    // The mod that owned this content is gone. The entry stays in the sidecar
                    // untouched, so reinstalling the mod brings the item back.
                    missing.Add(e.Key);
                    if (phase == "load")
                        Unresolved.Add(e);
                    continue;
                }

                var item = items[e.Index] ?? (items[e.Index] = new Item());
                item.SetDefaults(type);
                item.stack = e.Stack;
                item.Prefix(e.Prefix);
                item.favorited = e.Favorited;
                restored++;
            }

            _log.Info("Content sidecar [" + phase + "] restored " + restored + " modded item(s)"
                      + (missing.Count > 0 ? "; " + missing.Count + " unavailable (mod not installed): "
                                             + string.Join(", ", missing.ToArray()) : ""));
        }

        private static bool Contains(List<Stashed> entries, string container, int index)
        {
            foreach (var entry in entries)
                if (entry.Index == index
                    && string.Equals(entry.Container, container, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ---- containers ----

        /// <summary>
        /// Every player-owned item array that the vanilla save round-trips. Anything omitted
        /// here silently loses its modded items, so this mirrors <c>Player.FixLoadedData</c>'s
        /// own list rather than being assembled by guesswork.
        /// </summary>
        private static Dictionary<string, Item[]> Containers(Player p)
        {
            var map = new Dictionary<string, Item[]>(StringComparer.Ordinal);
            Add(map, "inventory", p.inventory);
            Add(map, "armor", p.armor);
            Add(map, "dye", p.dye);
            Add(map, "miscEquips", p.miscEquips);
            Add(map, "miscDyes", p.miscDyes);
            Add(map, "bank", p.bank?.item);
            Add(map, "bank2", p.bank2?.item);
            Add(map, "bank3", p.bank3?.item);
            Add(map, "bank4", p.bank4?.item);

            try
            {
                var loadouts = p.Loadouts;
                if (loadouts != null)
                {
                    for (var i = 0; i < loadouts.Length; i++)
                    {
                        var lo = loadouts[i];
                        if (lo == null)
                            continue;
                        Add(map, "loadout" + i + ".armor", lo.Armor);
                        Add(map, "loadout" + i + ".dye", lo.Dye);
                    }
                }
            }
            catch { /* loadouts are optional across versions */ }

            return map;
        }

        private static void Add(Dictionary<string, Item[]> map, string name, Item[] items)
        {
            if (items != null)
                map[name] = items;
        }

        // ---- sidecar file ----

        private static void Write(string path, List<Stashed> entries)
        {
            try
            {
                if (entries.Count == 0)
                {
                    // No modded items left: drop a stale sidecar rather than resurrect them later.
                    if (File.Exists(path))
                        File.Delete(path);
                    return;
                }

                var sb = new StringBuilder();
                sb.Append("timf-items\t").Append(FormatVersion).AppendLine();
                foreach (var e in entries)
                {
                    sb.Append(e.Container).Append('\t')
                      .Append(e.Index.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(e.Key).Append('\t')
                      .Append(e.Stack.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(((int)e.Prefix).ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(e.Favorited ? '1' : '0')
                      .AppendLine();
                }

                var tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);

                _log.Info("Content sidecar: wrote " + entries.Count + " item(s) to " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _log?.Error("Content sidecar: writing " + Path.GetFileName(path) + " failed", ex);
            }
        }

        private static List<Stashed> Read(string path)
        {
            var list = new List<Stashed>();
            try
            {
                if (!File.Exists(path))
                    return list;

                var lines = File.ReadAllLines(path, Encoding.UTF8);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("timf-items", StringComparison.Ordinal))
                        continue;

                    var parts = line.Split('\t');
                    if (parts.Length < 6)
                        continue;

                    int index, stack, prefix;
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index) ||
                        !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out stack) ||
                        !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out prefix))
                        continue;

                    list.Add(new Stashed
                    {
                        Container = parts[0],
                        Index = index,
                        Key = parts[2],
                        Stack = stack,
                        Prefix = (byte)prefix,
                        Favorited = parts[5] == "1",
                    });
                }
            }
            catch (Exception ex)
            {
                _log?.Error("Content sidecar: reading " + Path.GetFileName(path) + " failed", ex);
            }
            return list;
        }

        // ---- reflection helpers ----

        private static Player PlayerOf(object playerFileData)
        {
            if (playerFileData == null)
                return null;
            return playerFileData.GetType()
                .GetProperty("Player", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(playerFileData) as Player;
        }

        private static string PathOf(object playerFileData)
        {
            if (playerFileData == null)
                return null;
            return playerFileData.GetType()
                .GetProperty("Path", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(playerFileData) as string;
        }
    }
}
