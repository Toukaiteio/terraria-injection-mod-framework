namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional capability of a mod entry type: exposes the mod's primary feature switch.
    /// Backed by the mod's own config (cheap flip + save) — flipping it never loads, unloads,
    /// or re-patches anything, so hubs can offer it freely while a world is active, where the
    /// framework-level mod enable switch is locked to the main menu.
    /// </summary>
    public interface IModFeatureToggle
    {
        /// <summary>The mod's primary feature switch (config-backed; cheap, no load/unload).</summary>
        bool FeatureEnabled { get; set; }
    }
}
