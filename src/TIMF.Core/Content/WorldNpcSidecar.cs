using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.IO;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Persists framework NPCs by stable content key while keeping their process-local numeric
    /// ids out of the vanilla world file. Runtime NPC objects are restored even when saving fails.
    /// </summary>
    internal static class WorldNpcSidecar
    {
        private const string Magic = "TIMF-NPCS";
        private const int FormatVersion = 1;
        private const string Extension = ".timf-npcs";
        private static ContentManager _content;
        private static ILogger _log;
        private static SaveState _save;
        private static bool _sidecarReadFailed;
        private static readonly List<Record> Unresolved = new List<Record>();

        private sealed class SaveState
        {
            public readonly List<Record> Records = new List<Record>();
            public readonly List<NPC> Hidden = new List<NPC>();
            public readonly List<RoomState> Rooms = new List<RoomState>();
            public readonly List<int> ShimmeredTypes = new List<int>();
            public bool CommitAllowed = true;
        }

        private sealed class RoomState { public int Type; public Point Position; }

        private sealed class Record
        {
            public string ContentKey;
            public string GivenName;
            public Vector2 Position;
            public bool Homeless;
            public int HomeX;
            public int HomeY;
            public int Variation;
            public bool HomelessDespawn;
            public bool Shimmered;

            public void Write(BinaryWriter writer)
            {
                writer.Write(ContentKey ?? ""); writer.Write(GivenName ?? "");
                writer.Write(Position.X); writer.Write(Position.Y); writer.Write(Homeless);
                writer.Write(HomeX); writer.Write(HomeY); writer.Write(Variation);
                writer.Write(HomelessDespawn); writer.Write(Shimmered);
            }

            public static Record Read(BinaryReader reader) => new Record
            {
                ContentKey = reader.ReadString(), GivenName = reader.ReadString(),
                Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                Homeless = reader.ReadBoolean(), HomeX = reader.ReadInt32(),
                HomeY = reader.ReadInt32(), Variation = reader.ReadInt32(),
                HomelessDespawn = reader.ReadBoolean(), Shimmered = reader.ReadBoolean(),
            };
        }

        internal static void Install(Harmony harmony, ContentManager content, ILogger log)
        {
            _content = content; _log = log;
            WorldGen.Hooks.OnWorldLoad += RestoreAfterWorldLoad;
            try
            {
                var save = AccessTools.Method(typeof(WorldFile), "InternalSaveWorld",
                    new[] { typeof(bool), typeof(bool), typeof(bool) });
                if (save == null) log.Error("Content NPC sidecar: WorldFile.InternalSaveWorld not found");
                else harmony.Patch(save,
                    prefix: new HarmonyMethod(typeof(WorldNpcSidecar), nameof(BeforeSave)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(WorldNpcSidecar), nameof(AfterSave)) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(typeof(WorldNpcSidecar), nameof(SaveFinalizer)) { priority = Priority.Last });
                log.Info("Content NPC sidecar: stable-key world persistence installed");
            }
            catch (Exception ex) { log.Error("Content NPC sidecar: install failed", ex); }
        }

        private static void BeforeSave()
        {
            if (_save != null || _content == null || !_content.IsActivated || string.IsNullOrEmpty(SidecarPath())) return;
            var state = new SaveState();
            state.CommitAllowed = !_sidecarReadFailed;
            try
            {
                foreach (var npc in Main.npc)
                {
                    if (npc == null || !npc.active) continue;
                    var def = _content.GetNpc(npc.type);
                    if (def == null) continue;
                    // All custom ids must be hidden, including transient NPCs which deliberately
                    // do not enter the sidecar.
                    state.Hidden.Add(npc);
                    if (def.SaveToWorld) state.Records.Add(Capture(npc, def.ContentKey));
                    npc.active = false;
                }
                // TownRoomManager serializes its own list of raw NPC ids independently of
                // SaveNPCs, so hiding NPC instances alone is not sufficient.
                foreach (var def in _content.RegisteredNpcs)
                {
                    if (def.Type >= 0 && def.Type < NPC.ShimmeredTownNPCs.Length
                        && NPC.ShimmeredTownNPCs[def.Type])
                    {
                        state.ShimmeredTypes.Add(def.Type);
                        NPC.ShimmeredTownNPCs[def.Type] = false;
                    }
                    Point room;
                    if (!WorldGen.TownManager.HasRoom(def.Type, out room)) continue;
                    state.Rooms.Add(new RoomState { Type = def.Type, Position = room });
                    WorldGen.TownManager.KickOut(def.Type);
                    SetHasRoom(def.Type, false);
                }
                state.Records.AddRange(Unresolved);
                _save = state;
            }
            catch (Exception ex)
            {
                RestoreRuntime(state);
                _log?.Error("Content NPC sidecar: preparing save failed", ex);
            }
        }

        private static void AfterSave()
        {
            var state = _save; _save = null;
            if (state == null) return;
            try
            {
                if (state.CommitAllowed) Write(SidecarPath(), state.Records);
                else _log?.Warn("Content NPC sidecar: previous file could not be read; preserving it instead of overwriting");
            }
            catch (Exception ex) { _log?.Error("Content NPC sidecar: committing save failed", ex); }
            finally { RestoreRuntime(state); }
        }

        private static Exception SaveFinalizer(Exception __exception)
        {
            if (__exception != null) { var state = _save; _save = null; RestoreRuntime(state); }
            return __exception;
        }

        private static Record Capture(NPC npc, string key) => new Record
        {
            ContentKey = key, GivenName = npc.GivenName, Position = npc.position,
            Homeless = npc.homeless, HomeX = npc.homeTileX, HomeY = npc.homeTileY,
            Variation = npc.townNpcVariationIndex, HomelessDespawn = npc.homelessDespawn,
            Shimmered = npc.type >= 0 && npc.type < NPC.ShimmeredTownNPCs.Length
                        && NPC.ShimmeredTownNPCs[npc.type],
        };

        private static void RestoreRuntime(SaveState state)
        {
            if (state == null) return;
            foreach (var npc in state.Hidden) if (npc != null) npc.active = true;
            foreach (var room in state.Rooms)
                WorldGen.TownManager.SetRoom(room.Type, room.Position);
            foreach (var type in state.ShimmeredTypes)
                if (type >= 0 && type < NPC.ShimmeredTownNPCs.Length)
                    NPC.ShimmeredTownNPCs[type] = true;
        }

        private static void RestoreAfterWorldLoad()
        {
            Unresolved.Clear();
            _sidecarReadFailed = false;
            var records = Read(SidecarPath());
            var restored = 0;
            foreach (var record in records)
            {
                var type = _content?.NpcType(record.ContentKey) ?? 0;
                var def = type > 0 ? _content.GetNpc(type) : null;
                if (def == null || !_content.IsSessionAllowed(def.ModId)) { Unresolved.Add(record); continue; }
                var slot = FindFreeSlot();
                if (slot < 0) { Unresolved.Add(record); continue; }
                try
                {
                    var npc = Main.npc[slot] ?? (Main.npc[slot] = new NPC());
                    npc.SetDefaults(type, default(NPCSpawnParams));
                    npc.position = record.Position; npc.GivenName = record.GivenName;
                    npc.homeless = record.Homeless; npc.homeTileX = record.HomeX;
                    npc.homeTileY = record.HomeY; npc.townNpcVariationIndex = record.Variation;
                    npc.homelessDespawn = record.HomelessDespawn; npc.active = true;
                    if (record.Shimmered && type < NPC.ShimmeredTownNPCs.Length)
                        NPC.ShimmeredTownNPCs[type] = true;
                    if (!npc.homeless) WorldGen.TownManager.SetRoom(type, npc.homeTileX, npc.homeTileY);
                    restored++;
                }
                catch (Exception ex)
                {
                    Unresolved.Add(record);
                    _log?.Error("Content NPC sidecar: restore failed for " + record.ContentKey, ex);
                }
            }
            if (records.Count > 0) _log?.Info("Content NPC sidecar: restored " + restored
                + " NPC(s), retained " + Unresolved.Count + " unresolved record(s)");
        }

        private static int FindFreeSlot()
        {
            for (var i = 0; i < Main.npc.Length; i++)
                if (Main.npc[i] == null || !Main.npc[i].active) return i;
            return -1;
        }

        private static void SetHasRoom(int type, bool value)
        {
            try
            {
                var manager = WorldGen.TownManager;
                var field = manager?.GetType().GetField("_hasRoom",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var flags = field?.GetValue(manager) as bool[];
                if (flags != null && type >= 0 && type < flags.Length) flags[type] = value;
            }
            catch (Exception ex) { _log?.Error("Content NPC sidecar: room-state update failed", ex); }
        }

        private static void Write(string path, List<Record> records)
        {
            if (string.IsNullOrEmpty(path)) return;
            var temp = path + ".tmp";
            using (var stream = File.Create(temp)) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(FormatVersion); writer.Write(records.Count);
                foreach (var record in records) record.Write(writer);
                stream.Flush(true);
            }
            AtomicReplace(temp, path);
        }

        private static List<Record> Read(string path)
        {
            var result = new List<Record>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
            try
            {
                using (var stream = File.OpenRead(path)) using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadString() != Magic || reader.ReadInt32() != FormatVersion)
                        throw new InvalidDataException("Unsupported TIMF NPC sidecar");
                    var count = reader.ReadInt32();
                    if (count < 0 || count > 10000) throw new InvalidDataException("Invalid NPC record count: " + count);
                    for (var i = 0; i < count; i++) result.Add(Record.Read(reader));
                }
            }
            catch (Exception ex)
            {
                _sidecarReadFailed = true;
                _log?.Error("Content NPC sidecar: read failed; preserving current file", ex);
                result.Clear();
            }
            return result;
        }

        private static void AtomicReplace(string temp, string path)
        {
            var backup = path + ".bak";
            if (!File.Exists(path)) { File.Move(temp, path); return; }
            try { File.Replace(temp, path, backup, true); }
            catch (PlatformNotSupportedException) { File.Delete(path); File.Move(temp, path); }
        }

        private static string SidecarPath()
        {
            try
            {
                var path = Main.ActiveWorldFileData?.Path ?? Main.worldPathName;
                return string.IsNullOrEmpty(path) ? null : path + Extension;
            }
            catch { return null; }
        }
    }
}
