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
        /// Optional minimum version string (informational for now; compared as System.Version when possible).
        /// </summary>
        public string MinVersion { get; set; }
    }
}
