using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// <see cref="IVanillaTextures"/> backed by <see cref="TextureAssetSlots"/>: it late-binds to
    /// the game's own <c>TextureAssets</c> arrays (through whatever ReLogic the running game
    /// loaded) and returns the raw XNA <see cref="Texture2D"/>. Slots are cached per field.
    /// </summary>
    internal sealed class VanillaTextureService : IVanillaTextures
    {
        private readonly ILogger _log;
        private readonly Dictionary<string, TextureAssetSlots> _cache =
            new Dictionary<string, TextureAssetSlots>(StringComparer.Ordinal);

        public VanillaTextureService(ILogger log)
        {
            _log = log;
        }

        public Texture2D Get(string arrayFieldName, int index)
        {
            if (string.IsNullOrEmpty(arrayFieldName))
                return null;

            TextureAssetSlots slots;
            if (!_cache.TryGetValue(arrayFieldName, out slots) || slots == null)
            {
                // The static array field exists from type init, but its elements only fill in once
                // content is loaded. Resolve can fail early (graphics not up) — don't cache a miss.
                slots = TextureAssetSlots.Resolve(_log, arrayFieldName);
                if (slots != null)
                    _cache[arrayFieldName] = slots;
            }

            return slots != null ? slots.GetTexture(index) : null;
        }
    }
}
