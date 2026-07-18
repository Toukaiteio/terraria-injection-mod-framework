using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Holds registered info-accessory hooks and dispatches them from the RefreshInfoAccs postfix.
    /// Registered into the service registry as <see cref="IInfoAccessoryHookRegistry"/>.
    /// </summary>
    internal sealed class InfoAccessoryHookRegistry : IInfoAccessoryHookRegistry
    {
        private readonly ILogger _log;
        private readonly List<IInfoAccessoryHook> _hooks = new List<IInfoAccessoryHook>();
        private readonly object _lock = new object();

        public InfoAccessoryHookRegistry(ILogger log)
        {
            _log = log;
        }

        public void Add(IInfoAccessoryHook hook)
        {
            if (hook == null)
                return;
            lock (_lock)
            {
                if (!_hooks.Contains(hook))
                    _hooks.Add(hook);
            }
        }

        public void Remove(IInfoAccessoryHook hook)
        {
            if (hook == null)
                return;
            lock (_lock)
            {
                _hooks.Remove(hook);
            }
        }

        public void Dispatch(object localPlayer)
        {
            IInfoAccessoryHook[] snapshot;
            lock (_lock)
            {
                if (_hooks.Count == 0)
                    return;
                snapshot = _hooks.ToArray();
            }

            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].OnRefreshInfoAccessories(localPlayer);
                }
                catch (Exception ex)
                {
                    _log.Error("IInfoAccessoryHook.OnRefreshInfoAccessories failed", ex);
                }
            }
        }
    }
}
