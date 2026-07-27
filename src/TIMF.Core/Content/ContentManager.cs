using System;
using System.Collections.Generic;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Owns every registered content definition and the id space they live in.
    ///
    /// Load order is deliberate:
    /// <list type="number">
    /// <item>collect definitions from each content mod (no ids exist yet),</item>
    /// <item>allocate ids for every definition,</item>
    /// <item>grow the vanilla arrays so those ids are addressable,</item>
    /// <item>only then run <see cref="TimfItem.SetStaticDefaults"/>, which is allowed to
    ///       touch id-indexed arrays.</item>
    /// </list>
    /// Reversing 3 and 4 would have SetStaticDefaults write past the end of arrays that had
    /// not grown yet.
    /// </summary>
    internal sealed class ContentManager : IContentLookup
    {
        private readonly ILogger _log;
        private readonly ContentIdStore _idStore;
        private readonly VanillaArrayExpander _expander;
        private readonly Func<string, bool> _sessionAllowed;

        private readonly List<TimfItem> _pending = new List<TimfItem>();
        private readonly List<TimfTile> _pendingTiles = new List<TimfTile>();
        private readonly List<TimfWall> _pendingWalls = new List<TimfWall>();
        private readonly Dictionary<int, TimfItem> _byId = new Dictionary<int, TimfItem>();
        private readonly Dictionary<int, TimfTile> _tilesById = new Dictionary<int, TimfTile>();
        private readonly Dictionary<int, TimfWall> _wallsById = new Dictionary<int, TimfWall>();
        private readonly Dictionary<Type, TimfItem> _byType = new Dictionary<Type, TimfItem>();
        private readonly Dictionary<Type, TimfTile> _tilesByType = new Dictionary<Type, TimfTile>();
        private readonly Dictionary<Type, TimfWall> _wallsByType = new Dictionary<Type, TimfWall>();
        private readonly Dictionary<string, TimfItem> _byKey =
            new Dictionary<string, TimfItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimfTile> _tilesByKey =
            new Dictionary<string, TimfTile>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimfWall> _wallsByKey =
            new Dictionary<string, TimfWall>(StringComparer.Ordinal);

        public ContentManager(ILogger log, string configDir, Func<string, bool> sessionAllowed = null)
        {
            _log = log;
            _sessionAllowed = sessionAllowed;
            VanillaItemCount = ReadVanillaItemCount();
            VanillaTileCount = ReadVanillaTileCount();
            VanillaWallCount = ReadVanillaWallCount();
            _idStore = new ContentIdStore(log, configDir, VanillaItemCount, VanillaTileCount, VanillaWallCount);
            _expander = new VanillaArrayExpander(log);
        }

        /// <summary>Ids below this belong to the base game.</summary>
        public int VanillaItemCount { get; }

        public int VanillaTileCount { get; }
        public int VanillaWallCount { get; }

        public bool HasContent => _byId.Count > 0 || _tilesById.Count > 0 || _wallsById.Count > 0;

        public IReadOnlyDictionary<int, TimfItem> ItemsById => _byId;
        public IReadOnlyDictionary<int, TimfTile> TilesById => _tilesById;
        public IReadOnlyDictionary<int, TimfWall> WallsById => _wallsById;

        /// <summary>Collect one mod's declarations. Safe to call before ids exist.</summary>
        public void Collect(IContentMod mod, string modId)
        {
            if (mod == null)
                return;

            var before = _pending.Count;
            var beforeTiles = _pendingTiles.Count;
            var beforeWalls = _pendingWalls.Count;
            try
            {
                mod.AddContent(new ContentRegistry(_log, modId, _pending, _pendingTiles, _pendingWalls));
            }
            catch (Exception ex)
            {
                _log.Error("Content: AddContent failed for " + modId, ex);
                // Drop whatever this mod managed to register so a half-declared mod cannot
                // claim ids it will never back with working definitions.
                _pending.RemoveRange(before, _pending.Count - before);
                _pendingTiles.RemoveRange(beforeTiles, _pendingTiles.Count - beforeTiles);
                _pendingWalls.RemoveRange(beforeWalls, _pendingWalls.Count - beforeWalls);
                return;
            }

            _log.Info("Content: " + modId + " registered " + (_pending.Count - before)
                      + " item(s), " + (_pendingTiles.Count - beforeTiles) + " tile(s), "
                      + (_pendingWalls.Count - beforeWalls) + " wall(s)");
        }

        /// <summary>
        /// Assign ids, grow vanilla arrays, then run static defaults. Call once after every
        /// content mod has been collected.
        /// </summary>
        public void FinalizeRegistration()
        {
            if (_pending.Count == 0 && _pendingTiles.Count == 0 && _pendingWalls.Count == 0)
            {
                _log.Info("Content: no content registered; id space untouched");
                return;
            }

            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in _pending)
            {
                if (!duplicates.Add(item.ContentKey))
                {
                    _log.Error("Content: duplicate content key '" + item.ContentKey
                               + "' — the later definition is ignored");
                    continue;
                }

                try
                {
                    item.Type = _idStore.GetOrAllocateItemId(item.ContentKey);
                }
                catch (Exception ex)
                {
                    _log.Error("Content: could not allocate an id for " + item.ContentKey, ex);
                    continue;
                }

                _byId[item.Type] = item;
                _byKey[item.ContentKey] = item;
                _byType[item.GetType()] = item;
                _ordered.Add(item);
            }

            duplicates.Clear();
            foreach (var tile in _pendingTiles)
            {
                if (!duplicates.Add(tile.ContentKey))
                {
                    _log.Error("Content: duplicate tile content key '" + tile.ContentKey
                               + "' — the later definition is ignored");
                    continue;
                }

                try
                {
                    tile.Type = _idStore.GetOrAllocateTileId(tile.ContentKey);
                }
                catch (Exception ex)
                {
                    _log.Error("Content: could not allocate a tile id for " + tile.ContentKey, ex);
                    continue;
                }

                _tilesById[tile.Type] = tile;
                _tilesByKey[tile.ContentKey] = tile;
                _tilesByType[tile.GetType()] = tile;
                _orderedTiles.Add(tile);
            }

            duplicates.Clear();
            foreach (var wall in _pendingWalls)
            {
                if (!duplicates.Add(wall.ContentKey)) continue;
                try { wall.Type = _idStore.GetOrAllocateWallId(wall.ContentKey); }
                catch (Exception ex) { _log.Error("Content: could not allocate wall id for " + wall.ContentKey, ex); continue; }
                _wallsById[wall.Type] = wall;
                _wallsByKey[wall.ContentKey] = wall;
                _wallsByType[wall.GetType()] = wall;
                _orderedWalls.Add(wall);
            }

            _idStore.Flush();
            _log.Info("Content: reserved " + _byId.Count + " item id(s) " + _idStore.ItemIdBase
                      + ".." + (_idStore.NextItemId - 1) + " and " + _tilesById.Count
                      + " tile id(s) " + _idStore.TileIdBase + ".." + (_idStore.NextTileId - 1)
                      + " and " + _wallsById.Count + " wall id(s) " + _idStore.WallIdBase
                      + ".." + (_idStore.NextWallId - 1)
                      + "; waiting for vanilla content setup");
        }

        private bool _activated;

        /// <summary>True once the id space has been widened and the arrays actually grown.</summary>
        public bool IsActivated => _activated;

        internal bool IsSessionAllowed(string modId)
        {
            return _sessionAllowed == null || _sessionAllowed(modId);
        }

        internal bool IsSessionAllowed(TimfItem definition)
        {
            return definition != null && IsSessionAllowed(definition.ModId);
        }

        internal bool IsSessionAllowed(TimfTile definition)
        {
            return definition != null && IsSessionAllowed(definition.ModId);
        }

        internal bool IsSessionAllowed(TimfWall definition)
        {
            return definition != null && IsSessionAllowed(definition.ModId);
        }

        /// <summary>
        /// Widen the id space. Must run <em>after</em> the game finishes its own content setup.
        ///
        /// Terraria builds its content during <c>Main.Initialize_AlmostEverything</c>, which
        /// happens on the splash screen — well after TIMF is injected. Expanding before that
        /// point makes vanilla's own setup walk ids it has no data for: <c>ItemID.Sets
        /// .PostSetupContent</c> iterates to <c>ItemID.Count</c> and looks each id up in
        /// <c>ItemID.Search</c>, so a widened count crashes it with KeyNotFoundException.
        /// </summary>
        public void ActivateAfterVanillaSetup()
        {
            if (_activated || !HasContent)
                return;
            _activated = true;

            // Grab the pre-expansion texture array so captured references can be found by
            // identity afterwards.
            var oldItemTextures = ReadItemTextureArray();
            var oldTileTextures = ReadTextureArray("Tile");
            var oldWallTextures = ReadTextureArray("Wall");

            if (_byId.Count > 0 && !_expander.ExpandItemArrays(_idStore.NextItemId))
            {
                _log.Error("Content: array expansion failed — custom items are NOT safe this session");
                _byId.Clear();
                _byKey.Clear();
                _byType.Clear();
                _ordered.Clear();
                return;
            }

            if (_tilesById.Count > 0 && !_expander.ExpandTileArrays(_idStore.NextTileId))
            {
                _log.Error("Content: tile array expansion failed — custom tiles are NOT safe this session");
                _tilesById.Clear();
                _tilesByKey.Clear();
                _tilesByType.Clear();
                _orderedTiles.Clear();
            }
            else if (_tilesById.Count > 0)
            {
                ExpandExistingPlayerTileArrays(_idStore.NextTileId);
            }

            if (_wallsById.Count > 0 && !_expander.ExpandWallArrays(_idStore.NextWallId))
            {
                _log.Error("Content: wall array expansion failed — custom walls are NOT safe this session");
                _wallsById.Clear(); _wallsByKey.Clear(); _wallsByType.Clear(); _orderedWalls.Clear();
            }

            RegisterSearchNames(typeof(Terraria.ID.ItemID), _ordered, x => x.ContentKey, x => x.Type);
            RegisterSearchNames(typeof(Terraria.ID.TileID), _orderedTiles, x => x.ContentKey, x => x.Type);
            RegisterSearchNames(typeof(Terraria.ID.WallID), _orderedWalls, x => x.ContentKey, x => x.Type);

            // Expansion leaves the new TextureAssets.Item slots null, and vanilla's inventory
            // draw dereferences them without a guard. Park a valid vanilla asset in every new
            // slot now so the window before real textures load cannot break rendering.
            try
            {
                var slots = TextureAssetSlots.Resolve(_log);
                if (slots != null)
                {
                    var filled = slots.BackfillNulls(VanillaItemCount);
                    _log.Info("Content: backfilled " + filled + " texture slot(s) to keep them non-null");
                }

                var repointed = TextureAssetSlots.RepointCapturedArrays(
                    _log, oldItemTextures, ReadItemTextureArray());
                _log.Info("Content: repointed " + repointed + " renderer(s) holding the old texture array");

                var tileSlots = TextureAssetSlots.Resolve(_log, "Tile");
                if (tileSlots != null)
                {
                    var filled = tileSlots.BackfillNulls(VanillaTileCount);
                    _log.Info("Content: backfilled " + filled + " tile texture slot(s)");
                }
                TextureAssetSlots.RepointCapturedArrays(_log, oldTileTextures, ReadTextureArray("Tile"));

                var wallSlots = TextureAssetSlots.Resolve(_log, "Wall");
                if (wallSlots != null)
                    wallSlots.BackfillNulls(VanillaWallCount);
                TextureAssetSlots.RepointCapturedArrays(_log, oldWallTextures, ReadTextureArray("Wall"));
            }
            catch (Exception ex)
            {
                _log.Error("Content: texture slot backfill failed", ex);
            }

            foreach (var item in _byId.Values)
            {
                try { item.SetStaticDefaults(); }
                catch (Exception ex) { _log.Error("Content: SetStaticDefaults failed for " + item.ContentKey, ex); }
            }

            foreach (var tile in _tilesById.Values)
            {
                try { tile.SetStaticDefaults(); }
                catch (Exception ex) { _log.Error("Content: SetStaticDefaults failed for tile " + tile.ContentKey, ex); }
                if (tile.PlacementTemplateTile >= 0)
                    TileObjectDataRegistry.CloneTemplate(tile.Type, tile.PlacementTemplateTile, _log);
            }


            foreach (var wall in _wallsById.Values)
            {
                try { wall.SetStaticDefaults(); }
                catch (Exception ex) { _log.Error("Content: SetStaticDefaults failed for wall " + wall.ContentKey, ex); }
            }

            var recipeCountBefore = Terraria.Recipe.numRecipes;
            foreach (var item in _byId.Values)
            {
                try { item.AddRecipes(); }
                catch (Exception ex) { _log.Error("Content: AddRecipes failed for " + item.ContentKey, ex); }
            }
            _log.Info("Content: registered " + (Terraria.Recipe.numRecipes - recipeCountBefore)
                      + " custom recipe(s)");

            _log.Info("Content: " + _byId.Count + " item(s), " + _tilesById.Count
                      + " tile(s), and " + _wallsById.Count + " wall(s) live");
        }

        /// <summary>
        /// Give every modded id an entry in <c>ItemID.Search</c>. Anything that later walks the
        /// id range and asks for a name — vanilla does this in several places — would otherwise
        /// throw the same KeyNotFoundException that expanding too early caused.
        /// </summary>
        private void RegisterSearchNames<T>(
            Type idType,
            IEnumerable<T> definitions,
            Func<T, string> keyOf,
            Func<T, int> typeOf)
        {
            try
            {
                var search = idType
                    .GetField("Search", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?.GetValue(null);
                if (search == null)
                {
                    _log.Warn("Content: " + idType.Name + ".Search unavailable; modded ids will have no name entry");
                    return;
                }

                var add = search.GetType().GetMethod("Add", new[] { typeof(string), typeof(int) });
                if (add == null)
                {
                    _log.Warn("Content: " + idType.Name + ".Search has no Add(string,int); skipping name registration");
                    return;
                }

                var added = 0;
                foreach (var definition in definitions)
                {
                    var key = keyOf(definition);
                    try
                    {
                        add.Invoke(search, new object[] { "TIMF_" + key.Replace('/', '_'), typeOf(definition) });
                        added++;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("Content: could not register search name for " + key
                                  + ": " + (ex.InnerException ?? ex).Message);
                    }
                }
                _log.Info("Content: registered " + added + " id(s) into " + idType.Name + ".Search");
            }
            catch (Exception ex)
            {
                _log.Warn("Content: " + idType.Name + ".Search registration failed: " + ex.Message);
            }
        }

        // ---- IContentLookup ----

        public int ItemType<TItem>() where TItem : TimfItem
        {
            TimfItem def;
            return _byType.TryGetValue(typeof(TItem), out def) ? def.Type : 0;
        }

        public int ItemType(string contentKey)
        {
            TimfItem def;
            return contentKey != null && _byKey.TryGetValue(contentKey, out def) ? def.Type : 0;
        }

        public TimfItem GetItem(int type)
        {
            TimfItem def;
            return _byId.TryGetValue(type, out def) ? def : null;
        }

        public int TileType<TTile>() where TTile : TimfTile
        {
            TimfTile def;
            return _tilesByType.TryGetValue(typeof(TTile), out def) ? def.Type : 0;
        }

        public int TileType(string contentKey)
        {
            TimfTile def;
            return contentKey != null && _tilesByKey.TryGetValue(contentKey, out def) ? def.Type : 0;
        }

        public TimfTile GetTile(int type)
        {
            TimfTile def;
            return _tilesById.TryGetValue(type, out def) ? def : null;
        }

        public int WallType<TWall>() where TWall : TimfWall
        {
            TimfWall def;
            return _wallsByType.TryGetValue(typeof(TWall), out def) ? def.Type : 0;
        }

        public int WallType(string contentKey)
        {
            TimfWall def;
            return contentKey != null && _wallsByKey.TryGetValue(contentKey, out def) ? def.Type : 0;
        }

        public TimfWall GetWall(int type)
        {
            TimfWall def;
            return _wallsById.TryGetValue(type, out def) ? def : null;
        }

        public bool IsModdedTile(int type)
        {
            return type >= VanillaTileCount && _tilesById.ContainsKey(type);
        }

        public bool IsModdedWall(int type)
        {
            return type >= VanillaWallCount && _wallsById.ContainsKey(type);
        }

        /// <summary>True when the id belongs to TIMF content rather than the base game.</summary>
        public bool IsModdedItem(int type)
        {
            return type >= VanillaItemCount && _byId.ContainsKey(type);
        }

        private readonly List<TimfItem> _ordered = new List<TimfItem>();
        private readonly List<TimfTile> _orderedTiles = new List<TimfTile>();
        private readonly List<TimfWall> _orderedWalls = new List<TimfWall>();

        public IReadOnlyList<TimfItem> RegisteredItems => _ordered;
        public IReadOnlyList<TimfTile> RegisteredTiles => _orderedTiles;
        public IReadOnlyList<TimfWall> RegisteredWalls => _orderedWalls;

        /// <summary>Set by the texture loader once it has run, purely so diagnostics can show it.</summary>
        internal int TexturesLoaded { get; set; }
        internal int TexturesPlaceholder { get; set; }
        internal int TileTexturesLoaded { get; set; }
        internal int TileTexturesPlaceholder { get; set; }
        internal int WallTexturesLoaded { get; set; }
        internal int WallTexturesPlaceholder { get; set; }

        public IReadOnlyList<string> Report()
        {
            var lines = new List<string>();
            int liveCount;
            try { liveCount = Terraria.ID.ItemID.Count; }
            catch { liveCount = -1; }

            lines.Add("Vanilla item count : " + VanillaItemCount);
            lines.Add("ItemID.Count now   : " + liveCount
                      + (liveCount > VanillaItemCount ? "  (expanded)" : "  (NOT expanded)"));
            lines.Add("Id base            : " + _idStore.ItemIdBase);
            lines.Add("Ids allocated      : " + _byId.Count);
            lines.Add("Arrays expanded    : " + _expander.ExpandedArrayCount);
            lines.Add("Textures           : " + TexturesLoaded + " loaded, "
                      + TexturesPlaceholder + " placeholder");
            int liveTileCount;
            try { liveTileCount = Terraria.ID.TileID.Count; }
            catch { liveTileCount = -1; }
            lines.Add("Vanilla tile count : " + VanillaTileCount);
            lines.Add("TileID.Count now   : " + liveTileCount
                      + (liveTileCount > VanillaTileCount ? "  (expanded)" : "  (NOT expanded)"));
            lines.Add("Tile ids allocated : " + _tilesById.Count);
            lines.Add("Tile textures      : " + TileTexturesLoaded + " loaded, "
                      + TileTexturesPlaceholder + " placeholder");
            int liveWallCount;
            try { liveWallCount = Terraria.ID.WallID.Count; }
            catch { liveWallCount = -1; }
            lines.Add("Vanilla wall count : " + VanillaWallCount);
            lines.Add("WallID.Count now   : " + liveWallCount
                      + (liveWallCount > VanillaWallCount ? "  (expanded)" : "  (NOT expanded)"));
            lines.Add("Wall ids allocated : " + _wallsById.Count);
            lines.Add("Wall textures      : " + WallTexturesLoaded + " loaded, "
                      + WallTexturesPlaceholder + " placeholder");
            return lines;
        }

        /// <summary>Current <c>TextureAssets.Item</c>, read late-bound (see TextureAssetSlots).</summary>
        private Array ReadItemTextureArray()
        {
            return ReadTextureArray("Item");
        }

        private Array ReadTextureArray(string fieldName)
        {
            try
            {
                return typeof(Terraria.Main).Assembly
                    .GetType("Terraria.GameContent.TextureAssets")
                    ?.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?.GetValue(null) as Array;
            }
            catch
            {
                return null;
            }
        }

        private int ReadVanillaItemCount()
        {
            try { return Terraria.ID.ItemID.Count; }
            catch (Exception ex)
            {
                _log.Error("Content: could not read ItemID.Count", ex);
                return short.MaxValue;
            }
        }

        private int ReadVanillaTileCount()
        {
            try { return Terraria.ID.TileID.Count; }
            catch (Exception ex)
            {
                _log.Error("Content: could not read TileID.Count", ex);
                return ushort.MaxValue;
            }
        }

        private int ReadVanillaWallCount()
        {
            try { return Terraria.ID.WallID.Count; }
            catch (Exception ex)
            {
                _log.Error("Content: could not read WallID.Count", ex);
                return ushort.MaxValue;
            }
        }

        /// <summary>
        /// Player.adjTile is an instance array, so the assembly-wide static-array expander
        /// cannot see it. Player instances created after TileID.Count changes get the new size
        /// automatically; players constructed during boot need their existing array replaced.
        /// </summary>
        private void ExpandExistingPlayerTileArrays(int newCount)
        {
            var expanded = 0;
            try
            {
                var players = Terraria.Main.player;
                if (players == null)
                    return;

                foreach (var player in players)
                {
                    if (player == null || player.adjTile == null || player.adjTile.Length >= newCount)
                        continue;

                    var grown = new bool[newCount];
                    Array.Copy(player.adjTile, grown, player.adjTile.Length);
                    player.adjTile = grown;
                    expanded++;
                }
                _log.Info("Content: expanded Player.adjTile for " + expanded + " existing player instance(s)");
            }
            catch (Exception ex)
            {
                _log.Error("Content: expanding Player.adjTile failed", ex);
            }
        }
    }
}
