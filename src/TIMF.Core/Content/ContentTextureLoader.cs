using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Puts modded item textures into <c>TextureAssets.Item</c>.
    ///
    /// Everything here is late-bound on purpose. Terraria references ReLogic as an external
    /// assembly but ships no ReLogic.dll beside the exe — it loads one embedded in its own
    /// resources. A compile-time reference to a ReLogic.dll extracted from anywhere else
    /// produces a <em>different</em> assembly identity, so <c>TextureAssets.Item</c> resolves
    /// to a field whose signature does not match and every access throws
    /// MissingFieldException. Reflecting against whatever the running game actually loaded
    /// sidesteps the whole question.
    /// </summary>
    internal sealed class ContentTextureLoader
    {
        private readonly ILogger _log;
        private readonly ContentManager _content;
        private bool _done;

        public ContentTextureLoader(ILogger log, ContentManager content)
        {
            _log = log;
            _content = content;
        }

        public int LoadedCount { get; private set; }
        public int PlaceholderCount { get; private set; }

        /// <summary>Runs once, on the render thread, after the id space has been widened.</summary>
        public void EnsureLoaded(Func<string, string> resolveModDirectory)
        {
            if (_done || !_content.HasContent || !_content.IsActivated)
                return;

            GraphicsDevice device;
            try
            {
                device = Main.instance?.GraphicsDevice;
                if (device == null)
                    return;   // graphics not up yet; retry next frame
            }
            catch
            {
                return;
            }

            var itemSlots = TextureAssetSlots.Resolve(_log, "Item");
            var tileSlots = TextureAssetSlots.Resolve(_log, "Tile");
            var wallSlots = TextureAssetSlots.Resolve(_log, "Wall");
            if (itemSlots == null && tileSlots == null && wallSlots == null)
            {
                _done = true;   // no point retrying a missing field every frame
                return;
            }

            _done = true;

            foreach (var kv in _content.ItemsById)
            {
                var id = kv.Key;
                var def = kv.Value;
                Texture2D texture = null;

                try
                {
                    var dir = resolveModDirectory(def.ModId);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var path = Path.Combine(dir, def.Texture.Replace('/', Path.DirectorySeparatorChar));
                        if (!Path.HasExtension(path))
                            path += ".png";

                        if (File.Exists(path))
                        {
                            using (var fs = File.OpenRead(path))
                                texture = Texture2D.FromStream(device, fs);
                        }
                        else
                        {
                            _log.Warn("Content: texture not found for " + def.ContentKey + " at " + path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Content: failed to load texture for " + def.ContentKey, ex);
                }

                if (texture == null)
                {
                    texture = CreatePlaceholder(device);
                    PlaceholderCount++;
                }
                else
                {
                    LoadedCount++;
                }

                if (itemSlots == null || !itemSlots.Assign(id, texture, def.ContentKey))
                    _log.Error("Content: could not install a texture asset for " + def.ContentKey);
            }

            _content.TexturesLoaded = LoadedCount;
            _content.TexturesPlaceholder = PlaceholderCount;

            foreach (var kv in _content.TilesById)
            {
                var id = kv.Key;
                var def = kv.Value;
                Texture2D texture = null;

                try
                {
                    var dir = resolveModDirectory(def.ModId);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var path = Path.Combine(dir, def.Texture.Replace('/', Path.DirectorySeparatorChar));
                        if (!Path.HasExtension(path))
                            path += ".png";

                        if (File.Exists(path))
                        {
                            using (var fs = File.OpenRead(path))
                                texture = Texture2D.FromStream(device, fs);
                        }
                        else
                        {
                            _log.Warn("Content: tile texture not found for " + def.ContentKey + " at " + path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Content: failed to load tile texture for " + def.ContentKey, ex);
                }

                if (texture == null)
                {
                    texture = CreatePlaceholder(device);
                    PlaceholderCount++;
                    _content.TileTexturesPlaceholder++;
                }
                else
                {
                    LoadedCount++;
                    _content.TileTexturesLoaded++;
                }

                if (tileSlots == null || !tileSlots.Assign(id, texture, def.ContentKey))
                    _log.Error("Content: could not install a tile texture asset for " + def.ContentKey);
            }

            foreach (var kv in _content.WallsById)
            {
                var id = kv.Key;
                var def = kv.Value;
                Texture2D texture = null;
                try
                {
                    var dir = resolveModDirectory(def.ModId);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var path = Path.Combine(dir, def.Texture.Replace('/', Path.DirectorySeparatorChar));
                        if (!Path.HasExtension(path)) path += ".png";
                        if (File.Exists(path))
                        {
                            using (var fs = File.OpenRead(path))
                                texture = Texture2D.FromStream(device, fs);
                        }
                        else _log.Warn("Content: wall texture not found for " + def.ContentKey + " at " + path);
                    }
                }
                catch (Exception ex) { _log.Error("Content: failed to load wall texture for " + def.ContentKey, ex); }

                if (texture == null)
                {
                    texture = CreatePlaceholder(device);
                    PlaceholderCount++;
                    _content.WallTexturesPlaceholder++;
                }
                else
                {
                    LoadedCount++;
                    _content.WallTexturesLoaded++;
                }
                if (wallSlots == null || !wallSlots.Assign(id, texture, def.ContentKey))
                    _log.Error("Content: could not install a wall texture asset for " + def.ContentKey);
            }

            _log.Info("Content: textures ready (" + LoadedCount + " loaded, "
                      + PlaceholderCount + " placeholder)");
        }

        /// <summary>Magenta 16x16 so a missing texture is visible rather than invisible.</summary>
        private static Texture2D CreatePlaceholder(GraphicsDevice device)
        {
            var tex = new Texture2D(device, 16, 16);
            var pixels = new Microsoft.Xna.Framework.Color[16 * 16];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Microsoft.Xna.Framework.Color(255, 0, 255);
            tex.SetData(pixels);
            return tex;
        }
    }

    /// <summary>
    /// Late-bound accessor for <c>TextureAssets.Item</c> and the <c>Asset&lt;Texture2D&gt;</c>
    /// type behind it, resolved from the assemblies the game actually loaded.
    /// </summary>
    internal sealed class TextureAssetSlots
    {
        private readonly ILogger _log;
        private readonly Array _array;
        private readonly ConstructorInfo _ctor;
        private readonly MethodInfo _submit;
        private readonly MethodInfo _setValue;
        private readonly MethodInfo _setState;
        private readonly Type _stateType;
        private readonly string _fieldName;

        private TextureAssetSlots(ILogger log, Array array, Type assetType, string fieldName)
        {
            _log = log;
            _array = array;
            _fieldName = fieldName;

            _ctor = assetType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            _submit = assetType.GetMethod("SubmitLoadedContent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var valueProp = assetType.GetProperty("Value",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _setValue = valueProp?.GetSetMethod(true);

            var stateProp = assetType.GetProperty("State",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _setState = stateProp?.GetSetMethod(true);
            _stateType = stateProp?.PropertyType;
        }

        public static TextureAssetSlots Resolve(ILogger log, string fieldName = "Item")
        {
            try
            {
                var taType = typeof(Main).Assembly.GetType("Terraria.GameContent.TextureAssets");
                if (taType == null)
                {
                    log.Error("Content: Terraria.GameContent.TextureAssets not found");
                    return null;
                }

                var field = taType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (field == null)
                {
                    log.Error("Content: TextureAssets." + fieldName + " field not found");
                    return null;
                }

                var array = field.GetValue(null) as Array;
                if (array == null)
                {
                    log.Error("Content: TextureAssets." + fieldName + " is null");
                    return null;
                }

                var assetType = array.GetType().GetElementType();
                log.Info("Content: TextureAssets." + fieldName + " resolved, length " + array.Length
                         + ", element " + assetType?.FullName
                         + " from " + assetType?.Assembly.GetName().Name);
                return new TextureAssetSlots(log, array, assetType, fieldName);
            }
            catch (Exception ex)
            {
                log.Error("Content: could not resolve TextureAssets." + fieldName, ex);
                return null;
            }
        }

        /// <summary>
        /// Fill any null slot at or above <paramref name="from"/> with a clone of a known-good
        /// vanilla asset. A null here is not a blank icon, it is a NullReferenceException in
        /// vanilla's inventory draw loop, which aborts the loop and makes every later slot
        /// disappear — so no modded id may ever be left holding null.
        /// </summary>
        public int BackfillNulls(int from)
        {
            var donor = FindDonor();
            if (donor == null)
                return 0;

            var filled = 0;
            for (var i = from; i < _array.Length; i++)
            {
                if (_array.GetValue(i) != null)
                    continue;
                _array.SetValue(donor, i);
                filled++;
            }
            return filled;
        }

        /// <summary>
        /// Re-point anything that captured the pre-expansion <c>TextureAssets.Item</c> array.
        ///
        /// Expansion swaps in a new array, but objects built earlier keep the reference they
        /// were handed. <c>Main.ItemMapIconRenderer</c> is an <c>OutlinedTextureRenderer</c>
        /// constructed with <c>TextureAssets.Item</c> during load, so it holds the old
        /// 6147-long array plus a parallel <c>_contents</c> array of the same length. Asking it
        /// to outline a modded id then indexes past the end, and because that happens between
        /// SpriteBatch.Begin and End the batch is never closed — the visible failure is the
        /// *next* frame dying in PrepareRenderTarget with "Begin cannot be called again",
        /// one frame away from the real cause.
        ///
        /// Matching is by reference against the old array so sibling renderers over unrelated
        /// texture arrays (NPC heads, which TIMF does not expand) are left alone.
        /// </summary>
        public static int RepointCapturedArrays(ILogger log, Array oldArray, Array newArray)
        {
            if (oldArray == null || newArray == null || ReferenceEquals(oldArray, newArray))
                return 0;

            var fixedUp = 0;
            try
            {
                var listField = typeof(Main).GetField("ContentThatNeedsRenderTargets",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var list = listField?.GetValue(null) as System.Collections.IEnumerable;
                if (list == null)
                {
                    log.Warn("Content: Main.ContentThatNeedsRenderTargets unavailable; "
                             + "renderers may keep a stale texture array");
                    return 0;
                }

                foreach (var holder in list)
                {
                    if (holder == null)
                        continue;

                    var type = holder.GetType();
                    var matchField = type.GetField("_matchingArray",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (matchField == null)
                        continue;

                    if (!ReferenceEquals(matchField.GetValue(holder) as Array, oldArray))
                        continue;

                    matchField.SetValue(holder, newArray);

                    // _contents is indexed in lockstep with _matchingArray, so it has to grow too.
                    var contentsField = type.GetField("_contents",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var contents = contentsField?.GetValue(holder) as Array;
                    if (contents != null && contents.Length < newArray.Length)
                    {
                        var grown = Array.CreateInstance(
                            contents.GetType().GetElementType(), newArray.Length);
                        Array.Copy(contents, grown, contents.Length);
                        contentsField.SetValue(holder, grown);
                    }

                    fixedUp++;
                    log.Info("Content: repointed " + type.Name + " onto the expanded texture array");
                }
            }
            catch (Exception ex)
            {
                log.Error("Content: repointing captured texture arrays failed", ex);
            }

            return fixedUp;
        }

        private object FindDonor()
        {
            for (var i = 1; i < Math.Min(_array.Length, 100); i++)
            {
                var v = _array.GetValue(i);
                if (v != null)
                    return v;
            }
            _log.Warn("Content: no vanilla texture asset available to backfill with");
            return null;
        }

        public bool Assign(int id, Texture2D texture, string contentKey)
        {
            try
            {
                if (id < 0 || id >= _array.Length)
                {
                    _log.Error("Content: TextureAssets." + _fieldName + " too small for id " + id
                               + " (len " + _array.Length + ")");
                    return false;
                }

                var asset = Build(texture, contentKey);
                if (asset == null)
                    return false;

                _array.SetValue(asset, id);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Content: assigning texture for " + contentKey + " failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Return the already-loaded texture stored in this slot. Custom assets are injected
        /// directly and are not registered in Main.Assets' vanilla name repository, so callers
        /// such as TilePaintSystem must consume their Value instead of requesting Name again.
        /// </summary>
        public Texture2D GetTexture(int id)
        {
            try
            {
                if (id < 0 || id >= _array.Length)
                    return null;

                var asset = _array.GetValue(id);
                if (asset == null)
                    return null;

                var value = asset.GetType().GetProperty("Value",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return value?.GetValue(asset, null) as Texture2D;
            }
            catch (Exception ex)
            {
                _log.Warn("Content: could not read TextureAssets." + _fieldName
                          + "[" + id + "].Value: " + ex.Message);
                return null;
            }
        }

        private object Build(Texture2D texture, string contentKey)
        {
            if (_ctor == null)
            {
                _log.Error("Content: Asset<Texture2D>(string) constructor not found");
                return null;
            }

            object asset;
            try { asset = _ctor.Invoke(new object[] { "TIMF/" + contentKey }); }
            catch (Exception ex)
            {
                _log.Error("Content: Asset construction failed for " + contentKey, ex);
                return null;
            }

            // Preferred: the entry point the real content pipeline uses.
            if (_submit != null && _submit.GetParameters().Length == 2)
            {
                try
                {
                    _submit.Invoke(asset, new object[] { texture, null });
                    return asset;
                }
                catch
                {
                    // A null content source can upset internal bookkeeping; fall through.
                }
            }

            try
            {
                _setValue?.Invoke(asset, new object[] { texture });
                if (_setState != null && _stateType != null)
                    _setState.Invoke(asset, new[] { Enum.Parse(_stateType, "Loaded") });
                return asset;
            }
            catch (Exception ex)
            {
                _log.Error("Content: could not mark asset loaded for " + contentKey, ex);
                return null;
            }
        }
    }
}
