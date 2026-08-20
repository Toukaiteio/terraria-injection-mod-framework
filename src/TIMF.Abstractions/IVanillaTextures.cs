using Microsoft.Xna.Framework.Graphics;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Fetches a raw <see cref="Texture2D"/> from a vanilla <c>TextureAssets</c> array field
    /// (for example "WireUi") by index, resolved through the game's own loaded ReLogic in Core.
    ///
    /// Client mods must not reference a ReLogic.dll of their own: Terraria loads ReLogic from an
    /// assembly embedded in its exe, so a mod's compile-time reference resolves to a different
    /// assembly identity and every access to an <c>Asset&lt;Texture2D&gt;</c>-typed field throws
    /// MissingFieldException at runtime. This service hands back the already-decoded XNA texture,
    /// so mods can reuse genuine vanilla art with no ReLogic dependency and no reflection of their own.
    /// </summary>
    public interface IVanillaTextures
    {
        /// <summary>
        /// The live vanilla texture at <paramref name="index"/> of the static
        /// <c>Terraria.GameContent.TextureAssets.<paramref name="arrayFieldName"/></c> array,
        /// or null if the field/element is unavailable or not loaded yet.
        /// </summary>
        Texture2D Get(string arrayFieldName, int index);
    }
}
