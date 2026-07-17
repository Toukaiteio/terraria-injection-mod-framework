using System.Collections.Generic;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Registry of successfully loaded mods, in load order. Registered by Core into
    /// <see cref="IModContext.Services"/> after all mods finish loading — resolve it lazily
    /// (e.g. in PostDraw), not during Load.
    /// </summary>
    public interface IModRegistry
    {
        IReadOnlyList<IModInfo> Mods { get; }
    }
}
