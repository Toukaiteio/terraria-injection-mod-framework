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
        /// Classifies this mod for preparation right after injection (before the main menu),
        /// e.g. framework libraries, service publishers, and content mods. When false (default)
        /// the mod is world-staged: it loads when the player enters a world and unloads again on
        /// returning to the main menu. Authority-only mods still wait for authority activation.
        /// </summary>
        public bool LoadBeforeWorld { get; set; }

        /// <summary>
        /// Optional assertion of the capability side. When set it must match exactly what the
        /// capability interfaces (<see cref="IClientMod"/> / <see cref="IAuthorityMod"/>) imply —
        /// it documents intent and fails the load on drift, it does not override inference.
        /// Leave unset to just infer.
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
        /// Whether joining peers need matching code. Orthogonal to <see cref="Side"/>.
        ///
        /// Defaults to <see cref="TimfNetProfile.Vanilla"/>: a mod stays vanilla-join
        /// compatible unless it explicitly opts into the handshake. Requires an
        /// <see cref="TimfSide.Authority"/> half to be anything else.
        /// </summary>
        public TimfNetProfile Net { get; set; } = TimfNetProfile.Vanilla;
    }
}
