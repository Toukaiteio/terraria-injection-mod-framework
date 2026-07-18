namespace TIMF.Abstractions
{
    /// <summary>
    /// Per-frame hook that runs as a Harmony prefix on Player.ItemCheck for the local player.
    /// That is AFTER input CopyInto and BEFORE item-use processing, so setting controlUseItem /
    /// mouse aim here is not overwritten by real mouse state later in the same tick.
    ///
    /// Register via <see cref="IPlayerUpdateHookRegistry"/> from <see cref="IModContext.Services"/>.
    /// </summary>
    public interface IPlayerUpdateHook
    {
        /// <summary>Called once per local-player ItemCheck, immediately before the method body.</summary>
        void OnPreUpdate();
    }
}
