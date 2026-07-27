using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace WorldMapIcons
{
    /// <summary>
    /// Reflection bridge to the game's texture assets / helpers whose ReLogic types differ between
    /// compile-time and runtime. Resolves NPC / projectile textures, item draw frames, projectile
    /// frame counts, and map reveal checks.
    /// </summary>
    internal sealed class GameTextures
    {
        private readonly ILogger _log;
        private readonly ITerrariaReflection _reflection;

        private Array _npcAssets;       // TextureAssets.Npc  (Asset<Texture2D>[])
        private Array _projAssets;      // TextureAssets.Projectile
        private PropertyInfo _assetValueProp; // Asset<Texture2D>.Value
        private int[] _projFrames;      // Main.projFrames
        private MethodInfo _getItemDrawFrame; // Main.GetItemDrawFrame(int, out Texture2D, out Rectangle)
        private object _map;            // Main.Map
        private MethodInfo _isRevealed; // WorldMap.IsRevealed(int,int)
        private bool _resolved;
        private bool _loggedItemFail;

        public GameTextures(ILogger log, ITerrariaReflection reflection)
        {
            _log = log;
            _reflection = reflection;
        }

        public bool Ready => Resolve();

        public Texture2D NpcTexture(int type)
        {
            if (!Resolve() || _npcAssets == null || type < 0 || type >= _npcAssets.Length)
                return null;
            return AssetValue(_npcAssets.GetValue(type));
        }

        public Texture2D ProjectileTexture(int type)
        {
            if (!Resolve() || _projAssets == null || type < 0 || type >= _projAssets.Length)
                return null;
            return AssetValue(_projAssets.GetValue(type));
        }

        public int ProjectileFrameCount(int type)
        {
            if (_projFrames != null && type >= 0 && type < _projFrames.Length)
            {
                var f = _projFrames[type];
                return f <= 0 ? 1 : f;
            }
            return 1;
        }

        public bool GetItemDrawFrame(int type, out Texture2D texture, out Rectangle frame)
        {
            texture = null;
            frame = Rectangle.Empty;
            if (!Resolve() || _getItemDrawFrame == null)
                return false;

            try
            {
                var args = new object[] { type, null, null };
                _reflection.Invoke(_getItemDrawFrame, null, args);
                texture = args[1] as Texture2D;
                frame = args[2] is Rectangle r ? r : Rectangle.Empty;
                return texture != null;
            }
            catch (Exception ex)
            {
                if (!_loggedItemFail)
                {
                    _loggedItemFail = true;
                    _log.Error("GetItemDrawFrame invoke failed", ex);
                }
                return false;
            }
        }

        public bool IsRevealed(int tileX, int tileY)
        {
            if (!Resolve() || _map == null || _isRevealed == null)
                return true; // if unknown, don't block
            try
            {
                var res = _reflection.Invoke(_isRevealed, _map, new object[] { tileX, tileY });
                return res is bool b && b;
            }
            catch
            {
                return true;
            }
        }

        private Texture2D AssetValue(object asset)
        {
            if (asset == null || _assetValueProp == null)
                return null;
            try
            {
                return _reflection.GetPropertyValue(_assetValueProp, asset, null) as Texture2D;
            }
            catch
            {
                return null;
            }
        }

        private bool Resolve()
        {
            if (_resolved)
                return _npcAssets != null;
            _resolved = true;

            try
            {
                var asm = typeof(Main).Assembly;
                var texAssets = asm.GetType("Terraria.GameContent.TextureAssets");
                if (texAssets != null)
                {
                    _npcAssets = _reflection.GetFieldValue(texAssets.GetField("Npc", BindingFlags.Public | BindingFlags.Static), null) as Array;
                    _projAssets = _reflection.GetFieldValue(texAssets.GetField("Projectile", BindingFlags.Public | BindingFlags.Static), null) as Array;
                }

                // Asset<Texture2D>.Value property — grab from a sample element type.
                if (_npcAssets != null && _npcAssets.Length > 0)
                {
                    var sample = _npcAssets.GetValue(0);
                    if (sample != null)
                        _assetValueProp = sample.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                }

                _projFrames = _reflection.GetFieldValue(typeof(Main).GetField("projFrames", BindingFlags.Public | BindingFlags.Static), null) as int[];

                _getItemDrawFrame = typeof(Main).GetMethod(
                    "GetItemDrawFrame",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int), typeof(Texture2D).MakeByRefType(), typeof(Rectangle).MakeByRefType() },
                    null);

                var mapProp = typeof(Main).GetProperty("Map", BindingFlags.Public | BindingFlags.Static);
                _map = mapProp == null ? null : _reflection.GetPropertyValue(mapProp, null, null);
                if (_map != null)
                {
                    _isRevealed = _map.GetType().GetMethod(
                        "IsRevealed",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(int), typeof(int) },
                        null);
                }

                if (_npcAssets == null || _assetValueProp == null)
                    _log.Warn("WorldMapIcons: NPC texture assets not resolved");

                return _npcAssets != null;
            }
            catch (Exception ex)
            {
                _log.Error("WorldMapIcons texture reflection failed", ex);
                return false;
            }
        }
    }
}
