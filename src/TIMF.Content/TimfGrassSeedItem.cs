namespace TIMF.Content
{
    /// <summary>
    /// A seed-like item that converts an allowed substrate into a
    /// <see cref="TimfGrassTile"/>. The framework owns the replacement operation; mods only
    /// select the grass definition and declare valid substrates through CanGrowOn.
    /// </summary>
    public abstract class TimfGrassSeedItem : TimfItem
    {
        /// <summary>The allocated tile id of the grass grown by this seed.</summary>
        public abstract int GrassTileType { get; }
    }
}
