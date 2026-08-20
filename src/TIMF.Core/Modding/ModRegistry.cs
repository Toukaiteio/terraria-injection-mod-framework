using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModInfo : IModInfo, IModSessionState
    {
        public ModInfo(
            string id,
            string name,
            string version,
            TimfSide side,
            TimfNetProfile netProfile,
            bool isEnabled,
            bool isSessionAllowed,
            bool canChangeEnabled,
            string interactionLockReason,
            bool isLoaded,
            bool loadsBeforeWorld,
            bool serverLogicActive,
            bool hasSettings,
            bool canOpenSettings,
            IMod instance)
        {
            Id = id;
            Name = name;
            Version = version;
            Side = side;
            NetProfile = netProfile;
            IsEnabled = isEnabled;
            IsSessionAllowed = isSessionAllowed;
            CanChangeEnabled = canChangeEnabled;
            InteractionLockReason = interactionLockReason;
            IsLoaded = isLoaded;
            LoadsBeforeWorld = loadsBeforeWorld;
            ServerLogicActive = serverLogicActive;
            // Do not merely ask UIs to behave: withhold the callable settings surface while
            // the server/session forbids it, so older hubs cannot invoke BuildSettingsUI.
            Settings = canOpenSettings ? instance as IModSettings : null;
            // Same gating for the feature switch: only reachable while the mod may interact.
            FeatureToggle = canOpenSettings || (isLoaded && isSessionAllowed && isEnabled)
                ? instance as IModFeatureToggle : null;
            HasSettingsCapability = hasSettings;
            CanOpenSettings = canOpenSettings;
        }

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public TimfSide Side { get; }
        public TimfNetProfile NetProfile { get; }
        public bool IsEnabled { get; }
        public bool IsSessionAllowed { get; }
        public bool CanChangeEnabled { get; }
        public string InteractionLockReason { get; }
        public bool IsLoaded { get; }
        public bool LoadsBeforeWorld { get; }
        public IModFeatureToggle FeatureToggle { get; }
        public bool ServerLogicActive { get; }
        public IModSettings Settings { get; }
        public bool HasSettings => Settings != null;
        public bool HasSettingsCapability { get; }
        public bool CanOpenSettings { get; }
    }

    internal sealed class ModRegistry : IModRegistry
    {
        private readonly List<IModInfo> _mods = new List<IModInfo>();
        private readonly ModLoader _loader;

        public ModRegistry(ModLoader loader)
        {
            _loader = loader;
        }

        public IReadOnlyList<IModInfo> Mods => _mods;

        public void Add(IModInfo info)
        {
            _mods.Add(info);
        }

        public bool TrySetEnabled(string id, bool enabled, out string message)
        {
            return _loader.TrySetModEnabled(id, enabled, out message);
        }
    }
}
