using System.Collections.Generic;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Registry of "best prefix" per item type.
    /// Core auto-detects all vanilla best prefixes on startup by brute-force.
    /// Mods may register overrides for custom items via <see cref="RegisterBestPrefix"/>.
    /// An item can have multiple best prefixes (e.g. accessories); each reforge picks randomly.
    /// </summary>
    public interface IPrefixService
    {
        void RegisterBestPrefix(int itemType, int prefixId);

        bool TryGetBestPrefixes(int itemType, out IReadOnlyList<int> prefixIds);

        bool TryGetRandomBestPrefix(int itemType, out int prefixId);
    }
}