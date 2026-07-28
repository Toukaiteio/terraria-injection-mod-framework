using System;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Holds all registered player-update hooks and dispatches them once per local-player tick.
    /// Registered into the service registry as <see cref="IPlayerUpdateHookRegistry"/>.
    /// </summary>
    internal sealed class PlayerUpdateHookRegistry
        : HookRegistryBase<IPlayerUpdateHook>, IPlayerUpdateHookRegistry
    {
        public PlayerUpdateHookRegistry(ILogger log, Func<object, bool> executionAllowed,
            Action<object, string, Exception> faultReporter = null)
            : base(log, executionAllowed, faultReporter) { }

        public void Dispatch()
        {
            var snapshot = Snapshot();
            if (snapshot == null)
                return;

            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].OnPreUpdate();
                }
                catch (Exception ex)
                {
                    Report(snapshot[i], "OnPreUpdate", ex);
                }
            }
        }
    }
}
