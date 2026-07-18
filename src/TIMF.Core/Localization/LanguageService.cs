using System;
using Terraria.Localization;
using TIMF.Abstractions;

namespace TIMF.Core.Localization
{
    /// <summary>
    /// Tracks <see cref="Language.ActiveCulture"/> and raises <see cref="LanguageChanged"/>
    /// when the player switches language. Polled each UI frame + hooks OnLanguageChanged when available.
    /// </summary>
    internal sealed class LanguageService : ILanguageService
    {
        private readonly ILogger _log;
        private string _current = "en-US";
        private bool _hooked;
        private bool _loggedHookFail;

        public event Action LanguageChanged;

        public LanguageService(ILogger log)
        {
            _log = log;
            TryReadCulture(forceEvent: false);
            TryHook();
        }

        public string CurrentLanguage
        {
            get { return _current; }
        }

        /// <summary>Call once per frame from the UI pass so late culture changes are noticed.</summary>
        public void Poll()
        {
            TryHook();
            TryReadCulture(forceEvent: true);
        }

        private void TryHook()
        {
            if (_hooked)
                return;
            try
            {
                // LanguageManager.Instance.OnLanguageChanged += ...
                var mgr = LanguageManager.Instance;
                if (mgr == null)
                    return;

                LanguageChangeCallback cb = _ =>
                {
                    try { TryReadCulture(forceEvent: true); }
                    catch { /* ignore */ }
                };
                mgr.OnLanguageChanged += cb;
                _hooked = true;
                _log.Info("LanguageService hooked LanguageManager.OnLanguageChanged");
            }
            catch (Exception ex)
            {
                if (!_loggedHookFail)
                {
                    _loggedHookFail = true;
                    _log.Warn("LanguageService hook failed (will poll): " + ex.Message);
                }
            }
        }

        private void TryReadCulture(bool forceEvent)
        {
            string name = null;
            try
            {
                var culture = Language.ActiveCulture;
                if (culture != null)
                    name = culture.Name;
            }
            catch
            {
                // game not ready
            }

            if (string.IsNullOrEmpty(name))
                name = "en-US";

            if (string.Equals(name, _current, StringComparison.OrdinalIgnoreCase))
                return;

            var prev = _current;
            _current = name;
            _log.Info("Language changed: " + prev + " → " + name);

            if (forceEvent || prev != null)
            {
                try
                {
                    var h = LanguageChanged;
                    if (h != null)
                        h();
                }
                catch (Exception ex)
                {
                    _log.Error("LanguageChanged subscriber failed", ex);
                }
            }
        }
    }
}
