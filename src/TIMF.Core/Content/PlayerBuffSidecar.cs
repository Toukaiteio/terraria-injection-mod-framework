using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Persists custom player effects by stable content key. Custom numeric buff ids never enter
    /// the vanilla .plr, so changing local allocations cannot turn an old effect into another one.
    /// </summary>
    internal static class PlayerBuffSidecar
    {
        private const string Extension = ".timfbuffs";
        private const string Header = "timf-buffs\t1";
        private static ContentManager _content;
        private static ILogger _log;
        private static readonly List<Entry> Stash = new List<Entry>();
        private static readonly List<Entry> Unresolved = new List<Entry>();
        private static bool _readFailed;

        private sealed class Entry
        {
            public int Slot;
            public string Key;
            public int Time;
            public bool Persist;
        }

        internal static void Install(Harmony harmony, ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;
            try
            {
                var save = AccessTools.Method(typeof(Player), "SavePlayer");
                var load = AccessTools.Method(typeof(Player), "LoadPlayer");
                if (save == null || load == null)
                {
                    log.Error("Buff sidecar: Player save/load methods not found — custom buffs will not persist");
                    return;
                }
                harmony.Patch(save,
                    prefix: new HarmonyMethod(typeof(PlayerBuffSidecar), nameof(BeforeSave)),
                    postfix: new HarmonyMethod(typeof(PlayerBuffSidecar), nameof(AfterSave)),
                    finalizer: new HarmonyMethod(typeof(PlayerBuffSidecar), nameof(SaveFinalizer)));
                harmony.Patch(load, postfix: new HarmonyMethod(typeof(PlayerBuffSidecar), nameof(AfterLoad)));
                log.Info("Buff sidecar: stable-key player effect persistence installed (" + Extension + ")");
            }
            catch (Exception ex) { log.Error("Buff sidecar: install failed", ex); }
        }

        private static void BeforeSave(object playerFile)
        {
            Stash.Clear();
            try
            {
                var player = PlayerOf(playerFile);
                if (player?.buffType == null || player.buffTime == null || _content == null) return;
                var length = Math.Min(player.buffType.Length, player.buffTime.Length);
                for (var i = 0; i < length; i++)
                {
                    var def = _content.GetBuff(player.buffType[i]);
                    if (def == null) continue;
                    Stash.Add(new Entry { Slot = i, Key = def.ContentKey, Time = player.buffTime[i], Persist = def.Save });
                    // Even Save=false effects must be hidden from .plr: their ids are framework-local.
                    player.buffType[i] = 0;
                    player.buffTime[i] = 0;
                }

                // Retain entries belonging to temporarily missing mods. Occupying the old slot is
                // an explicit player-side conflict, so live state wins and drops that old record.
                foreach (var old in Unresolved)
                {
                    if (old.Slot < 0 || old.Slot >= length) continue;
                    if (player.buffType[old.Slot] != 0 || ContainsSlot(Stash, old.Slot)) continue;
                    Stash.Add(old);
                }
            }
            catch (Exception ex)
            {
                // A partial stash must never be followed by a vanilla write: any custom ids not
                // reached yet would leak into .plr. Restore what was touched and abort the save.
                try { var player = PlayerOf(playerFile); if (player != null) RestoreRuntime(player, Stash); }
                catch (Exception restoreEx) { _log?.Error("Buff sidecar: prepare-failure restore failed", restoreEx); }
                Stash.Clear();
                _log?.Error("Buff sidecar: preparing save failed; vanilla player save was cancelled", ex);
                throw new InvalidOperationException("TIMF could not safely prepare custom buffs for player save", ex);
            }
        }

        private static void AfterSave(object playerFile)
        {
            try
            {
                var player = PlayerOf(playerFile);
                if (player != null) RestoreRuntime(player, Stash);
                var path = PathOf(playerFile);
                if (path != null && !_readFailed) Write(path + Extension, Stash);
                else if (_readFailed) _log?.Warn("Buff sidecar: previous file was unreadable; refusing to overwrite it");
            }
            catch (Exception ex) { _log?.Error("Buff sidecar: finishing save failed", ex); }
            finally { Stash.Clear(); }
        }

        private static Exception SaveFinalizer(Exception __exception, object playerFile)
        {
            if (__exception != null && Stash.Count > 0)
            {
                try { var player = PlayerOf(playerFile); if (player != null) RestoreRuntime(player, Stash); }
                catch (Exception ex) { _log?.Error("Buff sidecar: failed-save restore failed", ex); }
                finally { Stash.Clear(); }
            }
            return __exception;
        }

        private static void AfterLoad(object __result)
        {
            try
            {
                var player = PlayerOf(__result);
                var path = PathOf(__result);
                if (player == null || path == null || _content == null) return;
                bool failed;
                var entries = Read(path + Extension, out failed);
                _readFailed = failed;
                Unresolved.Clear();
                RestoreLoaded(player, entries);
            }
            catch (Exception ex) { _log?.Error("Buff sidecar: restoring on load failed", ex); }
        }

        private static void RestoreRuntime(Player player, List<Entry> entries)
        {
            foreach (var e in entries)
            {
                if (_content.BuffType(e.Key) == 0 || e.Slot < 0 || e.Slot >= player.buffType.Length) continue;
                player.buffType[e.Slot] = _content.BuffType(e.Key);
                player.buffTime[e.Slot] = e.Time;
            }
        }

        private static void RestoreLoaded(Player player, List<Entry> entries)
        {
            var restored = 0;
            foreach (var e in entries)
            {
                var type = _content.BuffType(e.Key);
                if (type == 0)
                {
                    Unresolved.Add(e);
                    continue;
                }
                var slot = e.Slot;
                if (slot < 0 || slot >= player.buffType.Length || player.buffType[slot] != 0)
                    slot = FirstEmpty(player);
                if (slot < 0)
                {
                    Unresolved.Add(e);
                    continue;
                }
                player.buffType[slot] = type;
                player.buffTime[slot] = Math.Max(1, e.Time);
                restored++;
            }
            _log?.Info("Buff sidecar: restored " + restored + " effect(s); " + Unresolved.Count + " unresolved");
        }

        private static int FirstEmpty(Player player)
        {
            for (var i = 0; i < player.buffType.Length; i++)
                if (player.buffType[i] == 0 || player.buffTime[i] <= 0) return i;
            return -1;
        }

        private static bool ContainsSlot(List<Entry> entries, int slot)
        {
            foreach (var e in entries) if (e.Slot == slot) return true;
            return false;
        }

        private static void Write(string path, List<Entry> entries)
        {
            var persisted = entries.FindAll(e => e.Persist);
            if (persisted.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            var sb = new StringBuilder().AppendLine(Header);
            foreach (var e in persisted)
                sb.Append(e.Slot.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Key).Append('\t').Append(e.Time.ToString(CultureInfo.InvariantCulture)).AppendLine();
            var tmp = path + ".tmp";
            var backup = path + ".bak";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(sb.ToString()); writer.Flush(); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, backup, true);
            else File.Move(tmp, path);
            _log?.Info("Buff sidecar: wrote " + persisted.Count + " effect(s) to " + Path.GetFileName(path));
        }

        private static List<Entry> Read(string path, out bool failed)
        {
            failed = false;
            var result = new List<Entry>();
            if (!File.Exists(path)) return result;
            try
            {
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
                    throw new InvalidDataException("unknown header or version");
                for (var i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = lines[i].Split('\t');
                    int slot, time;
                    if (parts.Length != 3
                        || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot)
                        || string.IsNullOrWhiteSpace(parts[1])
                        || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out time))
                        throw new InvalidDataException("invalid record at line " + (i + 1));
                    result.Add(new Entry { Slot = slot, Key = parts[1], Time = time, Persist = true });
                }
            }
            catch (Exception ex)
            {
                failed = true;
                result.Clear();
                _log?.Error("Buff sidecar: reading " + Path.GetFileName(path) + " failed; original will be preserved", ex);
            }
            return result;
        }

        private static Player PlayerOf(object data)
        {
            return data?.GetType().GetProperty("Player", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(data) as Player;
        }

        private static string PathOf(object data)
        {
            return data?.GetType().GetProperty("Path", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(data) as string;
        }
    }
}
