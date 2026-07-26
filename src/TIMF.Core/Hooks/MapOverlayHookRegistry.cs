using System;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Holds registered map-overlay hooks and dispatches them from the MapIconOverlay.Draw postfix.
    /// Registered into the service registry as <see cref="IMapOverlayHookRegistry"/>.
    /// </summary>
    internal sealed class MapOverlayHookRegistry
        : HookRegistryBase<IMapOverlayHook>, IMapOverlayHookRegistry
    {
        public MapOverlayHookRegistry(ILogger log) : base(log) { }

        /// <summary>Dispatch to all hooks. Returns hover text if any hook set it.</summary>
        public string Dispatch(MapOverlayInfo info, string hoverText)
        {
            var snapshot = Snapshot();
            if (snapshot == null)
                return hoverText;

            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i].OnDrawMap(info, ref hoverText);
                }
                catch (Exception ex)
                {
                    Report("OnDrawMap", ex);
                }
            }

            return hoverText;
        }
    }
}
