using System.Reflection;

namespace TIMF.Abstractions.Security
{
    /// <summary>Per-mod broker for postfix-only patches on non-sensitive Terraria methods.</summary>
    public interface IModPatchService
    {
        void PatchPostfix(MethodInfo original, MethodInfo postfix);
        void PatchPrefix(MethodInfo original, MethodInfo prefix);
        void Patch(MethodInfo original, MethodInfo prefix, MethodInfo postfix);
        void UnpatchAll();
    }
}
