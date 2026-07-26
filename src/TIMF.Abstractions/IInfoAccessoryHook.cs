namespace TIMF.Abstractions
{
    /// <summary>
    /// Client-only: after the local player's info-accessory flags are rebuilt for the frame.
    /// Register via <see cref="IClientServices.InfoAccessories"/>.
    /// The player is passed as <see cref="object"/> so this assembly stays free of Terraria;
    /// cast to Terraria.Player inside your hook.
    /// </summary>
    [TimfHook(TimfSide.Client)]
    public interface IInfoAccessoryHook
    {
        /// <summary>
        /// Called after info flags are rebuilt for the local player. Cast
        /// <paramref name="localPlayer"/> to Terraria.Player and set acc* fields as desired.
        /// </summary>
        void OnRefreshInfoAccessories(object localPlayer);
    }
}
