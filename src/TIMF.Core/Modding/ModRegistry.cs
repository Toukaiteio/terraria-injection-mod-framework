using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModInfo : IModInfo
    {
        public ModInfo(
            string id,
            string name,
            string version,
            TimfSide side,
            TimfNetProfile netProfile,
            bool isEnabled,
            bool isLoaded,
            bool serverLogicActive,
            IMod instance)
        {
            Id = id;
            Name = name;
            Version = version;
            Side = side;
            NetProfile = netProfile;
            IsEnabled = isEnabled;
            IsLoaded = isLoaded;
            ServerLogicActive = serverLogicActive;
            Settings = instance as IModSettings;
        }

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public TimfSide Side { get; }
        public TimfNetProfile NetProfile { get; }
        public bool IsEnabled { get; }
        public bool IsLoaded { get; }
        public bool ServerLogicActive { get; }
        public IModSettings Settings { get; }
        public bool HasSettings => Settings != null;
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
