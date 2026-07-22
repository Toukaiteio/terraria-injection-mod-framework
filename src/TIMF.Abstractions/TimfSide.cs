namespace TIMF.Abstractions
{
    /// <summary>
    /// Which process role(s) a TIMF mod participates in.
    /// Default is <see cref="Client"/> (existing client-only mods need no attribute).
    /// </summary>
    public enum TimfSide
    {
        /// <summary>Client / UI / overlay only. Never requires the handshake protocol.</summary>
        Client = 0,

        /// <summary>
        /// Server-authoritative logic only. Loaded when Singleplayer, Host &amp; Play,
        /// dedicated server, or a joined multiplayer session that completed TIMF handshake
        /// with this mod on the host list.
        /// </summary>
        Server = 1,

        /// <summary>
        /// Client path loads immediately; server path is activated via
        /// <see cref="IServerMod"/> when the session allows server logic.
        /// </summary>
        Both = 2,
    }
}
