namespace TIMF.Abstractions
{
    /// <summary>
    /// Read-only info about a discovered / loaded mod, exposed via <see cref="IModRegistry"/>.
    /// </summary>
    public interface IModInfo
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }

        /// <summary>Declared side (Client / Server / Both / Plugin).</summary>
        TimfSide Side { get; }

        /// <summary>
        /// User preference: when false the mod is skipped on load / server activate
        /// (see <see cref="IModRegistry.TrySetEnabled"/>).
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>True when <see cref="IMod.Load"/> has completed for this process.</summary>
        bool IsLoaded { get; }

        /// <summary>True when this session has activated server-side logic for Server/Both mods.</summary>
        bool ServerLogicActive { get; }

        /// <summary>Non-null when the mod implements <see cref="IModSettings"/> and is currently loaded.</summary>
        IModSettings Settings { get; }

        bool HasSettings { get; }
    }
}
