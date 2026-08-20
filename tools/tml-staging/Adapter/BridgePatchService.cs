using System.Reflection;
using HarmonyLib;
using TIMF.Abstractions.Security;

namespace TIMF.Bridge
{
    /// <summary>
    /// Real <see cref="IModPatchService"/> backed by a per-mod HarmonyLib instance (a net8 0Harmony.dll
    /// is bundled with the bridge — see build.txt dllReferences). Hosted TIMF client mods hand plain
    /// <see cref="MethodInfo"/> targets plus prefix/postfix methods that use Harmony's parameter-name
    /// conventions (__instance / __result / __state / named args); Harmony introspects those by
    /// reflection, so the client mod itself needs no reference to HarmonyLib. UnpatchAll is scoped to
    /// this mod's Harmony id so unloading one mod never removes another's patches.
    /// </summary>
    internal sealed class BridgePatchService : IModPatchService
    {
        private readonly Harmony _harmony;

        public BridgePatchService(string modId)
        {
            var id = string.IsNullOrWhiteSpace(modId) ? "mod" : modId.Trim();
            _harmony = new Harmony("timf.bridge.patch." + id);
        }

        public void PatchPrefix(MethodInfo original, MethodInfo prefix)
        {
            if (original == null || prefix == null)
                return;
            _harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        public void PatchPostfix(MethodInfo original, MethodInfo postfix)
        {
            if (original == null || postfix == null)
                return;
            _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }

        public void Patch(MethodInfo original, MethodInfo prefix, MethodInfo postfix)
        {
            if (original == null || (prefix == null && postfix == null))
                return;
            _harmony.Patch(
                original,
                prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(postfix) : null);
        }

        public void UnpatchAll()
        {
            try { _harmony.UnpatchAll(_harmony.Id); }
            catch { /* best effort on unload */ }
        }
    }
}
