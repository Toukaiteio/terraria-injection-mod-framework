using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    /// <summary>
    /// Stability guard for the sandbox's runtime half. It counts unhandled exceptions thrown by a
    /// mod inside framework-dispatched callbacks (per-frame draw, hook registries) and, when a mod
    /// faults too often within a short window, disables it for the rest of the process. This keeps a
    /// single misbehaving mod from spamming the log or destabilising the game frame after frame.
    ///
    /// Disabled mods stay resident but inert (see <c>ModLoader.IsExecutionAllowed</c>): unloading a
    /// mod mid-frame — with its live Harmony patches and UI state — is itself a stability risk, so we
    /// use the same fail-inert model the session gate already relies on.
    /// </summary>
    internal sealed class ModWatchdog
    {
        private readonly ILogger _log;
        private readonly int _threshold;
        private readonly TimeSpan _window;
        private readonly Action<ModDescriptor, string> _onDisabled;
        private readonly object _sync = new object();
        private readonly Dictionary<ModDescriptor, Queue<DateTime>> _faults =
            new Dictionary<ModDescriptor, Queue<DateTime>>();

        public ModWatchdog(ILogger log, Action<ModDescriptor, string> onDisabled,
            int threshold = 10, TimeSpan? window = null)
        {
            _log = log;
            _threshold = threshold < 1 ? 1 : threshold;
            _window = window ?? TimeSpan.FromSeconds(10);
            _onDisabled = onDisabled;
        }

        /// <summary>Record one callback fault for a mod; escalate to disable if it is now repeating.</summary>
        public void ReportFault(ModDescriptor d, string phase, Exception ex)
        {
            if (d == null)
                return;

            // Every fault is logged; disabling is a separate escalation so isolated glitches stay
            // visible without silencing the mod.
            _log.Error("Mod '" + d.Id + "' threw in " + phase, ex);

            string reason = null;
            lock (_sync)
            {
                if (d.RuntimeDisabled)
                    return;

                Queue<DateTime> q;
                if (!_faults.TryGetValue(d, out q))
                {
                    q = new Queue<DateTime>();
                    _faults[d] = q;
                }

                var now = DateTime.UtcNow;
                q.Enqueue(now);
                while (q.Count > 0 && now - q.Peek() > _window)
                    q.Dequeue();

                if (q.Count >= _threshold)
                {
                    reason = q.Count + " exceptions within " + (int)_window.TotalSeconds
                             + "s (last in " + phase + ": " + ex.GetType().Name + ")";
                    d.RuntimeDisabled = true;
                    d.RuntimeDisableReason = reason;
                    _faults.Remove(d);
                }
            }

            if (reason == null)
                return;

            _log.Warn("Mod '" + d.Id + "' auto-disabled for stability: " + reason
                      + ". It stays loaded but will receive no further callbacks this session.");
            try { _onDisabled?.Invoke(d, reason); }
            catch { /* a notification failure must never break the dispatch that reported the fault */ }
        }
    }
}
