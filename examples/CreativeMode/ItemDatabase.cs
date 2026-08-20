using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;
using TIMF.Pinyin;

namespace CreativeMode
{
    internal struct ItemEntry
    {
        public int Type;
        public string Name;
        public string NameLower;
        public string Pinyin;
        public string Initials;
    }

    /// <summary>
    /// Builds the id → display-name table for all vanilla items and gives items to the
    /// local player. Uses reflection for the give path so we don't bind to IEntitySource
    /// at compile time (its identity differs between extracted and runtime assemblies).
    /// </summary>
    internal sealed class ItemDatabase
    {
        private readonly ILogger _log;
        private readonly ITerrariaReflection _reflection;
        private readonly IPinyinService _pinyin;
        private readonly List<ItemEntry> _all = new List<ItemEntry>();
        private bool _built;

        // Reflection for giving items.
        private MethodInfo _getItemSource;   // Player.GetItemSource_OpenItem(int)
        private MethodInfo _quickSpawn;      // Player.QuickSpawnItem(IEntitySource, int, int)
        private bool _giveResolved;
        private bool _giveFailed;

        public ItemDatabase(ILogger log, ITerrariaReflection reflection, IPinyinService pinyin)
        {
            _log = log;
            _reflection = reflection;
            _pinyin = pinyin;
        }

        public IReadOnlyList<ItemEntry> All => _all;
        public bool IsBuilt => _built;

        public void EnsureBuilt()
        {
            if (_built)
                return;

            try
            {
                var count = ItemID.Count; // total vanilla item ids
                for (var type = 1; type < count; type++)
                {
                    string name;
                    try
                    {
                        name = Lang.GetItemNameValue(type);
                    }
                    catch
                    {
                        name = null;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                        continue; // skip unused / unnamed ids

                    _all.Add(new ItemEntry
                    {
                        Type = type,
                        Name = name,
                        NameLower = name.ToLowerInvariant(),
                        Pinyin = _pinyin != null ? _pinyin.ToPinyin(name) : "",
                        Initials = _pinyin != null ? _pinyin.ToInitials(name) : "",
                    });
                }

                _built = true;
                _log.Info("CreativeMode item DB built: " + _all.Count + " named items (of " + count + " ids)");
            }
            catch (Exception ex)
            {
                _log.Error("CreativeMode failed to build item DB", ex);
                _built = true; // don't retry forever
            }
        }

        /// <summary>Filter by name/id/pinyin/initials. Empty query returns everything.</summary>
        public void Search(string query, List<ItemEntry> into)
        {
            into.Clear();
            if (!_built)
                return;

            if (string.IsNullOrWhiteSpace(query))
            {
                into.AddRange(_all);
                return;
            }

            var q = query.Trim().ToLowerInvariant();

            // Numeric query → match by id as well.
            int idQuery;
            var isNum = int.TryParse(q, out idQuery);

            for (var i = 0; i < _all.Count; i++)
            {
                var e = _all[i];
                if (Matches(e, q)
                    || (isNum && e.Type == idQuery))
                {
                    into.Add(e);
                }
            }
        }

        /// <summary>Name/pinyin/initials match via the shared service, or name-only fallback.</summary>
        private bool Matches(ItemEntry e, string queryLower)
        {
            if (_pinyin != null)
                return _pinyin.Matches(e.Name, e.NameLower, e.Pinyin, e.Initials, queryLower);
            if (string.IsNullOrEmpty(queryLower))
                return true;
            return !string.IsNullOrEmpty(e.NameLower)
                   && e.NameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0;
        }

        public bool Give(int type, int amount)
        {
            if (amount <= 0)
                return false;

            var player = Main.LocalPlayer;
            if (player == null || !player.active)
                return false;

            if (!ResolveGive())
                return false;

            try
            {
                var source = _reflection.Invoke(_getItemSource, player, new object[] { type });
                // QuickSpawnItem drops near the player and pickups into inventory (respecting overflow).
                // Split into stacks of at most 9999 to stay within vanilla stack handling.
                var remaining = amount;
                while (remaining > 0)
                {
                    var give = Math.Min(remaining, 9999);
                    _reflection.Invoke(_quickSpawn, player, new object[] { source, type, give });
                    remaining -= give;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (!_giveFailed)
                {
                    _giveFailed = true;
                    _log.Error("CreativeMode give failed", ex);
                }
                return false;
            }
        }

        private bool ResolveGive()
        {
            if (_giveResolved)
                return _getItemSource != null && _quickSpawn != null;

            _giveResolved = true;
            try
            {
                var playerType = typeof(Player);
                _getItemSource = playerType.GetMethod(
                    "GetItemSource_OpenItem",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(int) },
                    null);

                // QuickSpawnItem(IEntitySource source, int item, int stack) — match by shape.
                foreach (var m in playerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "QuickSpawnItem")
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 3
                        && ps[1].ParameterType == typeof(int)
                        && ps[2].ParameterType == typeof(int))
                    {
                        _quickSpawn = m;
                        break;
                    }
                }

                if (_getItemSource == null || _quickSpawn == null)
                    _log.Error("CreativeMode: could not resolve give methods (GetItemSource_OpenItem/QuickSpawnItem)");

                return _getItemSource != null && _quickSpawn != null;
            }
            catch (Exception ex)
            {
                _log.Error("CreativeMode give reflection failed", ex);
                return false;
            }
        }
    }
}
