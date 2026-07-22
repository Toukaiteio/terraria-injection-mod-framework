using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional marker for the mod entry type. If absent, the first public non-abstract IMod is used.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class TimfModAttribute : Attribute
    {
        /// <summary>
        /// Stable id used for dependencies. Defaults to <see cref="IMod.Name"/> when empty.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Comma-separated hard dependency ids (alternative to multiple [TimfDependsOn]).
        /// </summary>
        public string Dependencies { get; set; }

        /// <summary>
        /// Comma-separated soft load-after ids (alternative to [TimfLoadAfter]).
        /// </summary>
        public string LoadAfter { get; set; }

        /// <summary>
        /// Client / Server / Both. Default <see cref="TimfSide.Client"/> (no handshake exposure).
        /// </summary>
        public TimfSide Side { get; set; } = TimfSide.Client;

        /// <summary>
        /// When <see cref="Side"/> is Server or Both: host requires joining clients to have this mod.
        /// Missing required mods causes a kick. Default true.
        /// </summary>
        public bool RequiredOnJoin { get; set; } = true;
    }
}
