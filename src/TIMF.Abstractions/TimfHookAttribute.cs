using System;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Marks a hook interface with the process role allowed to register it.
    /// Reuses <see cref="TimfSide"/> so hooks and mods share one vocabulary —
    /// a hook usable from either role is <see cref="TimfSide.Both"/>.
    ///
    /// Hook registries enforce this at Add-time by reading this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public sealed class TimfHookAttribute : Attribute
    {
        public TimfHookAttribute(TimfSide side)
        {
            Side = side;
        }

        /// <summary>Process role(s) permitted to register this hook.</summary>
        public TimfSide Side { get; }
    }
}
