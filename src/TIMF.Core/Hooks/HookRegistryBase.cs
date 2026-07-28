using System;
using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Shared hook storage that enforces the <see cref="TimfHookAttribute"/> declared on the
    /// hook interface, instead of each registry hand-rolling the same process check.
    /// </summary>
    internal abstract class HookRegistryBase<THook> where THook : class
    {
        private static readonly TimfSide AllowedSide = ReadAllowedSide();

        protected readonly ILogger Log;
        private readonly Func<object, bool> _executionAllowed;
        private readonly Action<object, string, Exception> _faultReporter;
        private readonly List<THook> _hooks = new List<THook>();
        private readonly object _lock = new object();

        protected HookRegistryBase(ILogger log, Func<object, bool> executionAllowed,
            Action<object, string, Exception> faultReporter = null)
        {
            Log = log;
            _executionAllowed = executionAllowed;
            _faultReporter = faultReporter;
        }

        public void Add(THook hook)
        {
            if (hook == null)
                return;

            if (!CurrentProcessAllows())
            {
                Log.Error(typeof(THook).Name + ".Add rejected: hook is declared "
                          + AllowedSide + "-side and cannot register on a dedicated server");
                return;
            }

            lock (_lock)
            {
                if (!_hooks.Contains(hook))
                    _hooks.Add(hook);
            }
        }

        public void Remove(THook hook)
        {
            if (hook == null)
                return;
            lock (_lock)
            {
                _hooks.Remove(hook);
            }
        }

        /// <summary>Snapshot for dispatch, or null when empty so callers can skip the loop.</summary>
        protected THook[] Snapshot()
        {
            lock (_lock)
            {
                if (_hooks.Count == 0)
                    return null;
                if (_executionAllowed == null)
                    return _hooks.ToArray();
                var allowed = new List<THook>(_hooks.Count);
                foreach (var hook in _hooks)
                    if (_executionAllowed(hook))
                        allowed.Add(hook);
                return allowed.Count == 0 ? null : allowed.ToArray();
            }
        }

        protected void Report(string member, Exception ex)
        {
            Log.Error(typeof(THook).Name + "." + member + " failed", ex);
        }

        /// <summary>
        /// Report a hook fault attributed to the mod that owns <paramref name="hook"/>. Routes to the
        /// watchdog when one is wired (which logs and may auto-disable a repeatedly-faulting mod);
        /// otherwise falls back to a plain log so dispatch is never silent.
        /// </summary>
        protected void Report(object hook, string member, Exception ex)
        {
            if (_faultReporter != null)
                _faultReporter(hook, typeof(THook).Name + "." + member, ex);
            else
                Report(member, ex);
        }

        /// <summary>
        /// A dedicated server never gets a local player, so a client-only hook could never fire
        /// there. The authority bit is deliberately not gated on <c>netMode</c>: any process may
        /// gain authority later (menu → host), so registration must stay open and the hook body
        /// gates its writes on <see cref="IAuthorityServices.IsAuthoritative"/> instead.
        /// </summary>
        private static bool CurrentProcessAllows()
        {
            if (AllowedSide != TimfSide.Client)
                return true;
            return !IsDedicated();
        }

        private static bool IsDedicated()
        {
            try { return Terraria.Main.dedServ; }
            catch { return false; }
        }

        private static TimfSide ReadAllowedSide()
        {
            try
            {
                var attr = (TimfHookAttribute)Attribute.GetCustomAttribute(
                    typeof(THook), typeof(TimfHookAttribute));
                if (attr != null)
                    return attr.Side;
            }
            catch { /* fall through to permissive default */ }

            // Undeclared hooks stay permissive rather than silently dropping registrations.
            return TimfSide.Both;
        }
    }
}
