namespace TIMF.Abstractions
{
    /// <summary>
    /// Hook invoked after the local player's info-accessory flags are rebuilt for the frame.
    /// Core posts this after <c>Player.UpdateEquips</c> (every frame) and also after
    /// <c>Player.RefreshInfoAccs</c> (inventory UI path, which recomputes from scratch).
    /// Grant "informational" accessory effects (watch, compass, depth meter, weather, fishing,
    /// etc.) here so the game's UI shows them — without owning the actual items.
    ///
    /// The player is passed as <see cref="object"/> so this abstraction stays free of a
    /// Terraria reference; cast it to Terraria.Player inside your hook.
    /// </summary>
    public interface IInfoAccessoryHook
    {
        /// <summary>
        /// Called after info flags are rebuilt for the local player. Cast
        /// <paramref name="localPlayer"/> to Terraria.Player and set acc* fields as desired.
        /// </summary>
        void OnRefreshInfoAccessories(object localPlayer);
    }
}
