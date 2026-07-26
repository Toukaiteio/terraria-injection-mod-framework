using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Keeps custom containers and world-chest custom items out of .wld. Entire custom
    /// containers are hidden while vanilla SaveChests runs; modded slots in vanilla chests are
    /// temporarily replaced with air. The sidecar is keyed by content name, not numeric id.
    /// </summary>
    internal static class WorldChestSidecar
    {
        private const string Magic = "TIMF-CHESTS";
        private const int FormatVersion = 1;
        private const string Extension = ".timf-chests";

        private static ContentManager _content;
        private static ILogger _log;
        private static SaveState _save;
        private static List<ChestRecord> _pendingLoad;
        private static int _restoreDelayFrames = -1;
        private static readonly List<ChestRecord> Unresolved = new List<ChestRecord>();

        private sealed class SaveState
        {
            public readonly List<ChestRecord> Records = new List<ChestRecord>();
            public readonly List<RemovedChest> Removed = new List<RemovedChest>();
            public readonly List<RemovedItem> Blanked = new List<RemovedItem>();
        }

        private sealed class RemovedChest { public int Index; public Chest Chest; }
        private sealed class RemovedItem { public Item[] Items; public int Index; public Item Item; }

        private sealed class ChestRecord
        {
            public int X;
            public int Y;
            public string ContainerTileKey;
            public string Name;
            public readonly List<ItemRecord> Items = new List<ItemRecord>();
            public bool IsWholeContainer => !string.IsNullOrEmpty(ContainerTileKey);

            public void Write(BinaryWriter writer)
            {
                writer.Write(X);
                writer.Write(Y);
                writer.Write(ContainerTileKey ?? "");
                writer.Write(Name ?? "");
                writer.Write(Items.Count);
                foreach (var item in Items) item.Write(writer);
            }

            public static ChestRecord Read(BinaryReader reader)
            {
                var record = new ChestRecord
                {
                    X = reader.ReadInt32(), Y = reader.ReadInt32(),
                    ContainerTileKey = reader.ReadString(), Name = reader.ReadString(),
                };
                var count = reader.ReadInt32();
                if (count < 0 || count > 1000)
                    throw new InvalidDataException("Invalid TIMF chest item count: " + count);
                for (var i = 0; i < count; i++) record.Items.Add(ItemRecord.Read(reader));
                return record;
            }
        }

        private sealed class ItemRecord
        {
            public int Slot;
            public string ContentKey;
            public int VanillaType;
            public int Stack;
            public byte Prefix;
            public bool Favorited;

            public void Write(BinaryWriter writer)
            {
                writer.Write(Slot);
                writer.Write(ContentKey ?? "");
                writer.Write(VanillaType);
                writer.Write(Stack);
                writer.Write(Prefix);
                writer.Write(Favorited);
            }

            public static ItemRecord Read(BinaryReader reader)
            {
                return new ItemRecord
                {
                    Slot = reader.ReadInt32(), ContentKey = reader.ReadString(),
                    VanillaType = reader.ReadInt32(), Stack = reader.ReadInt32(),
                    Prefix = reader.ReadByte(), Favorited = reader.ReadBoolean(),
                };
            }
        }

        internal static void Install(Harmony harmony, ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;
            WorldGen.Hooks.OnWorldLoad += AfterWorldLoaded;
            try
            {
                var save = AccessTools.Method(typeof(WorldFile), "InternalSaveWorld",
                    new[] { typeof(bool), typeof(bool), typeof(bool) });
                if (save == null)
                    log.Error("Content chest sidecar: WorldFile.InternalSaveWorld not found");
                else
                    harmony.Patch(save,
                        prefix: new HarmonyMethod(typeof(WorldChestSidecar), nameof(BeforeSave))
                            { priority = Priority.First },
                        postfix: new HarmonyMethod(typeof(WorldChestSidecar), nameof(AfterSave))
                            { priority = Priority.Last },
                        finalizer: new HarmonyMethod(typeof(WorldChestSidecar), nameof(SaveFinalizer))
                            { priority = Priority.Last });

                var load = AccessTools.Method(typeof(WorldFile), "LoadWorld", Type.EmptyTypes);
                if (load == null)
                    log.Error("Content chest sidecar: WorldFile.LoadWorld not found");
                else
                    harmony.Patch(load,
                        postfix: new HarmonyMethod(typeof(WorldChestSidecar), nameof(AfterLoad)));
                log.Info("Content chest sidecar: reliable world-container persistence installed");
            }
            catch (Exception ex) { log.Error("Content chest sidecar: install failed", ex); }
        }

        private static void BeforeSave()
        {
            if (_save != null || _content == null || !_content.IsActivated)
                return;
            if (string.IsNullOrEmpty(SidecarPath()))
                return;

            var state = new SaveState();
            try
            {
                var seen = new HashSet<long>();
                var chests = Main.chest;
                if (chests != null)
                    for (var index = 0; index < chests.Length; index++)
                    {
                        var chest = chests[index];
                        if (chest == null) continue;
                        var tile = InWorld(chest.x, chest.y) ? Main.tile[chest.x, chest.y] : null;
                        var container = tile != null && tile.active()
                            ? _content.GetTile(tile.type) as TimfContainerTile : null;
                        if (container != null)
                        {
                            var record = CaptureWhole(chest, container.ContentKey);
                            state.Records.Add(record);
                            seen.Add(PositionKey(record.X, record.Y));
                            // SaveChests walks only Main.chest. Leave its private coordinate map
                            // untouched and restore the exact same object after serialization.
                            state.Removed.Add(new RemovedChest { Index = index, Chest = chest });
                            chests[index] = null;
                            continue;
                        }

                        var partial = CaptureCustomItems(chest, state.Blanked);
                        if (partial.Items.Count > 0)
                        {
                            state.Records.Add(partial);
                            seen.Add(PositionKey(partial.X, partial.Y));
                        }
                    }

                PreserveUnresolved(state.Records, seen);
                _save = state;
                _log.Info("Content chest sidecar: stashed " + state.Removed.Count
                          + " custom container(s) and " + state.Blanked.Count
                          + " custom item slot(s) for vanilla serialization");
            }
            catch (Exception ex)
            {
                RestoreRuntime(state);
                _log.Error("Content chest sidecar: preparing save failed", ex);
            }
        }

        private static void AfterSave()
        {
            var state = _save;
            _save = null;
            if (state == null) return;
            try { Write(SidecarPath(), state.Records); }
            catch (Exception ex) { _log?.Error("Content chest sidecar: committing save failed", ex); }
            finally { RestoreRuntime(state); }
        }

        private static Exception SaveFinalizer(Exception __exception)
        {
            // The postfix commits successful saves. On failure, keep the previous sidecar and
            // immediately restore all live container objects/items.
            if (__exception != null)
            {
                var state = _save;
                _save = null;
                RestoreRuntime(state);
            }
            return __exception;
        }

        private static ChestRecord CaptureWhole(Chest chest, string key)
        {
            var record = new ChestRecord
                { X = chest.x, Y = chest.y, ContainerTileKey = key, Name = chest.name };
            if (chest.item != null)
                for (var i = 0; i < chest.item.Length; i++)
                {
                    var item = CaptureItem(i, chest.item[i], true);
                    if (item != null) record.Items.Add(item);
                }
            return record;
        }

        private static ChestRecord CaptureCustomItems(Chest chest, List<RemovedItem> blanked)
        {
            var record = new ChestRecord { X = chest.x, Y = chest.y, Name = chest.name };
            if (chest.item == null) return record;
            for (var i = 0; i < chest.item.Length; i++)
            {
                var item = CaptureItem(i, chest.item[i], false);
                if (item == null) continue;
                record.Items.Add(item);
                blanked.Add(new RemovedItem { Items = chest.item, Index = i, Item = chest.item[i] });
                chest.item[i] = NewAirItem();
            }
            return record;
        }

        private static ItemRecord CaptureItem(int slot, Item item, bool includeVanilla)
        {
            if (item == null || item.type <= 0 || item.stack <= 0) return null;
            var definition = _content.GetItem(item.type);
            if (definition == null && !includeVanilla) return null;
            return new ItemRecord
            {
                Slot = slot, ContentKey = definition?.ContentKey,
                VanillaType = definition == null ? item.type : 0,
                Stack = item.stack, Prefix = item.prefix, Favorited = item.favorited,
            };
        }

        private static void RestoreRuntime(SaveState state)
        {
            if (state == null) return;
            try
            {
                foreach (var removed in state.Removed)
                    if (Main.chest != null && removed.Index >= 0 && removed.Index < Main.chest.Length)
                        Main.chest[removed.Index] = removed.Chest;
                foreach (var blanked in state.Blanked)
                    if (blanked.Items != null && blanked.Index >= 0 && blanked.Index < blanked.Items.Length)
                        blanked.Items[blanked.Index] = blanked.Item;
            }
            catch (Exception ex) { _log?.Error("Content chest sidecar: restoring save stash failed", ex); }
        }

        private static void PreserveUnresolved(List<ChestRecord> output, HashSet<long> seen)
        {
            var retained = new List<ChestRecord>();
            foreach (var record in Unresolved)
            {
                if (!InWorld(record.X, record.Y))
                    continue;
                ChestRecord currentRecord = null;
                if (seen.Contains(PositionKey(record.X, record.Y)))
                {
                    foreach (var candidate in output)
                        if (candidate.X == record.X && candidate.Y == record.Y)
                        { currentRecord = candidate; break; }

                    if (currentRecord == null)
                        continue;
                    // If the container itself is available but one of its item mods is not,
                    // the unresolved record is also marked "whole". Merge those air slots into
                    // the freshly captured same-kind container instead of discarding them.
                    if (record.IsWholeContainer)
                    {
                        if (!currentRecord.IsWholeContainer
                            || !string.Equals(record.ContainerTileKey,
                                currentRecord.ContainerTileKey, StringComparison.Ordinal))
                            continue;
                        var keptInWhole = new ChestRecord
                        {
                            X = record.X, Y = record.Y,
                            ContainerTileKey = record.ContainerTileKey, Name = record.Name,
                        };
                        foreach (var item in record.Items)
                            if (!HasSlot(currentRecord, item.Slot))
                            {
                                currentRecord.Items.Add(item);
                                keptInWhole.Items.Add(item);
                            }
                        if (keptInWhole.Items.Count > 0) retained.Add(keptInWhole);
                        continue;
                    }
                    if (currentRecord.IsWholeContainer) continue;
                    var currentIndex = Chest.FindChest(record.X, record.Y);
                    var currentItems = currentIndex >= 0 ? Main.chest[currentIndex]?.item : null;
                    if (currentItems == null) continue;
                    var keptAlongsideCurrent = new ChestRecord
                        { X = record.X, Y = record.Y, Name = record.Name };
                    foreach (var item in record.Items)
                        if (item.Slot >= 0 && item.Slot < currentItems.Length
                            && (currentItems[item.Slot] == null || currentItems[item.Slot].type == 0)
                            && !HasSlot(currentRecord, item.Slot))
                        {
                            currentRecord.Items.Add(item);
                            keptAlongsideCurrent.Items.Add(item);
                        }
                    if (keptAlongsideCurrent.Items.Count > 0)
                        retained.Add(keptAlongsideCurrent);
                    continue;
                }
                var chestIndex = Chest.FindChest(record.X, record.Y);
                if (record.IsWholeContainer)
                {
                    if (chestIndex < 0)
                    {
                        output.Add(record);
                        retained.Add(record);
                    }
                    continue;
                }
                if (chestIndex < 0 || Main.chest[chestIndex]?.item == null) continue;
                var current = Main.chest[chestIndex].item;
                var kept = new ChestRecord { X = record.X, Y = record.Y, Name = record.Name };
                foreach (var item in record.Items)
                    if (item.Slot >= 0 && item.Slot < current.Length
                        && (current[item.Slot] == null || current[item.Slot].type == 0))
                        kept.Items.Add(item);
                if (kept.Items.Count > 0)
                {
                    output.Add(kept);
                    retained.Add(kept);
                }
            }
            Unresolved.Clear();
            Unresolved.AddRange(retained);
        }

        private static bool HasSlot(ChestRecord record, int slot)
        {
            foreach (var item in record.Items)
                if (item.Slot == slot) return true;
            return false;
        }

        private static void AfterLoad()
        {
            Unresolved.Clear();
            _pendingLoad = null;
            _restoreDelayFrames = -1;
            if (_content == null || !_content.IsActivated) return;
            var path = SidecarPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                _pendingLoad = Read(path);
                _log.Info("Content chest sidecar: queued " + _pendingLoad.Count + " record(s)");
            }
            catch (Exception ex) { _log.Error("Content chest sidecar: loading failed", ex); }
        }

        private static void AfterWorldLoaded()
        {
            if (_pendingLoad != null) _restoreDelayFrames = 2;
        }

        internal static void PollDeferredRestore()
        {
            if (_restoreDelayFrames < 0 || Main.gameMenu) return;
            if (_restoreDelayFrames-- > 0) return;
            var records = _pendingLoad;
            _pendingLoad = null;
            _restoreDelayFrames = -1;
            if (records == null) return;

            var restoredContainers = 0;
            var restoredItems = 0;
            foreach (var record in records)
                try
                {
                    Chest chest;
                    if (record.IsWholeContainer)
                    {
                        var tileType = _content.TileType(record.ContainerTileKey);
                        var tile = InWorld(record.X, record.Y) ? Main.tile[record.X, record.Y] : null;
                        if (tileType <= 0 || tile == null || !tile.active() || tile.type != tileType
                            || !(_content.GetTile(tileType) is TimfContainerTile))
                        {
                            Unresolved.Add(record);
                            continue;
                        }
                        var index = Chest.FindChest(record.X, record.Y);
                        if (index < 0) index = Chest.CreateChest(record.X, record.Y);
                        chest = index >= 0 ? Main.chest[index] : null;
                        if (chest == null) { Unresolved.Add(record); continue; }
                        chest.name = record.Name ?? "";
                        restoredContainers++;
                    }
                    else
                    {
                        var index = Chest.FindChest(record.X, record.Y);
                        chest = index >= 0 ? Main.chest[index] : null;
                        if (chest == null) continue;
                    }

                    var missing = ApplyItems(chest, record);
                    restoredItems += record.Items.Count - missing.Items.Count;
                    if (missing.Items.Count > 0) Unresolved.Add(missing);
                }
                catch (Exception ex)
                {
                    Unresolved.Add(record);
                    _log?.Error("Content chest sidecar: restore failed at "
                                + record.X + "," + record.Y, ex);
                }
            _log.Info("Content chest sidecar: restored " + restoredContainers
                      + " custom container(s) and " + restoredItems + " item(s); retained "
                      + Unresolved.Count + " unresolved record(s)");
        }

        private static ChestRecord ApplyItems(Chest chest, ChestRecord source)
        {
            var missing = new ChestRecord
            {
                X = source.X, Y = source.Y, ContainerTileKey = source.ContainerTileKey,
                Name = source.Name,
            };
            if (chest.item == null) { missing.Items.AddRange(source.Items); return missing; }
            foreach (var record in source.Items)
            {
                if (record.Slot < 0 || record.Slot >= chest.item.Length) continue;
                var type = string.IsNullOrEmpty(record.ContentKey)
                    ? record.VanillaType : _content.ItemType(record.ContentKey);
                if (type <= 0) { missing.Items.Add(record); continue; }
                var existing = chest.item[record.Slot];
                if (existing != null && existing.type > 0 && existing.stack > 0) continue;
                var item = existing ?? new Item();
                item.SetDefaults(type);
                item.stack = Math.Max(1, record.Stack);
                item.Prefix(record.Prefix);
                item.favorited = record.Favorited;
                chest.item[record.Slot] = item;
            }
            return missing;
        }

        private static Item NewAirItem()
        {
            var item = new Item();
            item.SetDefaults(0);
            return item;
        }

        private static void Write(string path, List<ChestRecord> records)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (records.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(records.Count);
                foreach (var record in records) record.Write(writer);
                writer.Flush();
                stream.Flush(true);
            }
            AtomicReplace(temp, path);
            _log.Info("Content chest sidecar: atomically wrote " + records.Count
                      + " record(s) to " + Path.GetFileName(path));
        }

        private static List<ChestRecord> Read(string path)
        {
            var records = new List<ChestRecord>();
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (!string.Equals(reader.ReadString(), Magic, StringComparison.Ordinal))
                    throw new InvalidDataException("Not a TIMF chest sidecar");
                var version = reader.ReadInt32();
                if (version != FormatVersion)
                    throw new InvalidDataException("Unsupported TIMF chest sidecar version " + version);
                var count = reader.ReadInt32();
                if (count < 0 || count > 100000)
                    throw new InvalidDataException("Invalid TIMF chest record count: " + count);
                for (var i = 0; i < count; i++) records.Add(ChestRecord.Read(reader));
            }
            return records;
        }

        private static void AtomicReplace(string temp, string path)
        {
            if (!File.Exists(path)) { File.Move(temp, path); return; }
            var backup = path + ".bak";
            try
            {
                File.Replace(temp, path, backup, true);
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Copy(path, backup, true);
                File.Delete(path);
                File.Move(temp, path);
                File.Delete(backup);
            }
        }

        private static string SidecarPath()
        {
            try
            {
                var path = Main.ActiveWorldFileData?.Path;
                if (string.IsNullOrEmpty(path)) path = Main.worldPathName;
                return string.IsNullOrEmpty(path) ? null : path + Extension;
            }
            catch { return null; }
        }

        private static bool InWorld(int x, int y)
        {
            return Main.tile != null && x >= 0 && y >= 0
                   && x < Main.maxTilesX && y < Main.maxTilesY;
        }

        private static long PositionKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }
    }
}
