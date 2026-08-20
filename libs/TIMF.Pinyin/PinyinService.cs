using System;

namespace TIMF.Pinyin
{
    // Alias avoids the collision between this mod's namespace (TIMF.Pinyin) and NPinyin's
    // Pinyin class — a bare "Pinyin" here would bind to the namespace, not the type.
    using NPinyinLib = global::NPinyin.Pinyin;
    /// <summary>
    /// <see cref="IPinyinService"/> backed by the NPinyin library (~40k CJK characters).
    /// Provides full pinyin + first-letter initials; non-CJK text passes through unchanged.
    /// This is the single shared implementation that CreativeMode / BlockLocator (and any other
    /// mod) reuse instead of each carrying its own copy of the logic and dataset.
    /// </summary>
    internal sealed class PinyinService : IPinyinService
    {
        public string ToPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            try
            {
                // NPinyin.GetPinyin returns space-separated capitalized pinyin for CJK, and
                // passes non-CJK characters through as-is. Lowercase + strip spaces → "huoba".
                var raw = NPinyinLib.GetPinyin(text);
                return raw.Replace(" ", "").ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        public string ToInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            try
            {
                return NPinyinLib.GetInitials(text).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        public bool Matches(string name, string nameLower, string pinyin, string initials, string queryLower)
        {
            if (string.IsNullOrEmpty(queryLower))
                return true;
            if (!string.IsNullOrEmpty(nameLower) && nameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            if (!string.IsNullOrEmpty(name) && name.IndexOf(queryLower, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(pinyin) && pinyin.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            if (!string.IsNullOrEmpty(initials) && initials.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }
    }
}
