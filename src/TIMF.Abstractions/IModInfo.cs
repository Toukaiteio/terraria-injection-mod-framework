namespace TIMF.Abstractions
{
    /// <summary>
    /// Read-only info about a discovered / loaded mod, exposed via <see cref="IModRegistry"/>.
    /// </summary>
    public interface IModInfo
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }

        /// <summary>Capability side (Client / Authority / Both), inferred from the mod's interfaces.</summary>
        TimfSide Side { get; }

        /// <summary>
        /// Protocol axis — whether joining peers need matching code. Orthogonal to
        /// <see cref="Side"/>; prefer this over switching on <see cref="Side"/> when the
        /// question is "does this break vanilla-join compatibility".
        /// </summary>
        TimfNetProfile NetProfile { get; }

        /// <summary>
        /// User preference: when false the mod is skipped on load / server activate
        /// (see <see cref="IModRegistry.TrySetEnabled"/>).
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>True when <see cref="IMod.Load"/> has completed for this process.</summary>
        bool IsLoaded { get; }

        /// <summary>True when this session has activated the mod's authority half.</summary>
        bool ServerLogicActive { get; }

        /// <summary>
        /// Non-null when the mod implements <see cref="IModSettings"/>, is loaded, and the
        /// current session permits opening its settings surface.
        /// </summary>
        IModSettings Settings { get; }

        bool HasSettings { get; }
    }
}
