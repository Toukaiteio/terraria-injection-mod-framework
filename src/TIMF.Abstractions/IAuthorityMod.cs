namespace TIMF.Abstractions
{
    /// <summary>
    /// Capability marker: this mod contains world-authoritative logic
    /// (loot, NPC, world rules).
    ///
    /// Loader inference: alone → <see cref="TimfSide.Authority"/>;
    /// with <see cref="IClientMod"/> → <see cref="TimfSide.Both"/>.
    ///
    /// By default the mod stays vanilla-join compatible: it is not advertised on the
    /// handshake and pure vanilla clients can still join a host running it. Opt into the
    /// handshake with <c>[TimfMod(Net = TimfNetProfile.Optional / Required)]</c> when the
    /// logic needs matching code on the peer.
    /// </summary>
    public interface IAuthorityMod : IMod
    {
    }
}
