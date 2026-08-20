using System;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace TIMF.Bridge
{
    // ── Remaining v1 stubs ───────────────────────────────────────────────────────────────────────
    // Keybinds, the three client hooks (player-update / info-accessory / map-overlay), the reflection
    // broker and the mod registry are now real (see BridgeKeybinds / BridgeHookRegistries /
    // BridgeTerrariaReflection / BridgeModRegistry). What stays stubbed is authority (client-only
    // bridge), sensitive host operations (always denied) and the Harmony patch broker — tModLoader
    // ships MonoMod rather than HarmonyLib, so the postfix/prefix broker is not forwarded.

    /// <summary>
    /// Authority is out of scope (client-only). IsAuthoritative is false; the sub-services are null —
    /// a client mod should gate any world writes on IsAuthoritative and never reach them.
    /// </summary>
    internal sealed class BridgeAuthorityServices : IAuthorityServices
    {
        public bool IsAuthoritative => false;
        public IWeatherService Weather => null;
        public IPrefixService Prefix => null;
    }

    /// <summary>Every sensitive request is denied; the bridge grants no host access.</summary>
    internal sealed class BridgeSecurity : ISensitiveOperationService
    {
        private readonly string _modId;

        public BridgeSecurity(string modId)
        {
            _modId = modId;
        }

        private SensitiveOperationRequest Denied(string target, string purpose)
        {
            return new SensitiveOperationRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                ModId = _modId,
                Target = target,
                Purpose = purpose,
                Status = SensitiveOperationStatus.Denied,
                DecisionReason = "Sensitive operations are not available in the tModLoader bridge.",
                CreatedUtc = DateTime.UtcNow,
            };
        }

        public SensitiveOperationRequest RequestFileRead(string path, string purpose) => Denied(path, purpose);
        public SensitiveOperationRequest RequestFileWrite(string path, bool overwrite, string purpose) => Denied(path, purpose);
        public SensitiveOperationRequest RequestProcess(string executable, string arguments, string workingDirectory, string purpose) => Denied(executable, purpose);
        public SensitiveOperationRequest GetRequest(string requestId) => null;
        public void Cancel(string requestId) { }

        public byte[] ReadAllBytes(string requestId) => throw new InvalidOperationException("Denied by TIMF bridge.");
        public void WriteAllBytes(string requestId, byte[] data) => throw new InvalidOperationException("Denied by TIMF bridge.");
        public SensitiveProcessResult RunProcess(string requestId, int timeoutMilliseconds = 30000) => throw new InvalidOperationException("Denied by TIMF bridge.");
    }

    /// <summary>
    /// No-op patch broker. tModLoader ships MonoMod (not HarmonyLib), so the framework's Harmony-style
    /// postfix/prefix broker cannot be forwarded faithfully; mods that need detours should use tML's
    /// own MonoModHooks / On_ hooks. Mods relying on this broker are inert under the bridge.
    /// </summary>
    internal sealed class BridgePatchService : IModPatchService
    {
        public void PatchPostfix(System.Reflection.MethodInfo original, System.Reflection.MethodInfo postfix) { }
        public void PatchPrefix(System.Reflection.MethodInfo original, System.Reflection.MethodInfo prefix) { }
        public void Patch(System.Reflection.MethodInfo original, System.Reflection.MethodInfo prefix, System.Reflection.MethodInfo postfix) { }
        public void UnpatchAll() { }
    }

    /// <summary>Publishes a mod's custom service into the shared registry.</summary>
    internal sealed class BridgeServicePublisher : IModServicePublisher
    {
        private readonly IServiceRegistry _registry;

        public BridgeServicePublisher(IServiceRegistry registry)
        {
            _registry = registry;
        }

        public void Publish<TService>(TService instance) where TService : class
        {
            _registry?.Register(instance);
        }
    }
}
