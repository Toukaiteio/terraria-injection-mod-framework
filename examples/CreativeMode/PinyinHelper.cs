using System;
using NPinyin;

namespace CreativeMode
{
    /// <summary>
    /// Chinese pinyin helper for item search, backed by the NPinyin library
    /// (~40k CJK characters). Provides full pinyin + first-letter initials.
    /// Non-CJK text passes through unchanged.
    /// </summary>
    internal static class PinyinHelper
    {
        /// <summary>Full pinyin (lowercase, no spaces, no tones). e.g. "火把" → "huoba".</summary>
        public static string ToPinyin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            try
            {
                // NPinyin.GetPinyin returns space-separated capitalized pinyin for CJK,
                // and passes through non-CJK characters as-is.
                var raw = Pinyin.GetPinyin(text);
                // Lowercase and strip spaces → "huoba"
                return raw.Replace(" ", "").ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>First-letter initials (lowercase). e.g. "火把" → "hb".</summary>
        public static string ToInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            try
            {
                var raw = Pinyin.GetInitials(text);
                return raw.ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        public static bool Matches(string name, string nameLower, string pinyin, string initials, string queryLower)
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
