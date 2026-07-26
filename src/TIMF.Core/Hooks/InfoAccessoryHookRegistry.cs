using System;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Holds registered info-accessory hooks and dispatches them from the RefreshInfoAccs postfix.
    /// Registered into the service registry as <see cref="IInfoAccessoryHookRegistry"/>.
    /// </summary>
    internal sealed class InfoAccessoryHookRegistry
        : HookRegistryBase<IInfoAccessoryHook>, IInfoAccessoryHookRegistry
    {
        public InfoAccessoryHookRegistry(ILogger log) : base(log) { }

        public void Dispatch(object localPlayer)
        {
            var snapshot = Snapshot();
            if (snapshot == null)
                return;

            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].OnRefreshInfoAccessories(localPlayer);
                }
                catch (Exception ex)
                {
                    Report("OnRefreshInfoAccessories", ex);
                }
            }
        }
    }
}
