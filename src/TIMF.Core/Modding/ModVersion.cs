using System;
using System.Globalization;

namespace TIMF.Core.Modding
{
    /// <summary>
    /// A mod version: 1–4 dotted numeric components with an optional pre-release suffix
    /// (<c>1.2.0-beta.1</c>). A leading <c>v</c> is tolerated.
    ///
    /// Ordering follows semver where it matters here: numeric components compare first,
    /// and a pre-release sorts <em>below</em> the same numeric version without one, so
    /// <c>1.2.0-beta &lt; 1.2.0</c>.
    /// </summary>
    internal struct ModVersion : IComparable<ModVersion>
    {
        private static readonly char[] PreReleaseSeparators = { '-', '+' };

        private readonly int _major;
        private readonly int _minor;
        private readonly int _patch;
        private readonly int _revision;

        /// <summary>Null for a stable release.</summary>
        private readonly string _preRelease;

        private ModVersion(int major, int minor, int patch, int revision, string preRelease)
        {
            _major = major;
            _minor = minor;
            _patch = patch;
            _revision = revision;
            _preRelease = preRelease;
        }

        public static bool TryParse(string text, out ModVersion version)
        {
            version = default(ModVersion);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var s = text.Trim();
            if (s.Length > 1 && (s[0] == 'v' || s[0] == 'V'))
                s = s.Substring(1);

            string pre = null;
            var cut = s.IndexOfAny(PreReleaseSeparators);
            if (cut >= 0)
            {
                pre = s.Substring(cut + 1);
                s = s.Substring(0, cut);
                if (pre.Length == 0)
                    pre = null;
            }

            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4)
                return false;

            var nums = new int[4];
            for (var i = 0; i < parts.Length; i++)
            {
                int n;
                // NumberStyles.None rejects signs, whitespace and thousands separators,
                // so "1. 2", "-1" and "1,000" are all refused rather than coerced.
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n))
                    return false;
                nums[i] = n;
            }

            version = new ModVersion(nums[0], nums[1], nums[2], nums[3], pre);
            return true;
        }

        public int CompareTo(ModVersion other)
        {
            var c = _major.CompareTo(other._major);
            if (c != 0) return c;
            c = _minor.CompareTo(other._minor);
            if (c != 0) return c;
            c = _patch.CompareTo(other._patch);
            if (c != 0) return c;
            c = _revision.CompareTo(other._revision);
            if (c != 0) return c;

            // Same numbers: a stable release outranks any pre-release of it.
            if (_preRelease == null && other._preRelease == null) return 0;
            if (_preRelease == null) return 1;
            if (other._preRelease == null) return -1;
            return string.Compare(_preRelease, other._preRelease, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            var core = _major + "." + _minor + "." + _patch
                       + (_revision != 0 ? "." + _revision : "");
            return _preRelease == null ? core : core + "-" + _preRelease;
        }
    }
}
