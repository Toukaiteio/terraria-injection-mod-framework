using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Which Terraria process role a mod's code belongs to.
    ///
    /// Mirrors the two independent facts vanilla itself branches on, so a mod declares
    /// its side the same way Terraria's own code decides what to run:
    /// <list type="bullet">
    /// <item><see cref="Client"/> ← <c>!Main.dedServ</c> — there is a local player to draw / read input for.</item>
    /// <item><see cref="Authority"/> ← <c>Main.netMode != 1</c> — this process owns the world simulation.</item>
    /// </list>
    ///
    /// These are orthogonal in vanilla (singleplayer has both, a dedicated server has only
    /// authority, a multiplayer client has only the client half), so this is a flags enum
    /// rather than a list of named combinations.
    ///
    /// Note that <see cref="Authority"/> means "this code is world logic", not "this process
    /// is the server" — exactly like Terraria, which ships world-simulation code inside the
    /// client binary and gates it at runtime. Ask <see cref="IAuthorityServices.IsAuthoritative"/>
    /// whether the current process may actually write.
    ///
    /// Whether joining peers need matching code is a separate question — see <see cref="TimfNetProfile"/>.
    /// </summary>
    [Flags]
    public enum TimfSide
    {
        /// <summary>No capability declared. Never valid for a loaded mod.</summary>
        None = 0,

        /// <summary>Client process: UI, overlays, keybinds, local player hooks.</summary>
        Client = 1 << 0,

        /// <summary>World logic: loot, NPC, world rules. Gate writes on IsAuthoritative.</summary>
        Authority = 1 << 1,

        /// <summary>Both halves. Equivalent to <c>Client | Authority</c>.</summary>
        Both = Client | Authority,
    }

    /// <summary>Helpers for <see cref="TimfSide"/> classification.</summary>
    public static class TimfSides
    {
        public static bool IsClientCapable(TimfSide side)
        {
            return (side & TimfSide.Client) != 0;
        }

        public static bool IsAuthorityCapable(TimfSide side)
        {
            return (side & TimfSide.Authority) != 0;
        }

        /// <summary>
        /// Authority-only mods have nothing to run until the session grants authority,
        /// so the loader defers their assembly load until activation and unloads on deactivate.
        /// A mod with a client half is not deferred by side classification; its concrete
        /// load stage is selected by LoadBeforeWorld / world-staged loading.
        /// </summary>
        public static bool IsDeferredAuthority(TimfSide side)
        {
            return side == TimfSide.Authority;
        }
    }
}
