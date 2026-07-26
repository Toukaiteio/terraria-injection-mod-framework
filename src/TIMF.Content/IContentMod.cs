using System.Runtime.CompilerServices;
using TIMF.Abstractions;

[assembly: InternalsVisibleTo("TIMF.Core")]

namespace TIMF.Content
{
    /// <summary>
    /// Capability marker for a mod that adds new game content such as items and world tiles.
    ///
    /// Content is not a new axis on <see cref="TimfSide"/> — it <em>composes</em> the two
    /// existing capabilities, because new content needs both halves: the client half draws
    /// it and reads its name, the authority half spawns and drops it. So this interface
    /// derives from both and the loader infers <see cref="TimfSide.Both"/> as usual.
    ///
    /// Content mods must declare <see cref="TimfNetProfile.Optional"/> or
    /// <see cref="TimfNetProfile.Required"/>: custom ids only mean anything to a peer that
    /// has the same mod, so a vanilla client can never render them. The loader rejects a
    /// content mod left on the default <see cref="TimfNetProfile.Vanilla"/>.
    /// </summary>
    public interface IContentMod : IClientMod, IAuthorityMod
    {
        /// <summary>
        /// Declare this mod's content. Called once during load, before ids are allocated —
        /// register definitions here and do not assume any id is assigned yet.
        /// </summary>
        void AddContent(IContentRegistry registry);
    }
}
