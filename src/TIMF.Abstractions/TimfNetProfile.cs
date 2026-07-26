namespace TIMF.Abstractions
{
    /// <summary>
    /// How a mod's authority logic relates to the TIMF join protocol.
    ///
    /// This axis is orthogonal to <see cref="TimfSide"/>: side answers "which Terraria
    /// process role does this code belong to" (mirroring <c>Main.netMode</c> / <c>Main.dedServ</c>),
    /// while this answers "does the peer need matching code". Terraria itself has no
    /// equivalent concept — it exists purely at the TIMF layer.
    ///
    /// Values form a strictness ladder: <see cref="Vanilla"/> &lt; <see cref="Optional"/> &lt; <see cref="Required"/>.
    /// </summary>
    public enum TimfNetProfile
    {
        /// <summary>
        /// Authority logic stays within vanilla packet semantics. Not advertised in the
        /// handshake catalog; pure vanilla clients may join a host running it.
        /// Also the value carried by client-only mods, which have no authority half.
        /// </summary>
        Vanilla = 0,

        /// <summary>
        /// Advertised in the handshake catalog and mirrored onto peers that also have it.
        /// Peers missing it are never kicked.
        /// </summary>
        Optional = 1,

        /// <summary>
        /// Advertised in the handshake catalog; the host kicks peers that lack the mod
        /// or carry an older version.
        /// </summary>
        Required = 2,
    }

    /// <summary>Helpers for <see cref="TimfNetProfile"/>.</summary>
    public static class TimfNetProfiles
    {
        /// <summary>True when the mod is advertised on the TIMF handshake.</summary>
        public static bool ParticipatesInHandshake(TimfNetProfile profile)
        {
            return profile >= TimfNetProfile.Optional;
        }

        /// <summary>True when the host rejects peers that lack this mod.</summary>
        public static bool RequiresPeer(TimfNetProfile profile)
        {
            return profile == TimfNetProfile.Required;
        }

        /// <summary>
        /// True when a host running only mods of this profile stays joinable by pure vanilla clients.
        /// </summary>
        public static bool IsVanillaHostCompatible(TimfNetProfile profile)
        {
            return profile == TimfNetProfile.Vanilla;
        }
    }
}
