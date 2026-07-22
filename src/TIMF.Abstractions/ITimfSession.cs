using System.Collections.Generic;

namespace TIMF.Abstractions
{
    /// <summary>High-level multiplayer / process role for the current TIMF session.</summary>
    public enum TimfSessionKind
    {
        /// <summary>Main menu or not yet in a world.</summary>
        Menu = 0,

        /// <summary>Local singleplayer world (netMode 0, not dedicated).</summary>
        SinglePlayer = 1,

        /// <summary>Host &amp; Play / listen server in the client process (netMode 2, not dedServ).</summary>
        Host = 2,

        /// <summary>True dedicated server process (Main.dedServ).</summary>
        DedicatedServer = 3,

        /// <summary>Joined a remote multiplayer game (netMode 1).</summary>
        MultiplayerClient = 4,
    }

    /// <summary>One server-side mod entry as advertised or enabled for the session.</summary>
    public interface ITimfRemoteModInfo
    {
        string Id { get; }
        string Version { get; }
    }

    /// <summary>
    /// Current TIMF session role and whether server-side mod logic is active.
    /// Registered as a service before mods Load; state updates as netMode changes.
    /// </summary>
    public interface ITimfSession
    {
        TimfSessionKind Kind { get; }

        /// <summary>True when server-authoritative logic for the enabled set may run.</summary>
        bool ServerLogicEnabled { get; }

        /// <summary>
        /// True on a multiplayer client after the host completed a TIMF handshake.
        /// Always true for SP / host / dedicated once server logic is enabled.
        /// </summary>
        bool RemoteTimfConfirmed { get; }

        /// <summary>Server mods currently activated for this session (host list ∩ local on join).</summary>
        IReadOnlyList<ITimfRemoteModInfo> EnabledServerMods { get; }
    }
}
