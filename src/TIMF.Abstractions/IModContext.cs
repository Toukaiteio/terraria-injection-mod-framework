namespace TIMF.Abstractions
{
    /// <summary>
    /// Services provided by the framework to a loaded mod.
    /// Use <see cref="Client"/> / <see cref="Authority"/> for side-scoped APIs.
    /// </summary>
    public interface IModContext
    {
        ILogger Log { get; }

        /// <summary>Root TIMF home (logs, config, mods).</summary>
        string HomeDirectory { get; }

        /// <summary>Shared config directory (Home/config).</summary>
        string ConfigDirectory { get; }

        /// <summary>Directory containing this mod's assembly (its own folder under Mods/).</summary>
        string ModDirectory { get; }

        /// <summary>
        /// Directory for this mod's bundled assets. Defaults to ModDirectory, or
        /// ModDirectory/Content if that folder exists.
        /// </summary>
        string ContentDirectory { get; }

        /// <summary>Full path of the mod assembly.</summary>
        string ModAssemblyPath { get; }

        /// <summary>
        /// Cross-mod service bag (library mods, framework services).
        /// Prefer <see cref="Client"/> / <see cref="Authority"/> for typed side APIs.
        /// </summary>
        IServiceRegistry Services { get; }

        /// <summary>
        /// This mod's localization catalog (files under <c>Localization/*.json</c>).
        /// </summary>
        IModLocalization L { get; }

        /// <summary>
        /// Client-process APIs (UI, keybinds, local hooks).
        /// Null on dedicated server — never use without a null check.
        /// </summary>
        IClientServices Client { get; }

        /// <summary>
        /// Authority helpers. Never null; gate work with
        /// <see cref="IAuthorityServices.IsAuthoritative"/>.
        /// </summary>
        IAuthorityServices Authority { get; }
    }
}
