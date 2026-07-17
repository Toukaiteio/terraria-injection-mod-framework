using System.Collections.Generic;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModInfo : IModInfo
    {
        public ModInfo(string id, string name, string version, IMod instance)
        {
            Id = id;
            Name = name;
            Version = version;
            Settings = instance as IModSettings;
        }

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public IModSettings Settings { get; }
        public bool HasSettings => Settings != null;
    }

    internal sealed class ModRegistry : IModRegistry
    {
        private readonly List<IModInfo> _mods = new List<IModInfo>();

        public IReadOnlyList<IModInfo> Mods => _mods;

        public void Add(IModInfo info)
        {
            _mods.Add(info);
        }
    }
}
