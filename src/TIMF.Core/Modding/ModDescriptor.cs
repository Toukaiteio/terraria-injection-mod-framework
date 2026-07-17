using System;
using System.Collections.Generic;
using System.Reflection;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModDep
    {
        public string Id;
        public string MinVersion;
        public bool Soft;
    }

    /// <summary>Discovered mod before instantiation / dependency resolution.</summary>
    internal sealed class ModDescriptor
    {
        public string Path { get; set; }
        public Assembly Assembly { get; set; }
        public Type EntryType { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public List<ModDep> Deps { get; } = new List<ModDep>();
        public string FailReason { get; set; }
        public IMod Instance { get; set; }
        public bool Loaded { get; set; }

        public IEnumerable<string> HardDepIds
        {
            get
            {
                foreach (var d in Deps)
                {
                    if (!d.Soft)
                        yield return d.Id;
                }
            }
        }

        public IEnumerable<string> SoftAfterIds
        {
            get
            {
                foreach (var d in Deps)
                {
                    if (d.Soft)
                        yield return d.Id;
                }
            }
        }

        public static ModDescriptor FromType(string path, Assembly asm, Type entryType)
        {
            var d = new ModDescriptor
            {
                Path = path,
                Assembly = asm,
                EntryType = entryType,
            };

            try
            {
                var probe = (IMod)Activator.CreateInstance(entryType);
                d.Id = probe.Name ?? entryType.Name;
                d.Version = probe.Version ?? "0.0.0";
            }
            catch (Exception ex)
            {
                d.Id = entryType.Name;
                d.Version = "0.0.0";
                d.FailReason = "Failed to probe IMod: " + ex.Message;
            }

            var attr = (TimfModAttribute)Attribute.GetCustomAttribute(entryType, typeof(TimfModAttribute));
            if (attr != null)
            {
                if (!string.IsNullOrWhiteSpace(attr.Id))
                    d.Id = attr.Id.Trim();
                AddCsv(d, attr.Dependencies, soft: false);
                AddCsv(d, attr.LoadAfter, soft: true);
            }

            foreach (TimfDependsOnAttribute dep in entryType.GetCustomAttributes(typeof(TimfDependsOnAttribute), false))
            {
                if (string.IsNullOrWhiteSpace(dep.ModId))
                    continue;
                AddDep(d, dep.ModId.Trim(), dep.MinVersion, soft: false);
            }

            foreach (TimfLoadAfterAttribute after in entryType.GetCustomAttributes(typeof(TimfLoadAfterAttribute), false))
            {
                if (string.IsNullOrWhiteSpace(after.ModId))
                    continue;
                AddDep(d, after.ModId.Trim(), null, soft: true);
            }

            return d;
        }

        private static void AddCsv(ModDescriptor d, string csv, bool soft)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return;
            foreach (var part in csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                AddDep(d, part.Trim(), null, soft);
        }

        private static void AddDep(ModDescriptor d, string id, string minVersion, bool soft)
        {
            if (string.IsNullOrEmpty(id))
                return;
            foreach (var existing in d.Deps)
            {
                if (string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase) && existing.Soft == soft)
                {
                    if (!soft && string.IsNullOrEmpty(existing.MinVersion) && !string.IsNullOrEmpty(minVersion))
                        existing.MinVersion = minVersion;
                    return;
                }
            }

            d.Deps.Add(new ModDep { Id = id, MinVersion = minVersion, Soft = soft });
        }
    }
}
