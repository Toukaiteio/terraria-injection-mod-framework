namespace TIMF.Abstractions
{
    /// <summary>
    /// Capability marker: this mod runs world-authoritative logic
    /// (loot, NPC, world rules). Use <see cref="IVanillaPlugin"/> when the
    /// host must stay joinable by pure vanilla clients.
    ///
    /// Loader inference: alone → <see cref="TimfSide.Server"/>;
    /// with <see cref="IClientMod"/> → <see cref="TimfSide.Both"/>.
    /// </summary>
    public interface IAuthorityMod : IMod
    {
    }
}
