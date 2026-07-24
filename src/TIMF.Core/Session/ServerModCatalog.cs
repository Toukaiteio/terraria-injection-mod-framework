using System;
using System.Collections.Generic;
using System.Linq;
using TIMF.Abstractions;
using TIMF.Core.Modding;

namespace TIMF.Core.Session
{
    /// <summary>One server-side mod as advertised over the wire or enabled locally.</summary>
    internal sealed class ServerModEntry : ITimfRemoteModInfo
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public bool RequiredOnJoin { get; set; }

        public ServerModEntry() { }

        public ServerModEntry(string id, string version, bool requiredOnJoin)
        {
            Id = id ?? "";
            Version = version ?? "0.0.0";
            RequiredOnJoin = requiredOnJoin;
        }
    }

    /// <summary>
    /// Local catalog of handshake-visible Server/Both mods (Plugin excluded).
    /// Used for TIMF join protocol only — not the full server-authority set.
    /// </summary>
    internal sealed class ServerModCatalog
    {
        private readonly List<ServerModEntry> _entries = new List<ServerModEntry>();
        private readonly Dictionary<string, ModDescriptor> _byId =
            new Dictionary<string, ModDescriptor>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ServerModEntry> Entries => _entries;
        public bool HasAny => _entries.Count > 0;

        public void Rebuild(IEnumerable<ModDescriptor> descriptors)
        {
            _entries.Clear();
            _byId.Clear();
            if (descriptors == null)
                return;

            foreach (var d in descriptors)
            {
                if (d == null || d.FailReason != null)
                    continue;
                // Plugins intentionally stay out of the handshake catalog so pure vanilla
                // clients never need TIMF when the host only runs Plugin-side balance mods.
                if (!d.ParticipatesInHandshake)
                    continue;
                if (string.IsNullOrEmpty(d.Id))
                    continue;

                var e = new ServerModEntry(d.Id, d.Version ?? "0.0.0", d.RequiredOnJoin);
                _entries.Add(e);
                _byId[d.Id] = d;
            }

            _entries.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        }

        public bool TryGetDescriptor(string id, out ModDescriptor d)
        {
            return _byId.TryGetValue(id ?? "", out d);
        }

        public List<ServerModEntry> Snapshot()
        {
            return _entries.Select(e => new ServerModEntry(e.Id, e.Version, e.RequiredOnJoin)).ToList();
        }

        /// <summary>
        /// Host-dictates match: enable host entries that exist locally with VersionOk(local >= host).
        /// Returns missing required host ids for kick decisions.
        /// </summary>
        public static List<ServerModEntry> IntersectWithHost(
            IList<ServerModEntry> hostList,
            ServerModCatalog local,
            out List<string> missingRequired)
        {
            var enabled = new List<ServerModEntry>();
            missingRequired = new List<string>();
            if (hostList == null || local == null)
                return enabled;

            foreach (var h in hostList)
            {
                if (h == null || string.IsNullOrEmpty(h.Id))
                    continue;

                ModDescriptor localDesc;
                if (!local.TryGetDescriptor(h.Id, out localDesc))
                {
                    if (h.RequiredOnJoin)
                        missingRequired.Add(h.Id);
                    continue;
                }

                if (!ModLoader.VersionOk(localDesc.Version, h.Version))
                {
                    if (h.RequiredOnJoin)
                        missingRequired.Add(h.Id + " (need >=" + h.Version + ", have " + localDesc.Version + ")");
                    continue;
                }

                enabled.Add(new ServerModEntry(localDesc.Id, localDesc.Version, h.RequiredOnJoin));
            }

            return enabled;
        }
    }
}
