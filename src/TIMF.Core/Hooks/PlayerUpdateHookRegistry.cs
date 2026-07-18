using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Holds all registered player-update hooks and dispatches them once per local-player tick.
    /// Registered into the service registry as <see cref="IPlayerUpdateHookRegistry"/>.
    /// </summary>
    internal sealed class PlayerUpdateHookRegistry : IPlayerUpdateHookRegistry
    {
        private readonly ILogger _log;
        private readonly List<IPlayerUpdateHook> _hooks = new List<IPlayerUpdateHook>();
        private readonly object _lock = new object();

        public PlayerUpdateHookRegistry(ILogger log)
        {
            _log = log;
        }

        public void Add(IPlayerUpdateHook hook)
        {
            if (hook == null)
                return;
            lock (_lock)
            {
                if (!_hooks.Contains(hook))
                    _hooks.Add(hook);
            }
        }

        public void Remove(IPlayerUpdateHook hook)
        {
            if (hook == null)
                return;
            lock (_lock)
            {
                _hooks.Remove(hook);
            }
        }

        public void Dispatch()
        {
            IPlayerUpdateHook[] snapshot;
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
                    snapshot[i].OnPreUpdate();
                }
                catch (Exception ex)
                {
                    _log.Error("IPlayerUpdateHook.OnPreUpdate failed", ex);
                }
            }
        }
    }
}
