using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Stable content-key → id map, persisted to <c>config/content-ids.json</c>.
    ///
    /// Vanilla saves store raw numeric item ids, so an id assigned today must mean the same
    /// thing tomorrow or existing worlds and characters silently mutate. Two rules keep that true:
    ///
    /// 1. An id, once handed out for a content key, is never reassigned — not even after the
    ///    mod that owned it is uninstalled. Removed content leaves a tombstone so its id can
    ///    never be recycled into somebody else's item.
    /// 2. Allocation starts at a fixed base well above the vanilla range rather than at the
    ///    current <c>ItemID.Count</c>. If it tracked the vanilla count, a Terraria update that
    ///    added items would shift every modded id down onto vanilla ones.
    /// </summary>
    internal sealed class ContentIdStore
    {
        /// <summary>
        /// First id handed to modded items. Sits above vanilla (6147 in 1.4.5.6) with room for
        /// the game to keep growing. <c>ItemID.Count</c> is an Int16, so ids must stay under
        /// 32767 — this base still leaves roughly 22k slots.
        /// </summary>
        public const int DefaultItemIdBase = 10000;
        public const int DefaultTileIdBase = 2000;
        public const int DefaultWallIdBase = 1000;
        public const int DefaultNpcIdBase = 1000;
        public const int DefaultProjectileIdBase = 2000;
        public const int DefaultBuffIdBase = 1000;

        private const string BaseKey = "#itemIdBase";
        private const string TileBaseKey = "#tileIdBase";
        private const string TileKeyPrefix = "tile:";
        private const string WallBaseKey = "#wallIdBase";
        private const string WallKeyPrefix = "wall:";
        private const string NpcBaseKey = "#npcIdBase";
        private const string NpcKeyPrefix = "npc:";
        private const string ProjectileBaseKey = "#projectileIdBase";
        private const string ProjectileKeyPrefix = "projectile:";
        private const string BuffBaseKey = "#buffIdBase";
        private const string BuffKeyPrefix = "buff:";

        private readonly ILogger _log;
        private readonly string _path;
        private readonly Dictionary<string, int> _itemIds =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _tileIds =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _wallIds =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _npcIds =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _projectileIds =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _buffIds =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private int _itemIdBase = DefaultItemIdBase;
        private int _nextItemId = DefaultItemIdBase;
        private int _tileIdBase = DefaultTileIdBase;
        private int _nextTileId = DefaultTileIdBase;
        private int _wallIdBase = DefaultWallIdBase;
        private int _nextWallId = DefaultWallIdBase;
        private int _npcIdBase = DefaultNpcIdBase;
        private int _nextNpcId = DefaultNpcIdBase;
        private int _projectileIdBase = DefaultProjectileIdBase;
        private int _nextProjectileId = DefaultProjectileIdBase;
        private int _buffIdBase = DefaultBuffIdBase;
        private int _nextBuffId = DefaultBuffIdBase;
        private bool _dirty;

        public ContentIdStore(
            ILogger log,
            string configDir,
            int vanillaItemCount,
            int vanillaTileCount,
            int vanillaWallCount,
            int vanillaNpcCount = 0,
            int vanillaProjectileCount = 0,
            int vanillaBuffCount = 0)
        {
            _log = log;
            _path = Path.Combine(configDir ?? "", "content-ids.json");
            Load();

            if (vanillaItemCount > _itemIdBase)
            {
                // Terraria grew past our base. Every stored id now overlaps real vanilla items;
                // keeping them would silently turn saved modded items into vanilla ones.
                _log.Error("Vanilla item count (" + vanillaItemCount + ") has grown past the TIMF content id base ("
                           + _itemIdBase + "). All modded item ids must be reassigned, which INVALIDATES modded "
                           + "items in existing saves. Back up your worlds and players before continuing.");
                _itemIds.Clear();
                _itemIdBase = vanillaItemCount + 2000;
                _dirty = true;
            }

            _nextItemId = _itemIdBase;
            foreach (var id in _itemIds.Values)
            {
                if (id >= _nextItemId)
                    _nextItemId = id + 1;
            }

            if (vanillaTileCount > _tileIdBase)
            {
                _log.Error("Vanilla tile count (" + vanillaTileCount
                           + ") has grown past the TIMF tile id base (" + _tileIdBase
                           + "). Tile ids are being reassigned; existing .timf-tiles sidecars "
                           + "must be backed up before loading their worlds.");
                _tileIds.Clear();
                _tileIdBase = vanillaTileCount + 500;
                _dirty = true;
            }

            _nextTileId = _tileIdBase;
            foreach (var id in _tileIds.Values)
            {
                if (id >= _nextTileId)
                    _nextTileId = id + 1;
            }

            if (vanillaWallCount > _wallIdBase)
            {
                _log.Error("Vanilla wall count (" + vanillaWallCount
                           + ") has grown past the TIMF wall id base (" + _wallIdBase
                           + "). Wall ids are being reassigned.");
                _wallIds.Clear();
                _wallIdBase = vanillaWallCount + 250;
                _dirty = true;
            }
            _nextWallId = _wallIdBase;
            foreach (var id in _wallIds.Values)
                if (id >= _nextWallId) _nextWallId = id + 1;

            if (vanillaNpcCount > _npcIdBase)
            {
                _log.Error("Vanilla NPC count has grown past the TIMF NPC id base; NPC ids are being reassigned.");
                _npcIds.Clear();
                _npcIdBase = vanillaNpcCount + 250;
                _dirty = true;
            }
            _nextNpcId = _npcIdBase;
            foreach (var id in _npcIds.Values)
                if (id >= _nextNpcId) _nextNpcId = id + 1;

            if (vanillaProjectileCount > _projectileIdBase)
            {
                _log.Error("Vanilla projectile count has grown past the TIMF projectile id base; projectile ids are being reassigned.");
                _projectileIds.Clear(); _projectileIdBase = vanillaProjectileCount + 500; _dirty = true;
            }
            _nextProjectileId = _projectileIdBase;
            foreach (var id in _projectileIds.Values) if (id >= _nextProjectileId) _nextProjectileId = id + 1;

            if (vanillaBuffCount > _buffIdBase)
            {
                _log.Error("Vanilla buff count has grown past the TIMF buff id base; buff ids are being reassigned.");
                _buffIds.Clear(); _buffIdBase = vanillaBuffCount + 250; _dirty = true;
            }
            _nextBuffId = _buffIdBase;
            foreach (var id in _buffIds.Values) if (id >= _nextBuffId) _nextBuffId = id + 1;
        }

        public int ItemIdBase => _itemIdBase;

        /// <summary>Exclusive upper bound of allocated item ids; equals the base when nothing is registered.</summary>
        public int NextItemId => _nextItemId;

        public int TileIdBase => _tileIdBase;

        /// <summary>Exclusive upper bound of allocated tile ids.</summary>
        public int NextTileId => _nextTileId;
        public int WallIdBase => _wallIdBase;
        public int NextWallId => _nextWallId;
        public int NpcIdBase => _npcIdBase;
        public int NextNpcId => _nextNpcId;
        public int ProjectileIdBase => _projectileIdBase;
        public int NextProjectileId => _nextProjectileId;
        public int BuffIdBase => _buffIdBase;
        public int NextBuffId => _nextBuffId;

        public IReadOnlyDictionary<string, int> ItemIds => _itemIds;
        public IReadOnlyDictionary<string, int> TileIds => _tileIds;
        public IReadOnlyDictionary<string, int> WallIds => _wallIds;
        public IReadOnlyDictionary<string, int> NpcIds => _npcIds;
        public IReadOnlyDictionary<string, int> ProjectileIds => _projectileIds;
        public IReadOnlyDictionary<string, int> BuffIds => _buffIds;

        /// <summary>
        /// Stable id for a content key, allocating one on first sight. Keys are case-sensitive
        /// because they end up in save-affecting identity.
        /// </summary>
        public int GetOrAllocateItemId(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey))
                throw new ArgumentException("contentKey is required", nameof(contentKey));

            int existing;
            if (_itemIds.TryGetValue(contentKey, out existing))
                return existing;

            if (_nextItemId > short.MaxValue - 1)
            {
                throw new InvalidOperationException(
                    "Out of modded item ids: ItemID.Count is an Int16 so ids cannot exceed "
                    + (short.MaxValue - 1) + ".");
            }

            var id = _nextItemId++;
            _itemIds[contentKey] = id;
            _dirty = true;
            _log.Info("Allocated item id " + id + " -> " + contentKey);
            return id;
        }

        public int GetOrAllocateTileId(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey))
                throw new ArgumentException("contentKey is required", nameof(contentKey));

            int existing;
            if (_tileIds.TryGetValue(contentKey, out existing))
                return existing;

            if (_nextTileId > ushort.MaxValue - 1)
            {
                throw new InvalidOperationException(
                    "Out of modded tile ids: Tile.type is UInt16 so ids cannot exceed "
                    + (ushort.MaxValue - 1) + ".");
            }

            var id = _nextTileId++;
            _tileIds[contentKey] = id;
            _dirty = true;
            _log.Info("Allocated tile id " + id + " -> " + contentKey);
            return id;
        }

        public int GetOrAllocateWallId(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey))
                throw new ArgumentException("contentKey is required", nameof(contentKey));
            int existing;
            if (_wallIds.TryGetValue(contentKey, out existing)) return existing;
            if (_nextWallId > ushort.MaxValue - 1)
                throw new InvalidOperationException("Out of modded wall ids");
            var id = _nextWallId++;
            _wallIds[contentKey] = id;
            _dirty = true;
            _log.Info("Allocated wall id " + id + " -> " + contentKey);
            return id;
        }

        public int GetOrAllocateNpcId(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey))
                throw new ArgumentException("contentKey is required", nameof(contentKey));
            int existing;
            if (_npcIds.TryGetValue(contentKey, out existing)) return existing;
            if (_nextNpcId > short.MaxValue - 1)
                throw new InvalidOperationException("Out of modded NPC ids");
            var id = _nextNpcId++;
            _npcIds[contentKey] = id;
            _dirty = true;
            _log.Info("Allocated NPC id " + id + " -> " + contentKey);
            return id;
        }

        public int GetOrAllocateProjectileId(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey)) throw new ArgumentException("contentKey is required", nameof(contentKey));
            int existing; if (_projectileIds.TryGetValue(contentKey, out existing)) return existing;
            if (_nextProjectileId > short.MaxValue - 1) throw new InvalidOperationException("Out of modded projectile ids");
            var id = _nextProjectileId++; _projectileIds[contentKey] = id; _dirty = true;
            _log.Info("Allocated projectile id " + id + " -> " + contentKey); return id;
        }

        public int GetOrAllocateBuffId(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey)) throw new ArgumentException("contentKey is required", nameof(contentKey));
            int existing; if (_buffIds.TryGetValue(contentKey, out existing)) return existing;
            if (_nextBuffId > ushort.MaxValue - 1) throw new InvalidOperationException("Out of network-safe modded buff ids");
            var id = _nextBuffId++; _buffIds[contentKey] = id; _dirty = true;
            _log.Info("Allocated buff id " + id + " -> " + contentKey); return id;
        }

        /// <summary>Content key holding an id, or null when the id was never handed out.</summary>
        public string KeyForItemId(int id)
        {
            foreach (var kv in _itemIds)
            {
                if (kv.Value == id)
                    return kv.Key;
            }
            return null;
        }

        public void Flush()
        {
            if (!_dirty)
                return;
            Save();
            _dirty = false;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                var text = File.ReadAllText(_path);
                var i = 0;
                while (i < text.Length)
                {
                    var q1 = text.IndexOf('"', i);
                    if (q1 < 0) break;
                    var q2 = text.IndexOf('"', q1 + 1);
                    if (q2 < 0) break;
                    var key = text.Substring(q1 + 1, q2 - q1 - 1);

                    var colon = text.IndexOf(':', q2 + 1);
                    if (colon < 0) break;

                    var end = colon + 1;
                    while (end < text.Length && text[end] != ',' && text[end] != '}' && text[end] != '\n')
                        end++;

                    var raw = text.Substring(colon + 1, end - colon - 1).Trim();
                    int value;
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    {
                        if (string.Equals(key, BaseKey, StringComparison.Ordinal))
                            _itemIdBase = value;
                        else if (string.Equals(key, TileBaseKey, StringComparison.Ordinal))
                            _tileIdBase = value;
                        else if (key.StartsWith(TileKeyPrefix, StringComparison.Ordinal))
                            _tileIds[key.Substring(TileKeyPrefix.Length)] = value;
                        else if (string.Equals(key, WallBaseKey, StringComparison.Ordinal))
                            _wallIdBase = value;
                        else if (key.StartsWith(WallKeyPrefix, StringComparison.Ordinal))
                            _wallIds[key.Substring(WallKeyPrefix.Length)] = value;
                        else if (string.Equals(key, NpcBaseKey, StringComparison.Ordinal))
                            _npcIdBase = value;
                        else if (key.StartsWith(NpcKeyPrefix, StringComparison.Ordinal))
                            _npcIds[key.Substring(NpcKeyPrefix.Length)] = value;
                        else if (string.Equals(key, ProjectileBaseKey, StringComparison.Ordinal))
                            _projectileIdBase = value;
                        else if (key.StartsWith(ProjectileKeyPrefix, StringComparison.Ordinal))
                            _projectileIds[key.Substring(ProjectileKeyPrefix.Length)] = value;
                        else if (string.Equals(key, BuffBaseKey, StringComparison.Ordinal))
                            _buffIdBase = value;
                        else if (key.StartsWith(BuffKeyPrefix, StringComparison.Ordinal))
                            _buffIds[key.Substring(BuffKeyPrefix.Length)] = value;
                        else if (!string.IsNullOrWhiteSpace(key))
                            _itemIds[key] = value;
                    }

                    i = end;
                }

                _log.Info("Content id map loaded (" + _itemIds.Count + " item id(s), base " + _itemIdBase
                          + "; " + _tileIds.Count + " tile id(s), base " + _tileIdBase
                          + "; " + _wallIds.Count + " wall id(s), base " + _wallIdBase
                          + "; " + _npcIds.Count + " NPC id(s), base " + _npcIdBase
                          + "; " + _projectileIds.Count + " projectile id(s), base " + _projectileIdBase
                          + "; " + _buffIds.Count + " buff id(s), base " + _buffIdBase
                          + ") from " + _path);
            }
            catch (Exception ex)
            {
                // Losing this map silently would reshuffle ids and corrupt saves, so make it loud.
                _log.Error("Failed to read content-ids.json — modded item ids may be reassigned "
                           + "and existing saves may lose modded items: " + ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.Append("  \"").Append(BaseKey).Append("\": ").Append(_itemIdBase);
                sb.AppendLine(",");
                sb.Append("  \"").Append(TileBaseKey).Append("\": ").Append(_tileIdBase);
                sb.AppendLine(",");
                sb.Append("  \"").Append(WallBaseKey).Append("\": ").Append(_wallIdBase);

                sb.AppendLine(",");
                sb.Append("  \"").Append(NpcBaseKey).Append("\": ").Append(_npcIdBase);
                sb.AppendLine(",");
                sb.Append("  \"").Append(ProjectileBaseKey).Append("\": ").Append(_projectileIdBase);
                sb.AppendLine(",");
                sb.Append("  \"").Append(BuffBaseKey).Append("\": ").Append(_buffIdBase);

                foreach (var kv in _itemIds)
                {
                    sb.AppendLine(",");
                    sb.Append("  \"").Append(Escape(kv.Key)).Append("\": ").Append(kv.Value);
                }


                foreach (var kv in _tileIds)
                {
                    sb.AppendLine(",");
                    sb.Append("  \"").Append(Escape(TileKeyPrefix + kv.Key)).Append("\": ").Append(kv.Value);
                }

                foreach (var kv in _wallIds)
                {
                    sb.AppendLine(",");
                    sb.Append("  \"").Append(Escape(WallKeyPrefix + kv.Key)).Append("\": ").Append(kv.Value);
                }

                foreach (var kv in _npcIds)
                {
                    sb.AppendLine(",");
                    sb.Append("  \"").Append(Escape(NpcKeyPrefix + kv.Key)).Append("\": ").Append(kv.Value);
                }

                foreach (var kv in _projectileIds)
                {
                    sb.AppendLine(",");
                    sb.Append("  \"").Append(Escape(ProjectileKeyPrefix + kv.Key)).Append("\": ").Append(kv.Value);
                }

                foreach (var kv in _buffIds)
                {
                    sb.AppendLine(",");
                    sb.Append("  \"").Append(Escape(BuffKeyPrefix + kv.Key)).Append("\": ").Append(kv.Value);
                }

                sb.AppendLine();
                sb.AppendLine("}");

                // Write via a temp file so a crash mid-write cannot leave a truncated map.
                var tmp = _path + ".tmp";
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(sb.ToString()); writer.Flush(); stream.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(tmp, _path, _path + ".bak", true);
                else File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to write content-ids.json: " + ex.Message);
            }
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
