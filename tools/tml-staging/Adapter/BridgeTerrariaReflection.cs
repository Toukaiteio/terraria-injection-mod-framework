using System;
using System.Reflection;
using TIMF.Abstractions.Security;

namespace TIMF.Bridge
{
    /// <summary>
    /// Narrow reflection broker (ported from Core's TerrariaReflectionService). Only accepts members
    /// declared by the assembly that hosts <see cref="Terraria.Main"/> (tModLoader.dll under tML),
    /// with the same sensitive-identity blacklist and string/stream parameter refusal. Property reads
    /// additionally allow ReLogic / XNA-named asset assemblies (needed for Asset&lt;T&gt;.Value).
    /// </summary>
    internal sealed class BridgeTerrariaReflection : ITerrariaReflection
    {
        public object Invoke(MethodInfo method, object instance, object[] arguments)
        {
            ValidateTerrariaMethod(method);
            return method.Invoke(instance, arguments);
        }

        public object GetFieldValue(FieldInfo field, object instance)
        {
            ValidateMember(field, false);
            return field.GetValue(instance);
        }

        public void SetFieldValue(FieldInfo field, object instance, object value)
        {
            ValidateMember(field, false);
            field.SetValue(instance, value);
        }

        public object GetPropertyValue(PropertyInfo property, object instance, object[] index)
        {
            ValidateMember(property, true);
            return property.GetValue(instance, index);
        }

        internal static void ValidateTerrariaMethod(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (method.DeclaringType == null || method.DeclaringType.Assembly != typeof(Terraria.Main).Assembly)
                throw new UnauthorizedAccessException("Broker accepts only methods declared by the Terraria assembly.");
            var identity = (method.DeclaringType.FullName + "." + method.Name).ToLowerInvariant();
            foreach (var marker in new[] { "file", "directory", "save", "load", "path", "process", "shell", "registry" })
                if (identity.Contains(marker))
                    throw new UnauthorizedAccessException("Potentially sensitive Terraria method is not brokerable: " + identity);
            foreach (var p in method.GetParameters())
                if (p.ParameterType == typeof(string) || typeof(System.IO.Stream).IsAssignableFrom(p.ParameterType))
                    throw new UnauthorizedAccessException("Methods accepting strings or streams are not brokerable.");
        }

        private static void ValidateMember(MemberInfo member, bool allowGameDependency)
        {
            if (member == null || member.DeclaringType == null)
                throw new ArgumentNullException(nameof(member));
            var asm = member.DeclaringType.Assembly;
            var allowed = asm == typeof(Terraria.Main).Assembly;
            if (allowGameDependency)
            {
                var name = asm.GetName().Name ?? "";
                allowed |= name == "ReLogic" || name.StartsWith("Microsoft.Xna.Framework", StringComparison.Ordinal);
            }
            if (!allowed)
                throw new UnauthorizedAccessException("Reflection member is not declared by Terraria or an approved game asset assembly.");
            var identity = (member.DeclaringType.FullName + "." + member.Name).ToLowerInvariant();
            foreach (var marker in new[] { "file", "directory", "save", "load", "path", "process", "shell", "registry" })
                if (identity.Contains(marker))
                    throw new UnauthorizedAccessException("Potentially sensitive reflection member is not brokerable: " + identity);
        }
    }
}
