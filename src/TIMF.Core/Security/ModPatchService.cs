using System;
using System.Reflection;
using HarmonyLib;
using TIMF.Abstractions.Security;

namespace TIMF.Core.Security
{
    internal sealed class ModPatchService : IModPatchService
    {
        private readonly string _harmonyId;
        private readonly Assembly _modAssembly;
        private readonly Harmony _harmony;

        public ModPatchService(string modId, Assembly modAssembly)
        {
            _modAssembly = modAssembly ?? throw new ArgumentNullException(nameof(modAssembly));
            _harmonyId = "timf.safe-mod." + (modId ?? "unknown") + "." + Guid.NewGuid().ToString("N");
            _harmony = new Harmony(_harmonyId);
        }

        public void PatchPostfix(MethodInfo original, MethodInfo postfix)
            => Patch(original, null, postfix);

        public void PatchPrefix(MethodInfo original, MethodInfo prefix)
            => Patch(original, prefix, null);

        public void Patch(MethodInfo original, MethodInfo prefix, MethodInfo postfix)
        {
            TerrariaReflectionService.ValidateTerrariaMethod(original);
            ValidateCallback(prefix);
            ValidateCallback(postfix);
            if (prefix == null && postfix == null)
                throw new ArgumentException("At least one patch callback is required.");
            _harmony.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix));
        }

        public void UnpatchAll() => _harmony.UnpatchAll(_harmonyId);

        private void ValidateCallback(MethodInfo callback)
        {
            if (callback == null) return;
            if (callback.DeclaringType == null || callback.DeclaringType.Assembly != _modAssembly)
                throw new UnauthorizedAccessException("Patch callback must be declared by the requesting mod assembly.");
            if (!callback.IsStatic)
                throw new UnauthorizedAccessException("Patch callback must be static.");
        }
    }
}
