namespace TIMF.Abstractions
{
    /// <summary>
    /// Declared role of a TIMF mod. Prefer capability interfaces
    /// (<see cref="IClientMod"/>, <see cref="IAuthorityMod"/>, <see cref="IVanillaPlugin"/>)
    /// so the loader can infer and validate this value automatically.
    /// </summary>
    public enum TimfSide
    {
        /// <summary>Client process only: UI, overlays, local input. No handshake.</summary>
        Client = 0,

        /// <summary>
        /// Authoritative logic advertised on the TIMF handshake.
        /// Activates on SP / host / dedicated, or on a multiplayer client after handshake.
        /// </summary>
        Server = 1,

        /// <summary>Client path loads immediately; authority path activates with the session.</summary>
        Both = 2,

        /// <summary>
        /// Vanilla-compatible host plugin: authority only, never on multiplayer clients,
        /// never in the handshake catalog, never RequiredOnJoin.
        /// </summary>
        Plugin = 3,
    }

    /// <summary>Helpers for <see cref="TimfSide"/> classification.</summary>
    public static class TimfSides
    {
        public static bool IsClientCapable(TimfSide side)
        {
            return side == TimfSide.Client || side == TimfSide.Both;
        }

        public static bool IsAuthorityCapable(TimfSide side)
        {
            return side == TimfSide.Server
                || side == TimfSide.Both
                || side == TimfSide.Plugin;
        }

        public static bool ParticipatesInHandshake(TimfSide side)
        {
            return side == TimfSide.Server || side == TimfSide.Both;
        }

        public static bool IsDeferredAuthority(TimfSide side)
        {
            return side == TimfSide.Server || side == TimfSide.Plugin;
        }

        public static bool IsVanillaJoinCompatible(TimfSide side)
        {
            return side == TimfSide.Client || side == TimfSide.Plugin;
        }
    }
}
