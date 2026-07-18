namespace TIMF.Abstractions
{
    /// <summary>
    /// Per-mod localization catalog. Loads JSON key/value files from the mod's
    /// <c>Localization/</c> folder and tracks the game's active language.
    /// </summary>
    public interface IModLocalization
    {
        /// <summary>Current culture name, e.g. "en-US", "zh-Hans" (from the game).</summary>
        string CurrentLanguage { get; }

        /// <summary>
        /// Resolve a key. Fallback chain: active culture → language base (zh-Hans→zh) →
        /// en-US → en → <paramref name="fallback"/> → key itself.
        /// </summary>
        string Get(string key, string fallback = null);

        /// <summary><see cref="Get"/> then <c>string.Format</c> with <paramref name="args"/>.</summary>
        string Format(string key, params object[] args);

        /// <summary>True if the key exists in any loaded catalog for the active language chain.</summary>
        bool Has(string key);
    }
}
