namespace TIMF.Abstractions
{
    /// <summary>
    /// Capability marker: this mod uses client-process features
    /// (UI, keybinds, local player hooks, overlays, PostDraw).
    ///
    /// Loader inference: alone → <see cref="TimfSide.Client"/>;
    /// with <see cref="IAuthorityMod"/> → <see cref="TimfSide.Both"/>.
    /// </summary>
    public interface IClientMod : IMod
    {
    }
}
