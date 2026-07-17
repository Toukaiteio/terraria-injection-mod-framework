namespace TIMF.Abstractions
{
    /// <summary>
    /// Read-only info about a loaded mod, exposed via <see cref="IModRegistry"/>.
    /// </summary>
    public interface IModInfo
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }

        /// <summary>Non-null when the mod implements <see cref="IModSettings"/>.</summary>
        IModSettings Settings { get; }

        bool HasSettings { get; }
    }
}
