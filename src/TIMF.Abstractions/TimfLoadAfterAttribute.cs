using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Soft ordering hint: load this mod after <paramref name="modId"/> when both are present.
    /// Does not fail if the other mod is missing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class TimfLoadAfterAttribute : Attribute
    {
        public TimfLoadAfterAttribute(string modId)
        {
            ModId = modId ?? "";
        }

        public string ModId { get; }
    }
}
