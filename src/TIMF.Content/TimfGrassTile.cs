namespace TIMF.Content
{
    /// <summary>Framework-managed grass tile that spreads only during vanilla world updates.</summary>
    public abstract class TimfGrassTile : TimfTile
    {
        /// <summary>Whether this grass can replace the specified active substrate tile.</summary>
        public abstract bool CanGrowOn(int substrateTileType);

        /// <summary>
        /// Substrate restored when an old sidecar has no recorded origin for this grass cell.
        /// New placements and natural spread remember the exact replaced type. Return -1 to
        /// destroy legacy/untracked grass normally instead of guessing.
        /// </summary>
        public virtual int DefaultSubstrateTileType => -1;

        /// <summary>Maximum orthogonal spread attempts for one sampled random update.</summary>
        public virtual int SpreadAttempts => 1;

        /// <summary>Additional environmental gate such as depth, liquid or biome state.</summary>
        public virtual bool CanSpreadAt(int i, int j) => true;
    }
}
