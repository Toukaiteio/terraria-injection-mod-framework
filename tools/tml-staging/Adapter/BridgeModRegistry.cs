using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Bridge
{
    /// <summary>
    /// Read-only view of one hosted TIMF mod for the Mod Settings hub. Implements both
    /// <see cref="IModInfo"/> and <see cref="IModSessionState"/>.
    ///
    /// The bridge does not hot enable/disable mods (tModLoader owns that via its Mods menu), so the
    /// enable switch is reported locked. Settings pages are always openable for loaded mods that
    /// implement <see cref="IModSettings"/>.
    /// </summary>
    internal sealed class BridgeModInfo : IModInfo, IModSessionState
    {
        private readonly IMod _instance;

        public BridgeModInfo(string id, IMod instance, TimfSide side, TimfNetProfile net)
        {
            Id = id;
            _instance = instance;
            Side = side;
            NetProfile = net;
        }

        public string Id { get; }
        public string Name => string.IsNullOrEmpty(_instance?.Name) ? Id : _instance.Name;
        public string Version => _instance?.Version ?? "";
        public TimfSide Side { get; }
        public TimfNetProfile NetProfile { get; }

        public bool IsEnabled => true;
        public bool IsLoaded => true;
        public bool ServerLogicActive => false;

        public bool HasSettings => _instance is IModSettings;
        public IModSettings Settings => _instance as IModSettings;

        // Session state: managed by tModLoader, so the enable switch is locked but settings are open.
        public bool IsSessionAllowed => true;
        public bool CanChangeEnabled => false;
        public string InteractionLockReason => "Enable/disable is managed by tModLoader's Mods menu.";
        public bool HasSettingsCapability => HasSettings;
        public bool CanOpenSettings => HasSettings;
    }

    /// <summary>
    /// Registry of hosted TIMF mods, published into the shared service registry after discovery so
    /// ModSettingsHub can enumerate them and open settings pages.
    /// </summary>
    internal sealed class BridgeModRegistry : IModRegistry
    {
        private readonly List<IModInfo> _mods;

        public BridgeModRegistry(List<IModInfo> mods)
        {
            _mods = mods ?? new List<IModInfo>();
        }

        public IReadOnlyList<IModInfo> Mods => _mods;

        public bool TrySetEnabled(string id, bool enabled, out string message)
        {
            message = "Use tModLoader's Mods menu to enable or disable mods.";
            return false;
        }
    }
}
