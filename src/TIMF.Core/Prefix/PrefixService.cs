using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using TIMF.Abstractions;

namespace TIMF.Core.Prefix
{
    internal sealed class PrefixService : IPrefixService
    {
        private readonly Dictionary<int, List<int>> _bestMap = new Dictionary<int, List<int>>();
        private readonly object _lock = new object();
        private bool _detected;

        public void EnsureDetected()
        {
            if (_detected)
                return;
            _detected = true;
            DetectVanillaBestPrefixes();
        }

        public void RegisterBestPrefix(int itemType, int prefixId)
        {
            lock (_lock)
            {
                if (!_bestMap.TryGetValue(itemType, out var list))
                {
                    list = new List<int>();
                    _bestMap[itemType] = list;
                }
                if (!list.Contains(prefixId))
                    list.Add(prefixId);
            }
        }

        public bool TryGetBestPrefixes(int itemType, out IReadOnlyList<int> prefixIds)
        {
            EnsureDetected();
            lock (_lock)
            {
                if (_bestMap.TryGetValue(itemType, out var list) && list.Count > 0)
                {
                    prefixIds = list.ToArray();
                    return true;
                }
            }
            prefixIds = null;
            return false;
        }

        public bool TryGetRandomBestPrefix(int itemType, out int prefixId)
        {
            EnsureDetected();
            lock (_lock)
            {
                if (_bestMap.TryGetValue(itemType, out var list) && list.Count > 0)
                {
                    prefixId = list[Terraria.Main.rand.Next(list.Count)];
                    return true;
                }
            }
            prefixId = -1;
            return false;
        }

        private void DetectVanillaBestPrefixes()
        {
            var detected = 0;
            for (int i = 1; i < ItemID.Count; i++)
            {
                var sample = ContentSamples.ItemsByType[i];
                if (sample == null || sample.IsAir)
                    continue;
                if (!sample.CanHavePrefixes())
                    continue;

                var validPrefixes = CollectValidPrefixes(i);
                if (validPrefixes.Count == 0)
                    continue;

                var bests = new HashSet<int>();

                for (int attempt = 0; attempt < validPrefixes.Count * 3; attempt++)
                {
                    var probe = new Item();
                    probe.SetDefaults(i);
                    bool flag = false;
                    probe.Prefix(-2, out flag);
                    if (flag)
                        bests.Add(probe.prefix);
                }

                if (bests.Count > 0)
                {
                    lock (_lock)
                        _bestMap[i] = new List<int>(bests);
                    detected++;
                }
            }
        }

        private static List<int> CollectValidPrefixes(int itemType)
        {
            var valid = new List<int>();
            for (int p = 1; p < PrefixID.Count; p++)
            {
                var probe = new Item();
                probe.SetDefaults(itemType);
                probe.Prefix(p);
                if (probe.prefix == p)
                    valid.Add(p);
            }
            return valid;
        }
    }
}