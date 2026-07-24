using System;

namespace TIMF.Abstractions
{
    /// <summary>Which process role may register a hook interface.</summary>
    public enum TimfHookKind
    {
        /// <summary>Client process only (not dedicated server).</summary>
        Client = 0,

        /// <summary>World authority only (SP / host / dedicated).</summary>
        Authority = 1,

        /// <summary>Either role; rare shared hooks.</summary>
        Any = 2,
    }

    /// <summary>
    /// Marks a hook interface with its allowed side. Registries enforce this at Add-time.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public sealed class TimfHookAttribute : Attribute
    {
        public TimfHookAttribute(TimfHookKind kind)
        {
            Kind = kind;
        }

        public TimfHookKind Kind { get; }

        /// <summary>
        /// When true, the hook is safe for <see cref="TimfSide.Plugin"/> (vanilla net compatible).
        /// Authority hooks default to false (handshake Server/Both).
        /// </summary>
        public bool VanillaSafe { get; set; }
    }
}
