namespace TIMF.Abstractions
{
    /// <summary>
    /// Lets multiple mods register per-frame player-update hooks. Resolve this from
    /// <see cref="IModContext.Services"/> and call <see cref="Add"/> in your mod's Load.
    /// Core invokes every registered hook from a single Harmony prefix on Player.ItemCheck.
    /// Deliberately not Player.Update: Update runs ResetControls after the prefix would fire,
    /// so hooks that set controlUseItem / mouse aim only stick when dispatched from ItemCheck.
    /// </summary>
    public interface IPlayerUpdateHookRegistry
    {
        void Add(IPlayerUpdateHook hook);
        void Remove(IPlayerUpdateHook hook);
    }
}
