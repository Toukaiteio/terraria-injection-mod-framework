namespace TIMF.Abstractions
{
    /// <summary>
    /// Services provided by the framework to a loaded mod.
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
        /// ModDirectory/Content if that folder exists. Use for textures, data files, etc.
        /// </summary>
        string ContentDirectory { get; }

        /// <summary>Full path of the mod assembly.</summary>
        string ModAssemblyPath { get; }

        /// <summary>Cross-mod service registry (UI, future shared libs).</summary>
        IServiceRegistry Services { get; }
    }
}
