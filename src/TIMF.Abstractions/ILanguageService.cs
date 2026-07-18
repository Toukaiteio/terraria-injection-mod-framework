using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Framework language tracker. Mirrors <c>Terraria.Localization.Language.ActiveCulture</c>
    /// and notifies subscribers when the player changes language in Settings.
    /// </summary>
    public interface ILanguageService
    {
        /// <summary>Culture name, e.g. "en-US", "zh-Hans". "en-US" if the game is not ready.</summary>
        string CurrentLanguage { get; }

        /// <summary>Raised after the active language changes (or on first poll).</summary>
        event Action LanguageChanged;
    }
}
