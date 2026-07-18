namespace TIMF.Abstractions
{
    /// <summary>
    /// Lets mods register map-overlay draw hooks. Resolve from <see cref="IModContext.Services"/>
    /// and call <see cref="Add"/> in Load. Core invokes every hook from a Harmony postfix on the
    /// vanilla MapIconOverlay.Draw, so icons render on the fullscreen map and minimap.
    /// </summary>
    public interface IMapOverlayHookRegistry
    {
        void Add(IMapOverlayHook hook);
        void Remove(IMapOverlayHook hook);
    }
}
