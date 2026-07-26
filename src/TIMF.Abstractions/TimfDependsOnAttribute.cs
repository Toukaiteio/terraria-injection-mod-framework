using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Declares a hard dependency on another TIMF mod by <see cref="IMod.Name"/> (or [TimfMod(Id=...)]).
    /// The dependent mod will not load if the target is missing or failed.
    /// Multiple attributes are allowed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class TimfDependsOnAttribute : Attribute
    {
        public TimfDependsOnAttribute(string modId)
        {
            ModId = modId ?? "";
        }

        /// <summary>Target mod id (IMod.Name / TimfModAttribute.Id).</summary>
        public string ModId { get; }

        /// <summary>
        /// Optional minimum version of the target mod. Enforced at load: when the target
        /// reports a lower version, the dependent mod fails to load with a logged reason.
        ///
        /// Format: 1–4 dotted numbers with an optional pre-release suffix (<c>1.2</c>,
        /// <c>1.2.0</c>, <c>1.2.0.3</c>, <c>1.2.0-beta.1</c>); a leading <c>v</c> is tolerated.
        /// Numeric components compare first, and a pre-release ranks below the same numeric
        /// version without one (<c>1.2.0-beta</c> &lt; <c>1.2.0</c>).
        ///
        /// The check fails closed: if either this string or the target's
        /// <see cref="IMod.Version"/> cannot be parsed, the dependency is rejected rather
        /// than assumed satisfied.
        /// </summary>
        public string MinVersion { get; set; }
    }
}
