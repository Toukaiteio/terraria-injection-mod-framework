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
        private readonly List<TimfNpc> _pendingNpcs = new List<TimfNpc>();
        private readonly List<TimfBiome> _pendingBiomes = new List<TimfBiome>();
        private readonly List<TimfProjectile> _pendingProjectiles = new List<TimfProjectile>();
        private readonly List<TimfBuff> _pendingBuffs = new List<TimfBuff>();
        private readonly Dictionary<int, TimfItem> _byId = new Dictionary<int, TimfItem>();
        private readonly Dictionary<int, TimfTile> _tilesById = new Dictionary<int, TimfTile>();
        private readonly Dictionary<int, TimfWall> _wallsById = new Dictionary<int, TimfWall>();
        private readonly Dictionary<int, TimfNpc> _npcsById = new Dictionary<int, TimfNpc>();
        private readonly Dictionary<int, TimfProjectile> _projectilesById = new Dictionary<int, TimfProjectile>();
        private readonly Dictionary<int, TimfBuff> _buffsById = new Dictionary<int, TimfBuff>();
        private readonly Dictionary<Type, TimfItem> _byType = new Dictionary<Type, TimfItem>();
        private readonly Dictionary<Type, TimfTile> _tilesByType = new Dictionary<Type, TimfTile>();
        private readonly Dictionary<Type, TimfWall> _wallsByType = new Dictionary<Type, TimfWall>();
        private readonly Dictionary<Type, TimfNpc> _npcsByType = new Dictionary<Type, TimfNpc>();
        private readonly Dictionary<Type, TimfProjectile> _projectilesByType = new Dictionary<Type, TimfProjectile>();
        private readonly Dictionary<Type, TimfBuff> _buffsByType = new Dictionary<Type, TimfBuff>();
        private readonly Dictionary<string, TimfItem> _byKey =
            new Dictionary<string, TimfItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimfTile> _tilesByKey =
            new Dictionary<string, TimfTile>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimfWall> _wallsByKey =
            new Dictionary<string, TimfWall>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimfNpc> _npcsByKey =
            new Dictionary<string, TimfNpc>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimfProjectile> _projectilesByKey =
            new Dictionary<string, TimfProjectile>(StringComparer.Ordinal);
        private readonly Dictionary<string, TimfBuff> _buffsByKey =
            new Dictionary<string, TimfBuff>(StringComparer.Ordinal);
        private readonly List<TimfBiome> _orderedBiomes = new List<TimfBiome>();

        public ContentManager(ILogger log, string configDir, Func<string, bool> sessionAllowed = null)
        {
            _log = log;
            _sessionAllowed = sessionAllowed;
            VanillaItemCount = ReadVanillaItemCount();
            VanillaTileCount = ReadVanillaTileCount();
            VanillaWallCount = ReadVanillaWallCount();
            VanillaNpcCount = ReadVanillaNpcCount();
            VanillaProjectileCount = ReadVanillaProjectileCount();
            VanillaBuffCount = ReadVanillaBuffCount();
            _idStore = new ContentIdStore(log, configDir, VanillaItemCount, VanillaTileCount,
                VanillaWallCount, VanillaNpcCount, VanillaProjectileCount, VanillaBuffCount);
            _expander = new VanillaArrayExpander(log);
        }

        /// <summary>Ids below this belong to the base game.</summary>
        public int VanillaItemCount { get; }

        public int VanillaTileCount { get; }
        public int VanillaWallCount { get; }
        public int VanillaNpcCount { get; }
        public int VanillaProjectileCount { get; }
        public int VanillaBuffCount { get; }

        public bool HasContent => _byId.Count > 0 || _tilesById.Count > 0 || _wallsById.Count > 0
                                  || _npcsById.Count > 0 || _orderedBiomes.Count > 0
                                  || _projectilesById.Count > 0 || _buffsById.Count > 0;

        public IReadOnlyDictionary<int, TimfItem> ItemsById => _byId;
        public IReadOnlyDictionary<int, TimfTile> TilesById => _tilesById;
        public IReadOnlyDictionary<int, TimfWall> WallsById => _wallsById;
        public IReadOnlyDictionary<int, TimfNpc> NpcsById => _npcsById;
        public IReadOnlyDictionary<int, TimfProjectile> ProjectilesById => _projectilesById;
        public IReadOnlyDictionary<int, TimfBuff> BuffsById => _buffsById;

        /// <summary>Collect one mod's declarations. Safe to call before ids exist.</summary>
        public void Collect(IContentMod mod, string modId)
        {
            if (mod == null)
                return;

            var before = _pending.Count;
            var beforeTiles = _pendingTiles.Count;
            var beforeWalls = _pendingWalls.Count;
            var beforeNpcs = _pendingNpcs.Count;
            var beforeBiomes = _pendingBiomes.Count;
            var beforeProjectiles = _pendingProjectiles.Count;
            var beforeBuffs = _pendingBuffs.Count;
            try
            {
                mod.AddContent(new ContentRegistry(_log, modId, _pending, _pendingTiles, _pendingWalls,
                    _pendingNpcs, _pendingBiomes, _pendingProjectiles, _pendingBuffs));
            }
            catch (Exception ex)
            {
                _log.Error("Content: AddContent failed for " + modId, ex);
                // Drop whatever this mod managed to register so a half-declared mod cannot
                // claim ids it will never back with working definitions.
                _pending.RemoveRange(before, _pending.Count - before);
                _pendingTiles.RemoveRange(beforeTiles, _pendingTiles.Count - beforeTiles);
                _pendingWalls.RemoveRange(beforeWalls, _pendingWalls.Count - beforeWalls);
                _pendingNpcs.RemoveRange(beforeNpcs, _pendingNpcs.Count - beforeNpcs);
                _pendingBiomes.RemoveRange(beforeBiomes, _pendingBiomes.Count - beforeBiomes);
                _pendingProjectiles.RemoveRange(beforeProjectiles, _pendingProjectiles.Count - beforeProjectiles);
                _pendingBuffs.RemoveRange(beforeBuffs, _pendingBuffs.Count - beforeBuffs);
                return;
            }

            _log.Info("Content: " + modId + " registered " + (_pending.Count - before)
                      + " item(s), " + (_pendingTiles.Count - beforeTiles) + " tile(s), "
                      + (_pendingWalls.Count - beforeWalls) + " wall(s), "
                      + (_pendingNpcs.Count - beforeNpcs) + " NPC(s), "
                      + (_pendingBiomes.Count - beforeBiomes) + " biome(s), "
                      + (_pendingProjectiles.Count - beforeProjectiles) + " projectile(s), "
                      + (_pendingBuffs.Count - beforeBuffs) + " buff(s)");
        }

        /// <summary>
        /// Assign ids, grow vanilla arrays, then run static defaults. Call once after every
        /// content mod has been collected.
        /// </summary>
        public void FinalizeRegistration()
        {
            if (_pending.Count == 0 && _pendingTiles.Count == 0 && _pendingWalls.Count == 0
                && _pendingNpcs.Count == 0 && _pendingBiomes.Count == 0
                && _pendingProjectiles.Count == 0 && _pendingBuffs.Count == 0)
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

            duplicates.Clear();
            foreach (var npc in _pendingNpcs)
            {
                if (!duplicates.Add(npc.ContentKey)) continue;
                try { npc.Type = _idStore.GetOrAllocateNpcId(npc.ContentKey); }
                catch (Exception ex) { _log.Error("Content: could not allocate NPC id for " + npc.ContentKey, ex); continue; }
                _npcsById[npc.Type] = npc;
                _npcsByKey[npc.ContentKey] = npc;
                _npcsByType[npc.GetType()] = npc;
                _orderedNpcs.Add(npc);
            }

            duplicates.Clear();
            foreach (var biome in _pendingBiomes)
                if (duplicates.Add(biome.ContentKey)) _orderedBiomes.Add(biome);

            duplicates.Clear();
            foreach (var projectile in _pendingProjectiles)
            {
                if (!duplicates.Add(projectile.ContentKey)) continue;
                try { projectile.Type = _idStore.GetOrAllocateProjectileId(projectile.ContentKey); }
                catch (Exception ex) { _log.Error("Content: could not allocate projectile id for " + projectile.ContentKey, ex); continue; }
                _projectilesById[projectile.Type] = projectile;
                _projectilesByKey[projectile.ContentKey] = projectile;
                _projectilesByType[projectile.GetType()] = projectile;
                _orderedProjectiles.Add(projectile);
            }

            duplicates.Clear();
            foreach (var buff in _pendingBuffs)
            {
                if (!duplicates.Add(buff.ContentKey)) continue;
                try { buff.Type = _idStore.GetOrAllocateBuffId(buff.ContentKey); }
                catch (Exception ex) { _log.Error("Content: could not allocate buff id for " + buff.ContentKey, ex); continue; }
                _buffsById[buff.Type] = buff;
                _buffsByKey[buff.ContentKey] = buff;
                _buffsByType[buff.GetType()] = buff;
                _orderedBuffs.Add(buff);
            }

            _idStore.Flush();
            _log.Info("Content: reserved " + _byId.Count + " item id(s) " + _idStore.ItemIdBase
                      + ".." + (_idStore.NextItemId - 1) + " and " + _tilesById.Count
                      + " tile id(s) " + _idStore.TileIdBase + ".." + (_idStore.NextTileId - 1)
                      + " and " + _wallsById.Count + " wall id(s) " + _idStore.WallIdBase
                      + ".." + (_idStore.NextWallId - 1)
                      + " and " + _npcsById.Count + " NPC id(s) " + _idStore.NpcIdBase
                      + ".." + (_idStore.NextNpcId - 1)
                      + " and " + _projectilesById.Count + " projectile id(s) " + _idStore.ProjectileIdBase
                      + ".." + (_idStore.NextProjectileId - 1)
                      + " and " + _buffsById.Count + " buff id(s) " + _idStore.BuffIdBase
                      + ".." + (_idStore.NextBuffId - 1)
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
            var oldNpcTextures = ReadTextureArray("Npc");
            var oldProjectileTextures = ReadTextureArray("Projectile");
            var oldBuffTextures = ReadTextureArray("Buff");

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
            if (_npcsById.Count > 0 && !_expander.ExpandNpcArrays(_idStore.NextNpcId))
            {
                _log.Error("Content: NPC array expansion failed — custom NPCs are disabled");
                _npcsById.Clear(); _npcsByKey.Clear(); _npcsByType.Clear(); _orderedNpcs.Clear();
            }
            else if (_npcsById.Count > 0)
            {
                ExpandExistingNpcArrays(_idStore.NextNpcId);
            }
            if (_projectilesById.Count > 0 && !_expander.ExpandProjectileArrays(_idStore.NextProjectileId))
            {
                _log.Error("Content: projectile array expansion failed — custom projectiles are disabled");
                _projectilesById.Clear(); _projectilesByKey.Clear(); _projectilesByType.Clear(); _orderedProjectiles.Clear();
            }
            else if (_projectilesById.Count > 0)
            {
                ExpandExistingProjectileArrays(_idStore.NextProjectileId);
            }
            if (_buffsById.Count > 0 && !_expander.ExpandBuffArrays(_idStore.NextBuffId))
            {
                _log.Error("Content: buff array expansion failed — custom buffs are disabled");
                _buffsById.Clear(); _buffsByKey.Clear(); _buffsByType.Clear(); _orderedBuffs.Clear();
            }
            else if (_buffsById.Count > 0)
            {
                ExpandExistingBuffArrays(_idStore.NextBuffId);
            }

            RegisterSearchNames(typeof(Terraria.ID.ItemID), _ordered, x => x.ContentKey, x => x.Type);
            RegisterSearchNames(typeof(Terraria.ID.TileID), _orderedTiles, x => x.ContentKey, x => x.Type);
            RegisterSearchNames(typeof(Terraria.ID.WallID), _orderedWalls, x => x.ContentKey, x => x.Type);
            RegisterSearchNames(typeof(Terraria.ID.NPCID), _orderedNpcs, x => x.ContentKey, x => x.Type);
            RegisterSearchNames(typeof(Terraria.ID.ProjectileID), _orderedProjectiles, x => x.ContentKey, x => x.Type);
            RegisterSearchNames(typeof(Terraria.ID.BuffID), _orderedBuffs, x => x.ContentKey, x => x.Type);

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

                var npcSlots = TextureAssetSlots.Resolve(_log, "Npc");
                if (npcSlots != null)
                    npcSlots.BackfillNulls(VanillaNpcCount);
                TextureAssetSlots.RepointCapturedArrays(_log, oldNpcTextures, ReadTextureArray("Npc"));

                var projectileSlots = TextureAssetSlots.Resolve(_log, "Projectile");
                if (projectileSlots != null) projectileSlots.BackfillNulls(VanillaProjectileCount);
                TextureAssetSlots.RepointCapturedArrays(_log, oldProjectileTextures, ReadTextureArray("Projectile"));

                var buffSlots = TextureAssetSlots.Resolve(_log, "Buff");
                if (buffSlots != null) buffSlots.BackfillNulls(VanillaBuffCount);
                TextureAssetSlots.RepointCapturedArrays(_log, oldBuffTextures, ReadTextureArray("Buff"));
            }
            catch (Exception ex)
            {
                _log.Error("Content: texture slot backfill failed", ex);
            }

            foreach (var item in _byId.Values)
            {
                try
                {
                    item.SetStaticDefaults();
                    var pet = item as TimfPetItem;
                    if (pet != null)
                    {
                        var buffType = pet.PetBuffType;
                        if (buffType <= 0 || buffType >= Terraria.Main.vanityPet.Length
                            || buffType >= Terraria.Main.lightPet.Length)
                            throw new InvalidOperationException("PetBuffType " + buffType
                                + " is outside the active Buff ID space");

                        if (pet.PetSlot == TimfPetSlot.LightPet)
                            Terraria.Main.lightPet[buffType] = true;
                        else
                            Terraria.Main.vanityPet[buffType] = true;

                        // Equipped pet buffs are recreated by UpdatePet/UpdatePetLight and
                        // should follow vanilla's hidden-duration, non-save semantics.
                        Terraria.Main.buffNoTimeDisplay[buffType] = true;
                        Terraria.Main.buffNoSave[buffType] = true;
                    }
                }
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
            foreach (var npc in _npcsById.Values)
            {
                try { Terraria.Main.npcFrameCount[npc.Type] = Math.Max(1, npc.FrameCount); npc.SetStaticDefaults(); }
                catch (Exception ex) { _log.Error("Content: SetStaticDefaults failed for NPC " + npc.ContentKey, ex); }
            }
            RegisterNpcSamples();
            RegisterBossBars();
            foreach (var projectile in _projectilesById.Values)
            {
                try
                {
                    Terraria.Main.projFrames[projectile.Type] = Math.Max(1, projectile.FrameCount);
                    projectile.SetStaticDefaults();
                }
                catch (Exception ex) { _log.Error("Content: SetStaticDefaults failed for projectile " + projectile.ContentKey, ex); }
            }
            foreach (var buff in _buffsById.Values)
            {
                try
                {
                    Terraria.Main.debuff[buff.Type] = buff.IsDebuff;
                    Terraria.Main.buffNoSave[buff.Type] = !buff.Save;
                    Terraria.ID.BuffID.Sets.NurseCannotRemoveDebuff[buff.Type] = !buff.CanBeCleared;
                    buff.SetStaticDefaults();
                }
                catch (Exception ex) { _log.Error("Content: SetStaticDefaults failed for buff " + buff.ContentKey, ex); }
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
                      + " tile(s), " + _wallsById.Count + " wall(s), " + _npcsById.Count
                      + " NPC(s), " + _orderedBiomes.Count + " biome(s), "
                      + _projectilesById.Count + " projectile(s), and " + _buffsById.Count + " buff(s) live");
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

        public int NpcType<TNpc>() where TNpc : TimfNpc
        {
            TimfNpc def;
            return _npcsByType.TryGetValue(typeof(TNpc), out def) ? def.Type : 0;
        }

        public int NpcType(string contentKey)
        {
            TimfNpc def;
            return contentKey != null && _npcsByKey.TryGetValue(contentKey, out def) ? def.Type : 0;
        }

        public TimfNpc GetNpc(int type)
        {
            TimfNpc def;
            return _npcsById.TryGetValue(type, out def) ? def : null;
        }

        public bool IsBiomeActive<TBiome>(Terraria.Player player) where TBiome : TimfBiome
        {
            for (var i = 0; i < _orderedBiomes.Count; i++)
                if (_orderedBiomes[i] is TBiome)
                    return IsBiomeActive(_orderedBiomes[i], player);
            return false;
        }

        internal bool IsBiomeActive(TimfBiome biome, Terraria.Player player)
        {
            if (biome == null || !IsSessionAllowed(biome.ModId)) return false;
            Terraria.SceneMetrics metrics = null;
            try
            {
                var field = typeof(Terraria.Main).GetField("_playerSceneMetrics",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                metrics = field?.GetValue(null) as Terraria.SceneMetrics;
                return biome.IsActive(player, metrics, this);
            }
            catch (Exception ex)
            {
                _log.Error("Content: biome predicate failed for " + biome.ContentKey, ex);
                return false;
            }
        }

        public int ProjectileType<TProjectile>() where TProjectile : TimfProjectile
        { TimfProjectile def; return _projectilesByType.TryGetValue(typeof(TProjectile), out def) ? def.Type : 0; }
        public int ProjectileType(string contentKey)
        { TimfProjectile def; return contentKey != null && _projectilesByKey.TryGetValue(contentKey, out def) ? def.Type : 0; }
        public TimfProjectile GetProjectile(int type)
        { TimfProjectile def; return _projectilesById.TryGetValue(type, out def) ? def : null; }
        public int BuffType<TBuff>() where TBuff : TimfBuff
        { TimfBuff def; return _buffsByType.TryGetValue(typeof(TBuff), out def) ? def.Type : 0; }
        public int BuffType(string contentKey)
        { TimfBuff def; return contentKey != null && _buffsByKey.TryGetValue(contentKey, out def) ? def.Type : 0; }
        public TimfBuff GetBuff(int type)
        { TimfBuff def; return _buffsById.TryGetValue(type, out def) ? def : null; }

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
        private readonly List<TimfNpc> _orderedNpcs = new List<TimfNpc>();
        private readonly List<TimfProjectile> _orderedProjectiles = new List<TimfProjectile>();
        private readonly List<TimfBuff> _orderedBuffs = new List<TimfBuff>();

        public IReadOnlyList<TimfItem> RegisteredItems => _ordered;
        public IReadOnlyList<TimfTile> RegisteredTiles => _orderedTiles;
        public IReadOnlyList<TimfWall> RegisteredWalls => _orderedWalls;
        public IReadOnlyList<TimfNpc> RegisteredNpcs => _orderedNpcs;
        public IReadOnlyList<TimfBiome> RegisteredBiomes => _orderedBiomes;
        public IReadOnlyList<TimfProjectile> RegisteredProjectiles => _orderedProjectiles;
        public IReadOnlyList<TimfBuff> RegisteredBuffs => _orderedBuffs;

        /// <summary>Set by the texture loader once it has run, purely so diagnostics can show it.</summary>
        internal int TexturesLoaded { get; set; }
        internal int TexturesPlaceholder { get; set; }
        internal int TileTexturesLoaded { get; set; }
        internal int TileTexturesPlaceholder { get; set; }
        internal int WallTexturesLoaded { get; set; }
        internal int WallTexturesPlaceholder { get; set; }
        internal int NpcTexturesLoaded { get; set; }
        internal int NpcTexturesPlaceholder { get; set; }
        internal int ProjectileTexturesLoaded { get; set; }
        internal int ProjectileTexturesPlaceholder { get; set; }
        internal int BuffTexturesLoaded { get; set; }
        internal int BuffTexturesPlaceholder { get; set; }

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
            int liveNpcCount;
            try { liveNpcCount = Terraria.ID.NPCID.Count; } catch { liveNpcCount = -1; }
            lines.Add("Vanilla NPC count  : " + VanillaNpcCount);
            lines.Add("NPCID.Count now    : " + liveNpcCount
                      + (liveNpcCount > VanillaNpcCount ? "  (expanded)" : "  (NOT expanded)"));
            lines.Add("NPC ids allocated  : " + _npcsById.Count);
            lines.Add("NPC textures       : " + NpcTexturesLoaded + " loaded, "
                      + NpcTexturesPlaceholder + " placeholder");
            lines.Add("Biomes registered  : " + _orderedBiomes.Count);
            int liveProjectileCount;
            try { liveProjectileCount = Terraria.ID.ProjectileID.Count; } catch { liveProjectileCount = -1; }
            lines.Add("Vanilla projectile : " + VanillaProjectileCount);
            lines.Add("ProjectileID.Count : " + liveProjectileCount
                      + (liveProjectileCount > VanillaProjectileCount ? "  (expanded)" : "  (NOT expanded)"));
            lines.Add("Projectile ids     : " + _projectilesById.Count);
            lines.Add("Projectile textures: " + ProjectileTexturesLoaded + " loaded, " + ProjectileTexturesPlaceholder + " placeholder");
            int liveBuffCount;
            try { liveBuffCount = Terraria.ID.BuffID.Count; } catch { liveBuffCount = -1; }
            lines.Add("Vanilla buff count : " + VanillaBuffCount);
            lines.Add("BuffID.Count now   : " + liveBuffCount
                      + (liveBuffCount > VanillaBuffCount ? "  (expanded)" : "  (NOT expanded)"));
            lines.Add("Buff ids           : " + _buffsById.Count);
            lines.Add("Buff textures      : " + BuffTexturesLoaded + " loaded, " + BuffTexturesPlaceholder + " placeholder");
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

        private int ReadVanillaNpcCount()
        {
            try { return Terraria.ID.NPCID.Count; }
            catch (Exception ex) { _log.Error("Content: could not read NPCID.Count", ex); return short.MaxValue; }
        }

        private int ReadVanillaProjectileCount()
        {
            try { return Terraria.ID.ProjectileID.Count; }
            catch (Exception ex) { _log.Error("Content: could not read ProjectileID.Count", ex); return short.MaxValue; }
        }

        private int ReadVanillaBuffCount()
        {
            try { return Terraria.ID.BuffID.Count; }
            catch (Exception ex) { _log.Error("Content: could not read BuffID.Count", ex); return int.MaxValue; }
        }

        private void ExpandExistingNpcArrays(int required)
        {
            try
            {
                if (Terraria.Main.player != null)
                    foreach (var player in Terraria.Main.player)
                    {
                        if (player == null || player.npcTypeNoAggro == null
                            || player.npcTypeNoAggro.Length >= required) continue;
                        var grown = new bool[required];
                        Array.Copy(player.npcTypeNoAggro, grown, player.npcTypeNoAggro.Length);
                        player.npcTypeNoAggro = grown;
                    }

                EnsureTownRoomArrayCapacity(Terraria.WorldGen.TownManager);
            }
            catch (Exception ex) { _log.Error("Content: existing NPC-indexed array expansion failed", ex); }
        }

        private void RegisterNpcSamples()
        {
            var sortingId = VanillaNpcCount;
            foreach (var def in _orderedNpcs)
            {
                try
                {
                    var sample = new Terraria.NPC();
                    sample.SetDefaults(def.Type, default(Terraria.NPCSpawnParams));
                    var persistentId = "TIMF_" + def.ContentKey.Replace('/', '_');
                    Terraria.ID.ContentSamples.NpcsByNetId[def.Type] = sample;
                    Terraria.ID.ContentSamples.NpcNetIdsByPersistentIds[persistentId] = def.Type;
                    Terraria.ID.ContentSamples.NpcPersistentIdsByNetIds[def.Type] = persistentId;
                    Terraria.ID.ContentSamples.NpcBestiarySortingId[def.Type] = sortingId++;
                    Terraria.ID.ContentSamples.NpcBestiaryRarityStars[def.Type] = 0;
                    Terraria.ID.ContentSamples.NpcBestiaryCreditIdsByNpcNetIds[def.Type] = persistentId;
                }
                catch (Exception ex) { _log.Error("Content: NPC sample registration failed for " + def.ContentKey, ex); }
            }
        }

        /// <summary>
        /// Give framework bosses the vanilla bottom-screen health bar and minimap head.
        /// BigProgressBarSystem shows the shared CommonBossBigProgressBar for any npc.boss NPC, but it
        /// needs a boss-head texture (NPCID.Sets.BossHeadTextures[type] indexing TextureAssets.NpcHeadBoss),
        /// which defaults to -1 for a modded type. We simply point each custom boss at an existing
        /// vanilla boss-head index (Eye of Cthulhu) — this gives the full big health bar and a minimap
        /// icon with zero risk. Growing NpcHeadBoss instead is NOT safe: besides Main.BossNPCHeadRenderer,
        /// other systems capture that array (minimap/HUD boss-head draw), and any un-repointed holder
        /// indexes the appended slot past its old length and throws every frame between SpriteBatch
        /// Begin/End, wiping the whole interface the moment a boss is on screen. Reusing an in-bounds
        /// vanilla head sidesteps all of that. Client-only — the dedicated server has no textures.
        /// </summary>
        private void RegisterBossBars()
        {
            if (Terraria.Main.dedServ) return;
            try
            {
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                var setsType = typeof(Terraria.ID.NPCID).GetNestedType("Sets");
                var headIndexArr = setsType?.GetField("BossHeadTextures", flags)?.GetValue(null) as int[];
                if (headIndexArr == null)
                {
                    _log.Warn("Content: NPCID.Sets.BossHeadTextures not found — custom bosses will not get a health bar");
                    return;
                }

                var vanillaHead = FindReusableVanillaBossHead(headIndexArr);
                if (vanillaHead < 0)
                {
                    _log.Warn("Content: no vanilla boss head available to reuse — custom bosses will not get a health bar");
                    return;
                }

                var assigned = 0;
                foreach (var def in _orderedNpcs)
                {
                    Terraria.NPC sample;
                    if (!Terraria.ID.ContentSamples.NpcsByNetId.TryGetValue(def.Type, out sample)
                        || sample == null || !sample.boss)
                        continue;
                    if (def.Type < 0 || def.Type >= headIndexArr.Length || headIndexArr[def.Type] >= 0)
                        continue;
                    headIndexArr[def.Type] = vanillaHead;
                    assigned++;
                }

                if (assigned > 0)
                    _log.Info("Content: gave " + assigned + " custom boss(es) a big health bar (reusing vanilla head "
                              + vanillaHead + "; NpcHeadBoss is not grown, which would crash the HUD)");
            }
            catch (Exception ex) { _log.Error("Content: boss bar registration failed", ex); }
        }

        /// <summary>Finds a loaded vanilla boss-head index to reuse (Eye of Cthulhu, else first valid).</summary>
        private static int FindReusableVanillaBossHead(int[] headIndexArr)
        {
            try
            {
                var eoc = (int)Terraria.ID.NPCID.EyeofCthulhu;
                if (eoc >= 0 && eoc < headIndexArr.Length && headIndexArr[eoc] > 0)
                    return headIndexArr[eoc];
            }
            catch { /* fall through to scan */ }
            for (var i = 0; i < headIndexArr.Length; i++)
                if (headIndexArr[i] > 0) return headIndexArr[i];
            return -1;
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

        private void ExpandExistingProjectileArrays(int newCount)
        {
            var expanded = 0;
            try
            {
                if (Terraria.Main.player == null) return;
                foreach (var player in Terraria.Main.player)
                {
                    if (player == null || player.ownedProjectileCounts == null
                        || player.ownedProjectileCounts.Length >= newCount) continue;
                    var grown = new int[newCount];
                    Array.Copy(player.ownedProjectileCounts, grown, player.ownedProjectileCounts.Length);
                    player.ownedProjectileCounts = grown;
                    expanded++;
                }
                _log.Info("Content: expanded Player.ownedProjectileCounts for " + expanded
                          + " existing player instance(s)");
            }
            catch (Exception ex) { _log.Error("Content: existing projectile-indexed array expansion failed", ex); }
        }

        private void ExpandExistingBuffArrays(int newCount)
        {
            var players = 0;
            var npcs = 0;
            try
            {
                if (Terraria.Main.player != null)
                    foreach (var player in Terraria.Main.player)
                    {
                        if (player == null || player.buffImmune == null || player.buffImmune.Length >= newCount) continue;
                        var grown = new bool[newCount];
                        Array.Copy(player.buffImmune, grown, player.buffImmune.Length);
                        player.buffImmune = grown;
                        players++;
                    }
                if (Terraria.Main.npc != null)
                    foreach (var npc in Terraria.Main.npc)
                    {
                        if (npc == null || npc.buffImmune == null || npc.buffImmune.Length >= newCount) continue;
                        var grown = new bool[newCount];
                        Array.Copy(npc.buffImmune, grown, npc.buffImmune.Length);
                        npc.buffImmune = grown;
                        npcs++;
                    }
                _log.Info("Content: expanded buffImmune for " + players + " player(s) and " + npcs + " NPC(s)");
            }
            catch (Exception ex) { _log.Error("Content: existing buff-indexed array expansion failed", ex); }
        }

        /// <summary>
        /// Repairs arrays owned by a Player instance. Terraria constructs and deserializes
        /// players at several points after the static ID arrays have been widened, and some
        /// game builds bake the vanilla Count value into those instance initializers. Such a
        /// player therefore needs repairing before any vanilla update indexes it with a mod ID.
        /// </summary>
        internal void EnsurePlayerArrayCapacity(Terraria.Player player)
        {
            if (!_activated || player == null)
                return;

            try
            {
                if (_tilesById.Count > 0)
                    player.adjTile = Grow(player.adjTile, _idStore.NextTileId);
                if (_npcsById.Count > 0)
                    player.npcTypeNoAggro = Grow(player.npcTypeNoAggro, _idStore.NextNpcId);
                if (_projectilesById.Count > 0)
                    player.ownedProjectileCounts = Grow(player.ownedProjectileCounts, _idStore.NextProjectileId);
                if (_buffsById.Count > 0)
                    player.buffImmune = Grow(player.buffImmune, _idStore.NextBuffId);
            }
            catch (Exception ex)
            {
                _log.Error("Content: repairing player instance arrays failed", ex);
            }
        }

        /// <summary>Repairs arrays recreated by NPC construction and SetDefaults.</summary>
        internal void EnsureNpcArrayCapacity(Terraria.NPC npc)
        {
            if (!_activated || npc == null || _buffsById.Count == 0)
                return;

            try
            {
                npc.buffImmune = Grow(npc.buffImmune, _idStore.NextBuffId);
            }
            catch (Exception ex)
            {
                _log.Error("Content: repairing NPC instance arrays failed", ex);
            }
        }

        /// <summary>
        /// Repairs the housing manager when Terraria creates it after content activation.
        /// Its private array is initialized from a vanilla Count constant in some game builds.
        /// </summary>
        internal void EnsureTownRoomArrayCapacity(Terraria.GameContent.TownRoomManager manager)
        {
            if (!_activated || manager == null || _npcsById.Count == 0)
                return;

            try
            {
                var field = manager.GetType().GetField("_hasRoom",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var current = field?.GetValue(manager) as bool[];
                if (current == null || current.Length >= _idStore.NextNpcId)
                    return;
                var grown = Grow(current, _idStore.NextNpcId);
                field.SetValue(manager, grown);
                _log.Info("Content: expanded TownRoomManager._hasRoom " + current.Length
                          + " -> " + grown.Length);
            }
            catch (Exception ex)
            {
                _log.Error("Content: repairing TownRoomManager arrays failed", ex);
            }
        }

        private static bool[] Grow(bool[] current, int required)
        {
            if (required <= 0 || (current != null && current.Length >= required))
                return current;
            var grown = new bool[required];
            if (current != null)
                Array.Copy(current, grown, current.Length);
            return grown;
        }

        private static int[] Grow(int[] current, int required)
        {
            if (required <= 0 || (current != null && current.Length >= required))
                return current;
            var grown = new int[required];
            if (current != null)
                Array.Copy(current, grown, current.Length);
            return grown;
        }
    }
}
