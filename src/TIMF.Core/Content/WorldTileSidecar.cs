using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Keeps custom tile ids out of Terraria's .wld format. Immediately before the actual
    /// synchronous world write, TIMF records custom cells in a sidecar and swaps those cells
    /// for inactive clones. The original Tile objects are restored after the write. On load,
    /// the sidecar overlays the custom cells after vanilla has reconstructed the world.
    /// </summary>
    internal static class WorldTileSidecar
    {
        private const int FormatVersion = 4;
        private const string Magic = "TIMF-TILES";

        private static ContentManager _content;
        private static ILogger _log;
        private static List<RemovedTile> _saveStash;
        private static int? _saveTileCount;
        private static int? _saveWallCount;
        private static List<TileRecord> _pendingLoad;
        private static int _restoreDelayFrames = -1;
        private static readonly Dictionary<long, TileRecord> Unresolved =
            new Dictionary<long, TileRecord>();
        private static readonly Dictionary<long, GrassOrigin> GrassOrigins =
            new Dictionary<long, GrassOrigin>();

        internal static void RecordGrassOrigin(int x, int y, int substrateType)
        {
            if (!InWorld(x, y) || substrateType < 0 || substrateType > ushort.MaxValue)
                return;
            var definition = _content?.GetTile(substrateType);
            GrassOrigins[PositionKey(x, y)] = new GrassOrigin
            {
                VanillaType = definition == null ? substrateType : -1,
                ContentKey = definition?.ContentKey,
            };
        }

        internal static int TakeGrassOrigin(int x, int y, TIMF.Content.TimfGrassTile grass)
        {
            var key = PositionKey(x, y);
            GrassOrigin origin;
            GrassOrigins.TryGetValue(key, out origin);
            GrassOrigins.Remove(key);

            var type = -1;
            if (origin != null)
            {
                if (!string.IsNullOrEmpty(origin.ContentKey))
                {
                    var resolved = _content?.TileType(origin.ContentKey) ?? 0;
                    if (resolved > 0)
                        type = resolved;
                }
                else
                {
                    type = origin.VanillaType;
                }
            }
            if (type < 0)
                type = grass?.DefaultSubstrateTileType ?? -1;
            return type >= 0 && type <= ushort.MaxValue && grass != null && grass.CanGrowOn(type)
                ? type : -1;
        }

        internal static void Install(Harmony harmony, ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;

            // WorldFile.LoadWorld returns before the local player enters the world. Terraria
            // continues section/framing initialization in between, which can overwrite tiles
            // restored from a LoadWorld postfix. OnWorldLoad is raised from OnEnterWorld and is
            // therefore the first point where the reconstructed world is truly authoritative.
            WorldGen.Hooks.OnWorldLoad += AfterWorldLoaded;

            try
            {
                var save = AccessTools.Method(typeof(WorldFile), "InternalSaveWorld",
                    new[] { typeof(bool), typeof(bool), typeof(bool) });
                if (save == null)
                {
                    log.Error("Content: WorldFile.InternalSaveWorld not found — custom tile ids may enter .wld");
                }
                else
                {
                    harmony.Patch(save,
                        prefix: new HarmonyMethod(typeof(WorldTileSidecar), nameof(BeforeSave))
                            { priority = Priority.Low },
                        postfix: new HarmonyMethod(typeof(WorldTileSidecar), nameof(AfterSave)),
                        finalizer: new HarmonyMethod(typeof(WorldTileSidecar), nameof(SaveFinalizer)));
                }

                var load = AccessTools.Method(typeof(WorldFile), "LoadWorld", Type.EmptyTypes);
                if (load == null)
                    log.Error("Content: WorldFile.LoadWorld not found — custom tile sidecars cannot be restored");
                else
                    harmony.Patch(load,
                        postfix: new HarmonyMethod(typeof(WorldTileSidecar), nameof(AfterLoad)));
            }
            catch (Exception ex)
            {
                log.Error("Content: custom tile sidecar hooks failed to install", ex);
            }
        }

        private static void BeforeSave()
        {
            if (_content == null || !_content.IsActivated
                || (_content.RegisteredTiles.Count == 0 && _content.RegisteredWalls.Count == 0
                    && Unresolved.Count == 0))
                return;
            if (_saveStash != null)
                return;

            var path = SidecarPath();
            if (string.IsNullOrEmpty(path))
                return;

            var records = new Dictionary<long, TileRecord>();
            var stash = new List<RemovedTile>();
            var tileLayers = 0;
            var wallLayers = 0;

            try
            {
                for (var x = 0; x < Main.maxTilesX; x++)
                {
                    for (var y = 0; y < Main.maxTilesY; y++)
                    {
                        var tile = Main.tile[x, y];
                        if (tile == null)
                            continue;

                        var tileDefinition = tile.active() ? _content.GetTile(tile.type) : null;
                        var wallDefinition = tile.wall > 0 ? _content.GetWall(tile.wall) : null;
                        if (tileDefinition == null && wallDefinition == null)
                            continue;
                        if (tileDefinition != null) tileLayers++;
                        if (wallDefinition != null) wallLayers++;

                        GrassOrigin grassOrigin;
                        GrassOrigins.TryGetValue(PositionKey(x, y), out grassOrigin);
                        var record = TileRecord.FromTile(x, y,
                            tileDefinition?.ContentKey, wallDefinition?.ContentKey, tile,
                            tileDefinition is TIMF.Content.TimfGrassTile ? grassOrigin : null);
                        records[PositionKey(x, y)] = record;
                        stash.Add(new RemovedTile(x, y, tile));

                        var vanillaSafe = new Tile(tile);
                        if (tileDefinition != null)
                        {
                            vanillaSafe.active(false);
                            vanillaSafe.type = 0;
                            vanillaSafe.frameX = -1;
                            vanillaSafe.frameY = -1;
                        }
                        if (wallDefinition != null)
                        {
                            vanillaSafe.wall = 0;
                            vanillaSafe.ClearWallPaintAndCoating();
                            vanillaSafe.wallFrameX(0);
                            vanillaSafe.wallFrameY(0);
                        }
                        Main.tile[x, y] = vanillaSafe;
                    }
                }

                // Preserve entries whose owning mod is absent, but only while their cell is
                // still empty. If the player built something there without the mod, that edit
                // wins and the stale sidecar entry is discarded.
                foreach (var kv in Unresolved)
                {
                    var r = kv.Value;
                    if (!InWorld(r.X, r.Y))
                        continue;
                    var current = Main.tile[r.X, r.Y];
                    var tileCellFree = string.IsNullOrEmpty(r.TileContentKey)
                                       || current == null || !current.active();
                    var wallCellFree = string.IsNullOrEmpty(r.WallContentKey)
                                       || current == null || current.wall == 0;
                    if (tileCellFree && wallCellFree)
                        records[kv.Key] = r;
                }

                Write(path, records.Values);
                _saveStash = stash;
                _saveTileCount = ReadTileCount();
                _saveWallCount = ReadWallCount();
                WriteTileCount(_content.VanillaTileCount);
                WriteWallCount(_content.VanillaWallCount);
                _log.Info("Content tile sidecar: stashed " + stash.Count
                          + " cell(s) [tile layers=" + tileLayers + ", wall layers=" + wallLayers
                          + "], wrote " + records.Count + " record(s); TileID.Count "
                          + _saveTileCount + " -> " + _content.VanillaTileCount
                          + ", WallID.Count " + _saveWallCount + " -> " + _content.VanillaWallCount
                          + " for vanilla serialization");
            }
            catch (Exception ex)
            {
                RestoreTileCount();
                RestoreStash(stash);
                _saveStash = null;
                _log.Error("Content tile sidecar: preparing world save failed", ex);
            }
        }

        private static void AfterSave()
        {
            RestoreSaveStash();
        }

        private static Exception SaveFinalizer(Exception __exception)
        {
            RestoreSaveStash();
            return __exception;
        }

        private static void RestoreSaveStash()
        {
            var stash = _saveStash;
            _saveStash = null;
            RestoreTileCount();
            if (stash != null)
                RestoreStash(stash);
        }

        private static int ReadTileCount()
        {
            return Convert.ToInt32(typeof(Terraria.ID.TileID)
                .GetField("Count", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null));
        }

        private static void WriteTileCount(int value)
        {
            var field = typeof(Terraria.ID.TileID)
                .GetField("Count", BindingFlags.Public | BindingFlags.Static);
            if (field == null)
                throw new MissingFieldException("Terraria.ID.TileID.Count");
            field.SetValue(null, Convert.ChangeType(value, field.FieldType));
        }

        private static int ReadWallCount()
        {
            return Convert.ToInt32(typeof(Terraria.ID.WallID)
                .GetField("Count", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null));
        }

        private static void WriteWallCount(int value)
        {
            var field = typeof(Terraria.ID.WallID)
                .GetField("Count", BindingFlags.Public | BindingFlags.Static);
            if (field == null) throw new MissingFieldException("Terraria.ID.WallID.Count");
            field.SetValue(null, Convert.ChangeType(value, field.FieldType));
        }

        private static void RestoreTileCount()
        {
            if (!_saveTileCount.HasValue)
                return;
            var value = _saveTileCount.Value;
            _saveTileCount = null;
            try { WriteTileCount(value); }
            catch (Exception ex) { _log?.Error("Content tile sidecar: restoring TileID.Count failed", ex); }
            if (_saveWallCount.HasValue)
            {
                var wallValue = _saveWallCount.Value;
                _saveWallCount = null;
                try { WriteWallCount(wallValue); }
                catch (Exception ex) { _log?.Error("Content tile sidecar: restoring WallID.Count failed", ex); }
            }
        }

        private static void RestoreStash(List<RemovedTile> stash)
        {
            if (stash == null)
                return;
            foreach (var e in stash)
            {
                if (InWorld(e.X, e.Y))
                    Main.tile[e.X, e.Y] = e.Tile;
            }
        }

        private static void AfterLoad()
        {
            Unresolved.Clear();
            GrassOrigins.Clear();
            _pendingLoad = null;
            if (_content == null || !_content.IsActivated)
                return;

            var path = SidecarPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                _pendingLoad = Read(path);
                _log.Info("Content tile sidecar: queued " + _pendingLoad.Count
                          + " tile record(s) for post-enter-world restore");
            }
            catch (Exception ex)
            {
                _pendingLoad = null;
                _log.Error("Content tile sidecar: loading " + path + " failed", ex);
            }
        }

        private static void AfterWorldLoaded()
        {
            // OnWorldLoad is dispatched from the first OnEnterWorld handler. Other handlers
            // still run afterwards and may frame/clear cells. Arm a short main-thread delay;
            // GameHooks calls PollDeferredRestore from the normal in-world frame loop.
            if (_pendingLoad != null)
                _restoreDelayFrames = 2;
        }

        internal static void PollDeferredRestore()
        {
            if (_restoreDelayFrames < 0 || Main.gameMenu)
                return;
            if (_restoreDelayFrames-- > 0)
                return;

            var pending = _pendingLoad;
            _pendingLoad = null;
            _restoreDelayFrames = -1;
            if (pending == null || _content == null || !_content.IsActivated)
                return;

            var restored = 0;
            var restoredTiles = 0;
            var restoredWalls = 0;
            try
            {
                foreach (var record in pending)
                {
                    if (!InWorld(record.X, record.Y))
                        continue;

                    var type = string.IsNullOrEmpty(record.TileContentKey)
                        ? 0 : _content.TileType(record.TileContentKey);
                    var wall = string.IsNullOrEmpty(record.WallContentKey)
                        ? 0 : _content.WallType(record.WallContentKey);
                    if ((!string.IsNullOrEmpty(record.TileContentKey) && (type <= 0 || type > ushort.MaxValue))
                        || (!string.IsNullOrEmpty(record.WallContentKey) && (wall <= 0 || wall > ushort.MaxValue)))
                    {
                        Unresolved[PositionKey(record.X, record.Y)] = record;
                        continue;
                    }

                    var current = Main.tile[record.X, record.Y] ?? new Tile();
                    var applied = record.ApplyTo(current, (ushort)type, (ushort)wall);
                    var tileDefinition = type > 0 ? _content.GetTile(type) : null;
                    if (tileDefinition != null && tileDefinition.PlacementTemplateTile < 0)
                    {
                        // Migrate frames produced by the pre-v3 vanilla framing bug. A simple
                        // one-frame definition always draws from the first 16x16 cell.
                        applied.frameX = 0;
                        applied.frameY = 0;
                    }
                    Main.tile[record.X, record.Y] = applied;
                    if (tileDefinition is TIMF.Content.TimfGrassTile
                        && (record.GrassSubstrateVanillaType >= 0
                            || !string.IsNullOrEmpty(record.GrassSubstrateContentKey)))
                    {
                        GrassOrigins[PositionKey(record.X, record.Y)] = new GrassOrigin
                        {
                            VanillaType = record.GrassSubstrateVanillaType,
                            ContentKey = record.GrassSubstrateContentKey,
                        };
                    }
                    restored++;
                    if (type > 0) restoredTiles++;
                    if (wall > 0) restoredWalls++;
                }

                _log.Info("Content tile sidecar: restored " + restored
                          + " cell(s) after deferred enter-world [tile layers=" + restoredTiles
                          + ", wall layers=" + restoredWalls + "], retained " + Unresolved.Count
                          + " unresolved record(s)");
            }
            catch (Exception ex)
            {
                _log.Error("Content tile sidecar: post-enter-world restore failed", ex);
            }
        }

        private static string SidecarPath()
        {
            try
            {
                var worldPath = Main.ActiveWorldFileData?.Path;
                if (string.IsNullOrEmpty(worldPath))
                    worldPath = Main.worldPathName;
                return string.IsNullOrEmpty(worldPath) ? null : worldPath + ".timf-tiles";
            }
            catch
            {
                return null;
            }
        }

        private static bool InWorld(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY
                   && Main.tile != null;
        }

        private static long PositionKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        private static void Write(string path, IEnumerable<TileRecord> records)
        {
            var list = new List<TileRecord>(records);
            if (list.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(list.Count);
                foreach (var r in list)
                    r.Write(writer);
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        private static List<TileRecord> Read(string path)
        {
            var records = new List<TileRecord>();
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (!string.Equals(reader.ReadString(), Magic, StringComparison.Ordinal))
                    throw new InvalidDataException("Not a TIMF tile sidecar");
                var version = reader.ReadInt32();
                if (version < 1 || version > FormatVersion)
                    throw new InvalidDataException("Unsupported TIMF tile sidecar version " + version);

                var count = reader.ReadInt32();
                if (count < 0 || count > 100000000)
                    throw new InvalidDataException("Invalid tile record count " + count);
                for (var i = 0; i < count; i++)
                    records.Add(TileRecord.Read(reader, version));
            }
            return records;
        }

        private sealed class RemovedTile
        {
            public RemovedTile(int x, int y, Tile tile)
            {
                X = x;
                Y = y;
                Tile = tile;
            }
            public int X { get; }
            public int Y { get; }
            public Tile Tile { get; }
        }

        private sealed class GrassOrigin
        {
            public int VanillaType = -1;
            public string ContentKey;
        }

        private sealed class TileRecord
        {
            public int X;
            public int Y;
            public string TileContentKey;
            public string WallContentKey;
            public ushort Wall;
            public byte Liquid;
            public ushort STileHeader;
            public byte BTileHeader;
            public byte BTileHeader2;
            public byte BTileHeader3;
            public short FrameX;
            public short FrameY;
            public short WallFrameX;
            public short WallFrameY;
            public byte TileColor;
            public byte WallColor;
            public byte Slope;
            public bool HalfBrick;
            public bool InvisibleBlock;
            public bool FullbrightBlock;
            public bool InvisibleWall;
            public bool FullbrightWall;
            public int GrassSubstrateVanillaType = -1;
            public string GrassSubstrateContentKey;

            public static TileRecord FromTile(
                int x, int y, string tileKey, string wallKey, Tile tile, GrassOrigin grassOrigin)
            {
                return new TileRecord
                {
                    X = x,
                    Y = y,
                    TileContentKey = tileKey,
                    WallContentKey = wallKey,
                    Wall = tile.wall,
                    Liquid = tile.liquid,
                    STileHeader = tile.sTileHeader,
                    BTileHeader = tile.bTileHeader,
                    BTileHeader2 = tile.bTileHeader2,
                    BTileHeader3 = tile.bTileHeader3,
                    FrameX = tile.frameX,
                    FrameY = tile.frameY,
                    WallFrameX = (short)tile.wallFrameX(),
                    WallFrameY = (short)tile.wallFrameY(),
                    TileColor = tile.color(),
                    WallColor = tile.wallColor(),
                    Slope = tile.slope(),
                    HalfBrick = tile.halfBrick(),
                    InvisibleBlock = tile.invisibleBlock(),
                    FullbrightBlock = tile.fullbrightBlock(),
                    InvisibleWall = tile.invisibleWall(),
                    FullbrightWall = tile.fullbrightWall(),
                    GrassSubstrateVanillaType = grassOrigin?.VanillaType ?? -1,
                    GrassSubstrateContentKey = grassOrigin?.ContentKey,
                };
            }

            public Tile ApplyTo(Tile vanilla, ushort type, ushort wall)
            {
                var result = new Tile(vanilla);
                if (type > 0)
                {
                    result.type = type;
                    result.active(true);
                    result.frameX = FrameX;
                    result.frameY = FrameY;
                    result.slope(Slope);
                    result.halfBrick(HalfBrick);
                    result.color(TileColor);
                    result.invisibleBlock(InvisibleBlock);
                    result.fullbrightBlock(FullbrightBlock);
                }
                if (wall > 0)
                {
                    result.wall = wall;
                    result.wallFrameX(WallFrameX);
                    result.wallFrameY(WallFrameY);
                    result.wallColor(WallColor);
                    result.invisibleWall(InvisibleWall);
                    result.fullbrightWall(FullbrightWall);
                }
                return result;
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(X);
                writer.Write(Y);
                writer.Write(TileContentKey ?? "");
                writer.Write(WallContentKey ?? "");
                writer.Write(FrameX);
                writer.Write(FrameY);
                writer.Write(WallFrameX);
                writer.Write(WallFrameY);
                writer.Write(TileColor);
                writer.Write(WallColor);
                writer.Write(Slope);
                writer.Write(HalfBrick);
                writer.Write(InvisibleBlock);
                writer.Write(FullbrightBlock);
                writer.Write(InvisibleWall);
                writer.Write(FullbrightWall);
                writer.Write(GrassSubstrateVanillaType);
                writer.Write(GrassSubstrateContentKey ?? "");
            }

            public static TileRecord Read(BinaryReader reader, int version)
            {
                var record = new TileRecord
                {
                    X = reader.ReadInt32(),
                    Y = reader.ReadInt32(),
                    TileContentKey = reader.ReadString(),
                };
                if (version >= 2)
                    record.WallContentKey = reader.ReadString();
                if (version >= 3)
                {
                    record.FrameX = reader.ReadInt16();
                    record.FrameY = reader.ReadInt16();
                    record.WallFrameX = reader.ReadInt16();
                    record.WallFrameY = reader.ReadInt16();
                    record.TileColor = reader.ReadByte();
                    record.WallColor = reader.ReadByte();
                    record.Slope = reader.ReadByte();
                    record.HalfBrick = reader.ReadBoolean();
                    record.InvisibleBlock = reader.ReadBoolean();
                    record.FullbrightBlock = reader.ReadBoolean();
                    record.InvisibleWall = reader.ReadBoolean();
                    record.FullbrightWall = reader.ReadBoolean();
                    if (version >= 4)
                    {
                        record.GrassSubstrateVanillaType = reader.ReadInt32();
                        record.GrassSubstrateContentKey = reader.ReadString();
                    }
                }
                else
                {
                    record.Wall = reader.ReadUInt16();
                    record.Liquid = reader.ReadByte();
                    record.STileHeader = reader.ReadUInt16();
                    record.BTileHeader = reader.ReadByte();
                    record.BTileHeader2 = reader.ReadByte();
                    record.BTileHeader3 = reader.ReadByte();
                    record.FrameX = reader.ReadInt16();
                    record.FrameY = reader.ReadInt16();
                    var legacy = new Tile
                    {
                        sTileHeader = record.STileHeader,
                        bTileHeader = record.BTileHeader,
                        bTileHeader2 = record.BTileHeader2,
                        bTileHeader3 = record.BTileHeader3,
                    };
                    record.WallFrameX = (short)legacy.wallFrameX();
                    record.WallFrameY = (short)legacy.wallFrameY();
                    record.TileColor = legacy.color();
                    record.WallColor = legacy.wallColor();
                    record.Slope = legacy.slope();
                    record.HalfBrick = legacy.halfBrick();
                    record.InvisibleBlock = legacy.invisibleBlock();
                    record.FullbrightBlock = legacy.fullbrightBlock();
                    record.InvisibleWall = legacy.invisibleWall();
                    record.FullbrightWall = legacy.fullbrightWall();
                    // v1/v2 restored raw shared headers and are the source of the historical
                    // invisible-layer corruption. Coatings were not a supported stable feature
                    // in those formats, so prefer visible recovery when migrating them to v3.
                    record.InvisibleBlock = false;
                    record.InvisibleWall = false;
                }
                return record;
            }
        }
    }
}
