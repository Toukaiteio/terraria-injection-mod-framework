using System.Collections.Generic;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Registry of discovered TIMF mods (enabled and disabled). Registered by Core into
    /// <see cref="IModContext.Services"/> after discovery — resolve it lazily
    /// (e.g. in PostDraw), not during Load.
    /// </summary>
    public interface IModRegistry
    {
        /// <summary>All discovered mods in discovery/load order (includes disabled).</summary>
        IReadOnlyList<IModInfo> Mods { get; }

        /// <summary>
        /// Enable or disable a mod by id. May Load/Unload immediately. Once a world/session is
        /// active, the switch of any Authority-capable mod is locked until returning to menu.
        /// Disabled mods are skipped on next process start and do not participate in server handshake.
        ///
        /// Dependency inference keeps the set consistent: disabling a mod also cascade-disables every
        /// enabled mod that hard-depends on it, and enabling a mod first turns on its disabled hard
        /// dependencies (failing if any is missing, failed, or below a declared MinVersion). The
        /// <paramref name="message"/> lists any mods that were also toggled or the blocking reason.
        /// </summary>
        /// <returns>false if the mod was not found or the change was rejected.</returns>
        bool TrySetEnabled(string id, bool enabled, out string message);
    }
}
