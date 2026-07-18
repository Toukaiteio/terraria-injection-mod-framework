namespace TIMF.Abstractions
{
    /// <summary>
    /// Lets mods register info-accessory hooks. Resolve from <see cref="IModContext.Services"/>
    /// and call <see cref="Add"/> in Load. Core invokes every hook after the local player's
    /// info flags are rebuilt (<c>Player.UpdateEquips</c> every frame, and
    /// <c>Player.RefreshInfoAccs</c> when the inventory is open).
    /// </summary>
    public interface IInfoAccessoryHookRegistry
    {
        void Add(IInfoAccessoryHook hook);
        void Remove(IInfoAccessoryHook hook);
    }
}
