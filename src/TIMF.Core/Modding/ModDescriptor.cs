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
        public TimfSide Side { get; set; } = TimfSide.Client;

        /// <summary>Protocol axis — orthogonal to <see cref="Side"/>. See <see cref="TimfNetProfile"/>.</summary>
        public TimfNetProfile NetProfile { get; set; } = TimfNetProfile.Vanilla;

        public TimfSide InferredSide { get; set; } = TimfSide.Client;
        public bool SideWasExplicit { get; set; }
        public bool HasClientCapability { get; set; }
        public bool HasAuthorityCapability { get; set; }
        public List<ModDep> Deps { get; } = new List<ModDep>();
        public string FailReason { get; set; }
        public IMod Instance { get; set; }
        public bool Loaded { get; set; }
        public bool UserEnabled { get; set; } = true;
        public bool SessionAllowed { get; set; } = true;
        public string SessionLockReason { get; set; }

        /// <summary>
        /// True when the descriptor is classified for pre-world preparation (declared
        /// LoadBeforeWorld, a content mod, or promoted because a pre-world mod hard-depends on
        /// it). False = world-staged: loads on world enter and unloads on returning to the main
        /// menu. Authority-only mods still wait for authority activation even when this flag is
        /// true.
        /// </summary>
        public bool PreWorld { get; set; }

        /// <summary>
        /// Set by <see cref="ModWatchdog"/> when the mod faults repeatedly inside framework-dispatched
        /// callbacks. A runtime-disabled mod stays resident but receives no further callbacks (see
        /// <c>ModLoader.IsExecutionAllowed</c>), the same fail-inert model the session gate uses.
        /// </summary>
        public bool RuntimeDisabled { get; set; }
        public string RuntimeDisableReason { get; set; }
        public bool ServerActive { get; set; }
        public IModContext Context { get; set; }

        public bool ParticipatesInServer
        {
            get { return TimfSides.IsAuthorityCapable(Side); }
        }

        public bool ParticipatesInClient
        {
            get { return TimfSides.IsClientCapable(Side); }
        }

        public bool ParticipatesInHandshake
        {
            get { return TimfNetProfiles.ParticipatesInHandshake(NetProfile); }
        }

        /// <summary>Host rejects peers lacking this mod. Only ever true for handshake profiles.</summary>
        public bool RequiredOnJoin
        {
            get { return TimfNetProfiles.RequiresPeer(NetProfile); }
        }

        public bool IsDeferredServerAuthority
        {
            get { return TimfSides.IsDeferredAuthority(Side); }
        }

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

            // Do not instantiate mod code during discovery. The package audit must complete
            // before any mod constructor or static initializer can execute.
            d.Id = ReadConstantStringProperty(entryType, "Name") ?? entryType.Name;
            d.Version = ReadConstantStringProperty(entryType, "Version")
                        ?? asm.GetName().Version?.ToString() ?? "0.0.0";

            var attr = (TimfModAttribute)Attribute.GetCustomAttribute(entryType, typeof(TimfModAttribute));
            if (attr != null)
            {
                if (!string.IsNullOrWhiteSpace(attr.Id))
                    d.Id = attr.Id.Trim();
                AddCsv(d, attr.Dependencies, soft: false);
                AddCsv(d, attr.LoadAfter, soft: true);
                d.PreWorld = attr.LoadBeforeWorld;
            }

            // Content ids must be allocated for the whole set before any world data references
            // them, so content mods cannot be world-staged regardless of their declaration.
            if (!d.PreWorld && typeof(TIMF.Content.IContentMod).IsAssignableFrom(entryType))
                d.PreWorld = true;

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

            // Side classification (interfaces + optional explicit Side).
            if (d.FailReason == null)
            {
                var classified = SideClassifier.Classify(entryType, attr);
                d.InferredSide = classified.InferredSide;
                d.SideWasExplicit = classified.UsedExplicitSide;
                d.HasClientCapability = classified.HasClientCapability;
                d.HasAuthorityCapability = classified.HasAuthorityCapability;
                d.Side = classified.Side;
                d.NetProfile = classified.NetProfile;
                if (!string.IsNullOrEmpty(classified.FailReason))
                    d.FailReason = classified.FailReason;
            }

            return d;
        }

        private static string ReadConstantStringProperty(Type type, string propertyName)
        {
            try
            {
                var getter = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                    ?.GetGetMethod(false);
                var il = getter?.GetMethodBody()?.GetILAsByteArray();
                // Release C# expression-bodied/constant getter: ldstr <token>; ret.
                if (il == null || il.Length != 6 || il[0] != 0x72 || il[5] != 0x2a)
                    return null;
                return getter.Module.ResolveString(BitConverter.ToInt32(il, 1));
            }
            catch { return null; }
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
