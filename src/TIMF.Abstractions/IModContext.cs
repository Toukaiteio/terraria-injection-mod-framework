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

        /// <summary>Directory containing this mod's assembly (and optional content).</summary>
        string ModDirectory { get; }

        /// <summary>Full path of the mod assembly.</summary>
        string ModAssemblyPath { get; }
    }
}
