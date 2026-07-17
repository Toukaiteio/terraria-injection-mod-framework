using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional marker for the mod entry type. If absent, the first public non-abstract IMod is used.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class TimfModAttribute : Attribute
    {
    }
}
