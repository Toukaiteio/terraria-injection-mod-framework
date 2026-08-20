using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Grows vanilla's fixed-size, id-indexed static arrays so custom item and tile ids
    /// can be addressed without every vanilla lookup throwing IndexOutOfRange.
    ///
    /// Arrays are found by length: an id-indexed array is exactly <c>Count</c> elements long.
    /// The exact number of matching arrays is version-dependent; the verifier below checks the
    /// critical tables after each expansion instead of baking a particular Terraria build's
    /// array census into the compatibility layer.
    ///
    /// The scan covers the whole assembly. A curated type list was tried first and proved
    /// unworkable — it silently omitted <c>Item.claw</c>, which then threw IndexOutOfRange in
    /// the held-item draw layer as soon as a modded item was equipped. There is no way to know
    /// such a list is complete, and each omission surfaces as a crash in unrelated-looking code.
    ///
    /// The cost is the reverse risk: an unrelated array that happens to be <c>Count</c> long
    /// gets grown too. Extra trailing slots are harmless for indexed reads, and
    /// <see cref="VerifyCoverage"/> plus the per-type debug log make both the coverage and any
    /// surprise auditable.
    /// </summary>
    internal sealed class VanillaArrayExpander
    {
        private readonly ILogger _log;

        public VanillaArrayExpander(ILogger log)
        {
            _log = log;
        }

        public int ExpandedItemArrayCount { get; private set; }
        public int ExpandedTileArrayCount { get; private set; }
        public int ExpandedWallArrayCount { get; private set; }
        public int ExpandedNpcArrayCount { get; private set; }
        public int ExpandedProjectileArrayCount { get; private set; }
        public int ExpandedBuffArrayCount { get; private set; }
        public int ExpandedArrayCount => ExpandedItemArrayCount + ExpandedTileArrayCount + ExpandedWallArrayCount
                                         + ExpandedNpcArrayCount + ExpandedProjectileArrayCount + ExpandedBuffArrayCount;

        /// <summary>
        /// Grow every item-indexed array to <paramref name="newCount"/> and then publish the
        /// new <c>ItemID.Count</c>.
        ///
        /// Order matters: arrays are matched against the <em>old</em> count, so the count
        /// field has to be written last or nothing would match.
        /// </summary>
        public bool ExpandItemArrays(int newCount)
        {
            return Expand("Terraria.ID.ItemID", newCount, "item", new[]
            {
                "Terraria.Item:claw",
                "Terraria.Main:itemAnimations",
                "Terraria.GameContent.TextureAssets:Item",
                "Terraria.ID.ItemID+Sets:ItemNoGravity",
                "Terraria.ID.ItemID+Sets:IsAMaterial",
            });
        }

        /// <summary>
        /// Widen the tile id space. Same mechanism as items, against <c>TileID.Count</c>
        /// (a UInt16, so the ceiling is 65535 rather than the items' 32767).
        /// </summary>
        public bool ExpandTileArrays(int newCount)
        {
            return Expand("Terraria.ID.TileID", newCount, "tile", new[]
            {
                "Terraria.Main:tileSolid",
                "Terraria.Main:tileFrameImportant",
                "Terraria.Main:tileNoAttach",
                "Terraria.Main:tileLighted",
                "Terraria.Main:tileMerge",
                "Terraria.GameContent.TextureAssets:Tile",
                "Terraria.ID.TileID+Sets:Falling",
            });
        }

        public bool ExpandWallArrays(int newCount)
        {
            return Expand("Terraria.ID.WallID", newCount, "wall", new[]
            {
                "Terraria.Main:wallHouse",
                "Terraria.Main:wallDungeon",
                "Terraria.GameContent.TextureAssets:Wall",
            });
        }

        public bool ExpandNpcArrays(int newCount)
        {
            return Expand("Terraria.ID.NPCID", newCount, "npc", new[]
            {
                "Terraria.Main:npcFrameCount",
                "Terraria.GameContent.TextureAssets:Npc",
                "Terraria.ID.NPCID+Sets:AllNPCs",
            });
        }

        public bool ExpandProjectileArrays(int newCount)
        {
            return Expand("Terraria.ID.ProjectileID", newCount, "projectile", new[]
            {
                "Terraria.Main:projFrames",
                "Terraria.Main:projHostile",
                "Terraria.Main:projHook",
                "Terraria.Main:projPet",
                "Terraria.GameContent.TextureAssets:Projectile",
                "Terraria.Lang:_projectileNameCache",
            });
        }

        public bool ExpandBuffArrays(int newCount)
        {
            return Expand("Terraria.ID.BuffID", newCount, "buff", new[]
            {
                "Terraria.Main:debuff",
                "Terraria.Main:buffNoSave",
                "Terraria.Main:pvpBuff",
                "Terraria.Main:persistentBuff",
                "Terraria.Main:buffNoTimeDisplay",
                "Terraria.GameContent.TextureAssets:Buff",
                "Terraria.Lang:_buffNameCache",
                "Terraria.Lang:_buffDescriptionCache",
            });
        }

        private bool Expand(string idTypeName, int newCount, string label, string[] criticalArrays)
        {
            try
            {
                var idType = typeof(Terraria.Item).Assembly.GetType(idTypeName);
                if (idType == null)
                {
                    _log.Error("Content: " + idTypeName + " not found — cannot expand " + label + " arrays");
                    return false;
                }

                var countField = idType.GetField("Count", BindingFlags.Public | BindingFlags.Static);
                if (countField == null)
                {
                    _log.Error("Content: " + idTypeName + ".Count not found — cannot expand " + label + " arrays");
                    return false;
                }

                var oldCount = Convert.ToInt32(countField.GetValue(null));
                if (newCount <= oldCount)
                {
                    _log.Info("Content: no " + label + " array expansion needed (count " + oldCount + ")");
                    return true;
                }

                _log.Info("Content: expanding " + label + " arrays " + oldCount + " -> " + newCount);

                // Scan the whole assembly rather than a hand-written list of types.
                //
                // A curated list was tried first and silently missed arrays: Item.claw lives on
                // Terraria.Item, was not on the list, and crashed the held-item draw layer with
                // IndexOutOfRange the moment a modded item was equipped. Guessing which types
                // hold id-indexed arrays does not scale — there is no way to know the list is
                // complete, and every gap is a crash somewhere unrelated-looking.
                //
                // Reading arbitrary statics is safe here specifically because this runs after
                // Main.Initialize_AlmostEverything, so vanilla has already initialised its types.
                Type[] allTypes;
                try
                {
                    allTypes = idType.Assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException rex)
                {
                    allTypes = rex.Types.Where(t => t != null).ToArray();
                    _log.Warn("Content: partial type load while scanning for id-indexed arrays");
                }

                var grown = 0;
                var byType = new Dictionary<string, int>();
                foreach (var t in allTypes)
                {
                    var n = ExpandArraysIn(t, oldCount, newCount);
                    if (n <= 0)
                        continue;
                    grown += n;
                    byType[t.FullName ?? t.Name] = n;

                    // Nested containers (ItemID.Sets.Conversion and friends).
                    foreach (var nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var m = ExpandArraysIn(nested, oldCount, newCount);
                        if (m > 0)
                        {
                            grown += m;
                            byType[nested.FullName ?? nested.Name] = m;
                        }
                    }
                }

                if (string.Equals(label, "tile", StringComparison.Ordinal))
                    ExpandedTileArrayCount = grown;
                else if (string.Equals(label, "wall", StringComparison.Ordinal))
                    ExpandedWallArrayCount = grown;
                else if (string.Equals(label, "npc", StringComparison.Ordinal))
                    ExpandedNpcArrayCount = grown;
                else if (string.Equals(label, "projectile", StringComparison.Ordinal))
                    ExpandedProjectileArrayCount = grown;
                else if (string.Equals(label, "buff", StringComparison.Ordinal))
                    ExpandedBuffArrayCount = grown;
                else
                    ExpandedItemArrayCount = grown;
                foreach (var kv in byType.OrderByDescending(k => k.Value))
                    _log.Debug("Content: expanded " + kv.Value + " " + label + " array(s) in " + kv.Key);

                UpdateSetFactorySize(idType.Assembly.GetType(idTypeName + "+Sets"), newCount, label);
                VerifyCoverage(idType.Assembly, newCount, criticalArrays);

                // Publish last: everything above keyed off the old value.
                WriteCount(countField, newCount);
                _log.Info("Content: expanded " + grown + " " + label + "-indexed array(s); "
                          + idType.Name + ".Count is now "
                          + Convert.ToInt32(countField.GetValue(null)));
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Content: " + label + " array expansion failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Assert that arrays known to be indexed by item id actually grew.
        ///
        /// The scan is length-based, so anything that was null, threw on read, or simply was
        /// not exactly <c>oldCount</c> long slips through silently — and a missed array only
        /// shows up later as IndexOutOfRange deep inside vanilla. Naming the ones we already
        /// know matter turns that into a log line at the moment of expansion.
        /// </summary>
        private void VerifyCoverage(Assembly asm, int newCount, string[] critical)
        {
            foreach (var entry in critical)
            {
                var split = entry.Split(':');
                try
                {
                    var t = asm.GetType(split[0]);
                    var f = t?.GetField(split[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f == null)
                    {
                        _log.Warn("Content: coverage check — " + entry + " not found");
                        continue;
                    }

                    var arr = f.GetValue(null) as Array;
                    if (arr == null)
                        _log.Warn("Content: coverage check — " + entry + " is null");
                    else if (arr.Length < newCount)
                        _log.Error("Content: coverage check FAILED — " + entry + " is only " + arr.Length
                                   + " long, expected >= " + newCount
                                   + ". Indexing it with a modded id will throw IndexOutOfRange.");
                }
                catch (Exception ex)
                {
            _log.Warn("Content: coverage check for " + entry + " threw: " + ex.GetType().Name);
                }
            }
        }

        private int ExpandArraysIn(Type type, int oldCount, int newCount)
        {
            var grown = 0;
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }
            catch (Exception ex)
            {
            _log.Warn("Content: cannot enumerate " + type.FullName + ": " + ex.GetType().Name);
                return 0;
            }

            foreach (var f in fields)
            {
                if (!f.FieldType.IsArray || f.FieldType.GetArrayRank() != 1)
                    continue;

                Array current;
                try { current = f.GetValue(null) as Array; }
                catch { continue; }

                if (current == null || current.Length != oldCount)
                    continue;

                try
                {
                    var elem = f.FieldType.GetElementType();
                    var grownArray = Array.CreateInstance(elem, newCount);
                    Array.Copy(current, grownArray, oldCount);

                    if (elem != null && elem.IsArray && elem.GetArrayRank() == 1)
                    {
                        var innerElement = elem.GetElementType();
                        if (IsSquareJaggedTable(current, oldCount))
                            ExpandSquareJaggedRows(grownArray, oldCount, newCount, innerElement);
                        else
                            FillNewJaggedRowsWithEmptyArrays(grownArray, oldCount, newCount, innerElement);
                    }
                    else
                    {
                        var fill = MajorityValue(current, elem);
                        if (fill != null)
                        {
                            for (var i = oldCount; i < newCount; i++)
                                grownArray.SetValue(fill, i);
                        }
                    }

                    f.SetValue(null, grownArray);
                    grown++;
                }
                catch (Exception ex)
                {
            _log.Warn("Content: failed to expand " + type.Name + "." + f.Name + ": " + ex.GetType().Name);
                }
            }

            return grown;
        }

        /// <summary>
        /// Grow both axes of square jagged lookup tables such as <c>Main.tileMerge</c>.
        /// Expanding only the outer array makes the custom row addressable, but vanilla then
        /// indexes an old inner row with the custom neighbour id and still throws.
        /// </summary>
        private static bool IsSquareJaggedTable(Array outer, int oldCount)
        {
            var nonNull = 0;
            var square = 0;
            for (var i = 0; i < oldCount; i++)
            {
                var row = outer.GetValue(i) as Array;
                if (row == null)
                    continue;
                nonNull++;
                if (row.Length == oldCount)
                    square++;
            }
            return square > 0 && square * 2 >= nonNull;
        }

        private static void ExpandSquareJaggedRows(Array outer, int oldCount, int newCount, Type innerElementType)
        {
            if (innerElementType == null)
                return;

            for (var i = 0; i < newCount; i++)
            {
                var row = i < oldCount ? outer.GetValue(i) as Array : null;
                if (row != null && row.Length != oldCount)
                    continue;

                var grownRow = Array.CreateInstance(innerElementType, newCount);
                if (row != null)
                    Array.Copy(row, grownRow, row.Length);
                outer.SetValue(grownRow, i);
            }
        }

        /// <summary>
        /// Non-square jagged id tables normally use an empty inner array to mean "no entries".
        /// ArmorSetBonuses.SetsContaining is the important example: leaving a null crashes the
        /// hover tooltip, while creating a newCount-long row makes vanilla iterate thousands of
        /// null armor-set entries. New ids therefore receive a real zero-length row.
        /// </summary>
        private static void FillNewJaggedRowsWithEmptyArrays(
            Array outer,
            int oldCount,
            int newCount,
            Type innerElementType)
        {
            if (innerElementType == null)
                return;
            for (var i = oldCount; i < newCount; i++)
                outer.SetValue(Array.CreateInstance(innerElementType, 0), i);
        }

        /// <summary>
        /// Best-effort recovery of the value a set was created with.
        ///
        /// These arrays come from <c>SetFactory.CreateBoolSet(defaultState, …)</c> and friends,
        /// so new slots must carry that default — zero-filling would, for example, silently mark
        /// every modded item as not-a-material when the set defaults to true. The original
        /// defaultState is not recorded anywhere readable, but a set is by construction mostly
        /// its default with a handful of exceptions, so the most common value recovers it.
        /// Reference-typed sets are left null rather than guessed at.
        /// </summary>
        private static object MajorityValue(Array source, Type elementType)
        {
            if (source.Length == 0)
                return null;

            // Reference arrays need this just as much as value ones. Lang._itemTooltipCache is
            // seeded with ItemTooltip.None in every slot before a few are overridden, and
            // Lang.GetTooltip returns the element straight out — so a null left in a modded slot
            // is a NullReferenceException inside the hover tooltip, which both hides the tooltip
            // box and leaks the SpriteBatch it was drawing into.
            //
            // Only a dominant value is trusted here: arrays of all-distinct references (the
            // texture assets, say) have no meaningful default and are better left null for the
            // caller to fill deliberately.
            var requireDominant = !elementType.IsValueType;

            // Null has to be counted, not skipped. Several vanilla sets are nullable overrides
            // that are null almost everywhere — ForceConsumption is null for 6141 of 6147 ids —
            // so ignoring nulls makes the handful of deliberate exceptions look like the norm
            // and stamps one of them onto every modded id. That is exactly how modded
            // consumables stopped being consumed: they inherited ForceConsumption = false.
            var counts = new Dictionary<object, int>();
            var nullCount = 0;
            var sampleStep = source.Length > 4096 ? 4 : 1;   // sampling is ample for a majority
            for (var i = 0; i < source.Length; i += sampleStep)
            {
                object v;
                try { v = source.GetValue(i); }
                catch { continue; }
                if (v == null)
                {
                    nullCount++;
                    continue;
                }
                int c;
                counts[v] = counts.TryGetValue(v, out c) ? c + 1 : 1;
            }

            object best = null;
            var bestCount = nullCount;      // null starts as the incumbent
            var sampled = nullCount;
            foreach (var kv in counts)
            {
                sampled += kv.Value;
                if (kv.Value > bestCount)
                {
                    bestCount = kv.Value;
                    best = kv.Key;
                }
            }

            // best stays null when null won, which is the right fill for an override array.
            if (best == null)
                return null;

            if (requireDominant && bestCount * 2 <= sampled)
                return null;

            return best;
        }

        /// <summary>
        /// Keep <c>ItemID.Sets.Factory</c> in step so any set created after expansion is born
        /// at the new size instead of the vanilla one.
        /// </summary>
        private void UpdateSetFactorySize(Type setsType, int newCount, string label)
        {
            try
            {
                var factoryField = setsType?.GetField("Factory", BindingFlags.Public | BindingFlags.Static);
                var factory = factoryField?.GetValue(null);
                if (factory == null)
                    return;

                foreach (var f in factory.GetType().GetFields(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.FieldType != typeof(int))
                        continue;
                    f.SetValue(factory, newCount);
                    _log.Info("Content: " + label + " SetFactory." + f.Name + " updated to " + newCount);
                }
            }
            catch (Exception ex)
            {
            _log.Warn("Content: could not update " + label + " SetFactory size: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// <c>ItemID.Count</c> is a static readonly Int16. Reflection can still set it on
        /// .NET Framework, which is the only way to widen the id space from outside the game.
        /// </summary>
        private void WriteCount(FieldInfo countField, int newCount)
        {
            if (newCount > short.MaxValue && countField.FieldType == typeof(short))
                throw new InvalidOperationException("Item id space exceeds Int16 range (" + newCount + ")");

            countField.SetValue(null, Convert.ChangeType(newCount, countField.FieldType));
        }
    }
}
