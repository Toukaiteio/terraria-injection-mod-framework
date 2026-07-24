using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional marker for the mod entry type. If absent, the first public non-abstract
    /// <see cref="IMod"/> is used and side is inferred from capability interfaces.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class TimfModAttribute : Attribute
    {
        private TimfSide _side = TimfSide.Client;

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
        /// Explicit side override. When set, must be consistent with capability interfaces
        /// (<see cref="IClientMod"/> / <see cref="IAuthorityMod"/> / <see cref="IVanillaPlugin"/>).
        /// When left unset, the loader infers side from those interfaces.
        /// </summary>
        public TimfSide Side
        {
            get { return _side; }
            set
            {
                _side = value;
                SideSpecified = true;
            }
        }

        /// <summary>True when <see cref="Side"/> was assigned on the attribute.</summary>
        public bool SideSpecified { get; private set; }

        /// <summary>
        /// When side is Server or Both: host requires joining clients to have this mod.
        /// Ignored (always false) for Plugin / <see cref="IVanillaPlugin"/>.
        /// Default true for handshake server mods.
        /// </summary>
        public bool RequiredOnJoin { get; set; } = true;
    }
}
