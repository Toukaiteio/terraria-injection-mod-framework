using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using log4net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;
using TIMF.UI;

namespace TIMF.Bridge
{
    /// <summary>
    /// Discovers recompiled TIMF client mods across the loaded tModLoader mods, builds each an
    /// <see cref="IModContext"/> backed by tML services, and drives their lifecycle plus the shared
    /// immediate-mode UI, keybinds, and the client hooks (player-update / info-accessory / map-overlay).
    /// </summary>
    internal sealed class TimfHost
    {
        private sealed class HostedMod
        {
            public IMod Instance;
            public string Id;
            public BridgeModContext Context;
        }

        private readonly ILogger _log;
        private readonly BridgeServiceRegistry _registry = new BridgeServiceRegistry();
        private readonly BridgeClientServices _client;
        private readonly BridgeKeybindService _keybinds = new BridgeKeybindService();
        private readonly BridgePlayerUpdateRegistry _playerUpdate = new BridgePlayerUpdateRegistry();
        private readonly BridgeMapOverlayRegistry _mapOverlay = new BridgeMapOverlayRegistry();
        private readonly BridgeInfoAccessoryRegistry _infoAcc = new BridgeInfoAccessoryRegistry();
        private readonly List<HostedMod> _mods = new List<HostedMod>();
        private readonly List<IModInfo> _modInfos = new List<IModInfo>();

        private ImmediateModeUi _ui;
        private string _homeDir;
        private string _configDir;
        private GameTime _lastGameTime = new GameTime();
        private bool _started;
        private bool _itemCheckHooked;
        private bool _inItemCheck;

        public TimfHost(BridgeLogger bridgeLog)
        {
            _log = bridgeLog;
            _client = new BridgeClientServices
            {
                Services = _registry,
                Keybinds = _keybinds,
                PlayerUpdate = _playerUpdate,
                MapOverlay = _mapOverlay,
                InfoAccessories = _infoAcc,
            };
        }

        /// <summary>Latest GameTime captured from ModSystem.UpdateUI, used to drive UI timing.</summary>
        public void SetGameTime(GameTime gameTime)
        {
            if (gameTime != null)
                _lastGameTime = gameTime;
        }

        public void Start(Mod bridgeMod, ILog bridgeRawLog)
        {
            if (_started)
                return;
            _started = true;

            try
            {
                _homeDir = Path.Combine(Main.SavePath ?? Path.GetTempPath(), "TimfBridge");
                _configDir = Path.Combine(_homeDir, "config");
                Directory.CreateDirectory(_configDir);
            }
            catch { /* best effort */ }

            // Immediate-mode UI: register both interfaces exactly as the TIMF.UI library mod does.
            try
            {
                _ui = new ImmediateModeUi(new BridgeLogger(bridgeRawLog, "TIMF.UI"));
                _registry.Register<IImmediateModeUi>(_ui);
                _registry.Register<IUiHost>(_ui);
            }
            catch (Exception ex) { _log.Error("Bridge: UI init failed", ex); }

            // Publish the shared client-side services + reflection broker so mods can resolve them.
            _registry.Register<IClientServices>(_client);
            _registry.Register(_client.Keybinds);
            _registry.Register(_client.PlayerUpdate);
            _registry.Register(_client.MapOverlay);
            _registry.Register(_client.InfoAccessories);
            _registry.Register<ITerrariaReflection>(new BridgeTerrariaReflection());

            DiscoverAndLoad(bridgeMod);

            // Registry of hosted mods for ModSettingsHub (resolved lazily by that mod in PostDraw).
            _registry.Register<IModRegistry>(new BridgeModRegistry(_modInfos));

            HookItemCheck();

            _log.Info("Bridge: hosted " + _mods.Count + " TIMF client mod(s)");
        }

        private void DiscoverAndLoad(Mod bridgeMod)
        {
            Mod[] mods;
            try { mods = ModLoader.Mods; }
            catch (Exception ex) { _log.Error("Bridge: could not enumerate loaded mods", ex); return; }

            foreach (var mod in mods)
            {
                if (mod == null || mod.Name == bridgeMod.Name)
                    continue;

                Assembly asm;
                try { asm = mod.Code; }
                catch { asm = null; }
                if (asm == null)
                    continue;

                foreach (var type in SafeGetTypes(asm))
                {
                    if (!IsHostableClientMod(type))
                        continue;
                    try { LoadOne(type, mod); }
                    catch (Exception ex) { _log.Error("Bridge: failed to load TIMF mod type " + type.FullName, ex); }
                }
            }
        }

        private static bool IsHostableClientMod(Type t)
        {
            if (t == null || t.IsAbstract || !t.IsClass)
                return false;
            if (!typeof(IMod).IsAssignableFrom(t))
                return false;
            if (t.GetConstructor(Type.EmptyTypes) == null)
                return false;

            // Hosts client-capable entries only: IClientMod, or a bare IMod (defaults to client).
            // Authority-only mods (IAuthorityMod without a client half) are out of scope.
            var isClient = typeof(IClientMod).IsAssignableFrom(t);
            var isAuthorityOnly = typeof(IAuthorityMod).IsAssignableFrom(t) && !isClient;
            return !isAuthorityOnly;
        }

        private void LoadOne(Type type, Mod owner)
        {
            var instance = (IMod)Activator.CreateInstance(type);

            var attr = (TimfModAttribute)Attribute.GetCustomAttribute(type, typeof(TimfModAttribute));
            var id = attr != null && !string.IsNullOrWhiteSpace(attr.Id)
                ? attr.Id.Trim()
                : (!string.IsNullOrWhiteSpace(instance.Name) ? instance.Name : type.Name);
            var side = attr != null ? attr.Side : TimfSide.Client;
            var net = attr != null ? attr.Net : TimfNetProfile.Vanilla;

            var dataDir = Path.Combine(_homeDir ?? Path.GetTempPath(), "mod-data", Sanitize(id));
            try { Directory.CreateDirectory(dataDir); } catch { }

            string asmPath = null;
            try { asmPath = type.Assembly.Location; } catch { }

            var ctx = new BridgeModContext
            {
                Log = new BridgeLogger(owner.Logger, id),
                HomeDirectory = _homeDir,
                ConfigDirectory = _configDir,
                ModDirectory = dataDir,
                ContentDirectory = dataDir,
                ModAssemblyPath = asmPath,
                Services = _registry,
                L = new BridgeLocalization(owner),
                Client = _client,
                Authority = new BridgeAuthorityServices(),
                Security = new BridgeSecurity(id),
                Storage = new BridgeStorage(dataDir, dataDir),
                Patches = new BridgePatchService(),
                ServicePublisher = new BridgeServicePublisher(_registry),
            };

            instance.Load(ctx);
            _mods.Add(new HostedMod { Instance = instance, Id = id, Context = ctx });
            _modInfos.Add(new BridgeModInfo(id, instance, side, net));
            _log.Info("Bridge: loaded TIMF mod '" + id + "' (from tML mod " + owner.Name + ")");
        }

        // ── Per-frame input ──────────────────────────────────────────────────────────────────────

        /// <summary>Poll keybinds once per frame (from ModSystem.UpdateUI), gated on text-entry focus.</summary>
        public void PollKeybinds()
        {
            var typing = false;
            try
            {
                typing = Main.drawingPlayerChat || Main.editSign || Main.editChest
                         || (_ui != null && _ui.WantCaptureKeyboard);
            }
            catch { /* default: input allowed */ }

            _keybinds.Poll(!typing);
        }

        // ── Client hooks ────────────────────────────────────────────────────────────────────────

        private void HookItemCheck()
        {
            if (_itemCheckHooked || Main.dedServ)
                return;
            try
            {
                Terraria.On_Player.ItemCheck += ItemCheckHook;
                _itemCheckHooked = true;
            }
            catch (Exception ex) { _log.Error("Bridge: could not hook Player.ItemCheck", ex); }
        }

        private void UnhookItemCheck()
        {
            if (!_itemCheckHooked)
                return;
            try { Terraria.On_Player.ItemCheck -= ItemCheckHook; }
            catch { /* ignore */ }
            _itemCheckHooked = false;
        }

        private void ItemCheckHook(Terraria.On_Player.orig_ItemCheck orig, Player self)
        {
            // Prefix semantics for the local player, guarded against re-entrancy (some hooks invoke
            // Player.ItemCheck themselves, e.g. AutoFishing reeling/casting).
            if (!Main.dedServ && self != null && self.whoAmI == Main.myPlayer && !_inItemCheck)
            {
                _inItemCheck = true;
                try { _playerUpdate.Dispatch(ex => _log.Error("Bridge: player-update hook failed", ex)); }
                finally { _inItemCheck = false; }
            }
            orig(self);
        }

        /// <summary>Called from ModPlayer.PostUpdateEquips for the local player.</summary>
        public void DispatchInfoAccessories(object localPlayer)
        {
            if (Main.dedServ)
                return;
            _infoAcc.Dispatch(localPlayer, ex => _log.Error("Bridge: info-accessory hook failed", ex));
        }

        /// <summary>
        /// Called from ModSystem.PostDrawFullscreenMap (inside the open map SpriteBatch). Builds the
        /// fullscreen-map transform and dispatches the map-overlay hooks.
        /// </summary>
        public void RunMapOverlay(ref string mouseText)
        {
            if (Main.dedServ || _mapOverlay == null)
                return;

            MapOverlayInfo info;
            try
            {
                info = new MapOverlayInfo
                {
                    MapPosition = Main.mapFullscreenPos,
                    MapOffset = new Vector2(Main.screenWidth / 2f, Main.screenHeight / 2f),
                    ClippingRect = null,
                    MapScale = Main.mapFullscreenScale,
                    DrawScale = 1f,
                    Alpha = 1f,
                    Fullscreen = true,
                };
            }
            catch (Exception ex) { _log.Error("Bridge: map transform failed", ex); return; }

            _mapOverlay.Dispatch(info, ref mouseText, ex => _log.Error("Bridge: map-overlay hook failed", ex));
        }

        // ── UI frame ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One UI frame, driven from ModSystem.ModifyInterfaceLayers: close the incoming batch →
        /// NewFrame → each mod's PostDraw → Render, then re-open a UI batch so tML's subsequent
        /// interface drawing keeps working.
        /// </summary>
        public void RunUiPass()
        {
            if (Main.dedServ || _ui == null)
                return;

            var gt = _lastGameTime ?? new GameTime();
            var sb = Main.spriteBatch;

            SafeEnd(sb);

            try { _ui.NewFrame(gt); }
            catch (Exception ex) { _log.Error("Bridge: UI NewFrame failed", ex); }

            for (var i = 0; i < _mods.Count; i++)
            {
                try { _mods[i].Instance.PostDraw(gt); }
                catch (Exception ex) { _log.Error("Bridge: PostDraw failed for '" + _mods[i].Id + "'", ex); }
            }

            try { _ui.Render(); }
            catch (Exception ex) { _log.Error("Bridge: UI Render failed", ex); }

            ReopenUiBatch(sb);

            try
            {
                if (_ui.WantCaptureMouse && Main.LocalPlayer != null)
                    Main.LocalPlayer.mouseInterface = true;
            }
            catch { /* ignore */ }
        }

        public void Stop()
        {
            UnhookItemCheck();

            for (var i = _mods.Count - 1; i >= 0; i--)
            {
                try { _mods[i].Instance.Unload(); }
                catch (Exception ex) { _log.Error("Bridge: Unload failed for '" + _mods[i].Id + "'", ex); }
            }
            _mods.Clear();
            _modInfos.Clear();

            try { _ui?.DisposeResources(); }
            catch { /* ignore */ }
            _ui = null;
            _started = false;
        }

        private static void SafeEnd(SpriteBatch sb)
        {
            if (sb == null)
                return;
            try { sb.End(); }
            catch { /* was not begun */ }
        }

        private static void ReopenUiBatch(SpriteBatch sb)
        {
            if (sb == null)
                return;
            try
            {
                sb.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    Main.SamplerStateForCursor,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    Main.UIScaleMatrix);
            }
            catch { /* leave closed rather than crash the frame */ }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            // tML docs: do NOT call Assembly.GetTypes() on Mod.Code (errors with ExtendsFromMod).
            // Use AssemblyManager.GetLoadableTypes, falling back to GetTypes only if unavailable.
            try { return Terraria.ModLoader.Core.AssemblyManager.GetLoadableTypes(asm); }
            catch { }
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return FilterNull(ex.Types); }
            catch { return Array.Empty<Type>(); }
        }

        private static IEnumerable<Type> FilterNull(Type[] types)
        {
            if (types == null)
                yield break;
            foreach (var t in types)
                if (t != null)
                    yield return t;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "mod";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }
    }
}
