using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Bridge
{
    /// <summary>
    /// Player-update hooks. The host dispatches these from an On_Player.ItemCheck detour for the
    /// local player, matching the framework's "Harmony prefix on Player.ItemCheck" contract.
    /// </summary>
    internal sealed class BridgePlayerUpdateRegistry : IPlayerUpdateHookRegistry
    {
        private readonly List<IPlayerUpdateHook> _hooks = new List<IPlayerUpdateHook>();

        public void Add(IPlayerUpdateHook hook)
        {
            if (hook != null && !_hooks.Contains(hook))
                _hooks.Add(hook);
        }

        public void Remove(IPlayerUpdateHook hook)
        {
            if (hook != null)
                _hooks.Remove(hook);
        }

        public void Dispatch(Action<Exception> onError)
        {
            for (var i = 0; i < _hooks.Count; i++)
            {
                try { _hooks[i].OnPreUpdate(); }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
        }
    }

    /// <summary>
    /// Info-accessory hooks. The host dispatches these from a ModPlayer.PostUpdateEquips pass for the
    /// local player (right after vanilla rebuilds the info-accessory flags).
    /// </summary>
    internal sealed class BridgeInfoAccessoryRegistry : IInfoAccessoryHookRegistry
    {
        private readonly List<IInfoAccessoryHook> _hooks = new List<IInfoAccessoryHook>();

        public void Add(IInfoAccessoryHook hook)
        {
            if (hook != null && !_hooks.Contains(hook))
                _hooks.Add(hook);
        }

        public void Remove(IInfoAccessoryHook hook)
        {
            if (hook != null)
                _hooks.Remove(hook);
        }

        public void Dispatch(object localPlayer, Action<Exception> onError)
        {
            for (var i = 0; i < _hooks.Count; i++)
            {
                try { _hooks[i].OnRefreshInfoAccessories(localPlayer); }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
        }
    }

    /// <summary>
    /// Map-overlay hooks. The host dispatches these from ModSystem.PostDrawFullscreenMap with a
    /// <see cref="MapOverlayInfo"/> describing the fullscreen map transform (minimap overlay is not
    /// bridged in v1). Runs inside the open map SpriteBatch — hooks draw directly, never Begin/End.
    /// </summary>
    internal sealed class BridgeMapOverlayRegistry : IMapOverlayHookRegistry
    {
        private readonly List<IMapOverlayHook> _hooks = new List<IMapOverlayHook>();

        public void Add(IMapOverlayHook hook)
        {
            if (hook != null && !_hooks.Contains(hook))
                _hooks.Add(hook);
        }

        public void Remove(IMapOverlayHook hook)
        {
            if (hook != null)
                _hooks.Remove(hook);
        }

        public void Dispatch(MapOverlayInfo info, ref string hoverText, Action<Exception> onError)
        {
            for (var i = 0; i < _hooks.Count; i++)
            {
                try { _hooks[i].OnDrawMap(info, ref hoverText); }
                catch (Exception ex) { onError?.Invoke(ex); }
            }
        }
    }
}
