using Microsoft.Xna.Framework.Input;

namespace TIMF.Abstractions
{
    /// <summary>
    /// Framework keybind registry. Registers into vanilla <c>PlayerInput.KnownTriggers</c>
    /// so hotkeys appear in Settings → Controls and use the same rebinding / save path.
    ///
    /// Resolve via <c>context.Services.TryGetService(out IKeybindService svc)</c> in <c>Load</c>.
    /// </summary>
    public interface IKeybindService
    {
        /// <summary>
        /// Register (or return existing) a keybind.
        /// <paramref name="id"/> must be unique across mods — prefer "ModId.Action".
        /// <paramref name="defaultKey"/> is applied only when the profile has no binding yet.
        /// </summary>
        IKeybind Register(string id, string displayName, Keys defaultKey);

        /// <summary>Unregister a keybind previously created by <see cref="Register"/>.</summary>
        void Unregister(string id);

        /// <summary>Lookup a previously registered keybind, or null.</summary>
        IKeybind Get(string id);

        bool TryGet(string id, out IKeybind keybind);
    }
}
