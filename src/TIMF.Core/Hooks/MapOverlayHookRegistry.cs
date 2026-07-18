using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Holds registered map-overlay hooks and dispatches them from the MapIconOverlay.Draw postfix.
    /// Registered into the service registry as <see cref="IMapOverlayHookRegistry"/>.
    /// </summary>
    internal sealed class MapOverlayHookRegistry : IMapOverlayHookRegistry
    {
        private readonly ILogger _log;
        private readonly List<IMapOverlayHook> _hooks = new List<IMapOverlayHook>();
        private readonly object _lock = new object();

        public MapOverlayHookRegistry(ILogger log)
        {
            _log = log;
        }

        public void Add(IMapOverlayHook hook)
        {
            if (hook == null)
                return;
            lock (_lock)
            {
                if (!_hooks.Contains(hook))
                    _hooks.Add(hook);
            }
        }

        public void Remove(IMapOverlayHook hook)
        {
            if (hook == null)
                return;
            lock (_lock)
            {
                _hooks.Remove(hook);
            }
        }

        /// <summary>Dispatch to all hooks. Returns hover text if any hook set it.</summary>
        public string Dispatch(MapOverlayInfo info, string hoverText)
        {
            IMapOverlayHook[] snapshot;
            lock (_lock)
            {
                if (_hooks.Count == 0)
                    return hoverText;
                snapshot = _hooks.ToArray();
            }

            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].OnDrawMap(info, ref hoverText);
                }
                catch (Exception ex)
                {
                    _log.Error("IMapOverlayHook.OnDrawMap failed", ex);
                }
            }

            return hoverText;
        }
    }
}
