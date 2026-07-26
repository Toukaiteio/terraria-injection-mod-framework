namespace TIMF.Abstractions
{
    /// <summary>
    /// Per-frame hook that runs as a Harmony prefix on Player.ItemCheck for the local player.
    /// Client-only — register via <see cref="IClientServices.PlayerUpdate"/>.
    /// </summary>
    [TimfHook(TimfSide.Client)]
    public interface IPlayerUpdateHook
    {
        /// <summary>Called once per local-player ItemCheck, immediately before the method body.</summary>
        void OnPreUpdate();
    }
}
