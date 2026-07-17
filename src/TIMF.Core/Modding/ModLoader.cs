using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModLoader
    {
        private readonly ILogger _log;
        private readonly string _home;
        private readonly string _modsDir;
        private readonly string _configDir;
        private readonly ServiceRegistry _services = new ServiceRegistry();
        private readonly List<IMod> _mods = new List<IMod>();
        private readonly List<ModDescriptor> _descriptors = new List<ModDescriptor>();

        public IReadOnlyList<IMod> Mods => _mods;
        public IServiceRegistry Services => _services;
        public IReadOnlyList<ModDescriptor> Descriptors => _descriptors;

        public ModLoader(ILogger log, string home)
        {
            _log = log;
            _home = home;
            _modsDir = Path.Combine(home, "Mods");
            _configDir = Path.Combine(home, "config");
            Directory.CreateDirectory(_modsDir);
            Directory.CreateDirectory(_configDir);
        }

        public void LoadAll()
        {
            // Preferred layout: Mods/<ModId>/<ModId>.dll (+ assets). Legacy flat DLLs in
            // Mods/ root are still discovered for backward compatibility.
            var files = CollectModDlls();

            _log.Info("Scanning mods in " + _modsDir + " (" + files.Count + " dll candidates)");

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (IsFrameworkFile(name))
                {
                    _log.Debug("Skipping framework assembly: " + name);
                    continue;
                }

                try
                {
                    DiscoverOne(file);
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to discover mod " + name, ex);
                }
            }

            var byId = new Dictionary<string, ModDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _descriptors)
            {
                if (string.IsNullOrEmpty(d.Id))
                    continue;
                if (byId.ContainsKey(d.Id))
                {
                    d.FailReason = "Duplicate mod id '" + d.Id + "' (also " + Path.GetFileName(byId[d.Id].Path) + ")";
                    _log.Error(d.FailReason);
                    continue;
                }

                byId[d.Id] = d;
            }

            foreach (var d in _descriptors)
            {
                if (d.FailReason != null)
                    continue;

                foreach (var dep in d.Deps)
                {
                    if (dep.Soft)
                        continue;

                    ModDescriptor target;
                    if (!byId.TryGetValue(dep.Id, out target))
                    {
                        d.FailReason = "Missing dependency: " + dep.Id;
                        _log.Error("Mod '" + d.Id + "' " + d.FailReason + " (from " + Path.GetFileName(d.Path) + ")");
                        break;
                    }

                    if (target.FailReason != null)
                    {
                        d.FailReason = "Dependency failed: " + dep.Id + " (" + target.FailReason + ")";
                        _log.Error("Mod '" + d.Id + "' " + d.FailReason);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(dep.MinVersion) && !VersionOk(target.Version, dep.MinVersion))
                    {
                        d.FailReason = "Dependency " + dep.Id + " version " + target.Version + " < required " + dep.MinVersion;
                        _log.Error("Mod '" + d.Id + "' " + d.FailReason);
                        break;
                    }
                }
            }

            bool changed;
            do
            {
                changed = false;
                foreach (var d in _descriptors)
                {
                    if (d.FailReason != null)
                        continue;
                    foreach (var dep in d.Deps)
                    {
                        if (dep.Soft)
                            continue;
                        ModDescriptor target;
                        if (byId.TryGetValue(dep.Id, out target) && target.FailReason != null)
                        {
                            d.FailReason = "Dependency failed: " + dep.Id;
                            _log.Error("Mod '" + d.Id + "' blocked because dependency '" + dep.Id + "' failed");
                            changed = true;
                            break;
                        }
                    }
                }
            } while (changed);

            List<ModDescriptor> order;
            string cycle;
            if (!TryTopoSort(_descriptors.Where(d => d.FailReason == null).ToList(), out order, out cycle))
            {
                _log.Error("Dependency cycle detected: " + cycle);
                foreach (var id in cycle.Split(new[] { " -> " }, StringSplitOptions.None))
                {
                    ModDescriptor d;
                    if (byId.TryGetValue(id.Trim(), out d) && d.FailReason == null)
                        d.FailReason = "Dependency cycle: " + cycle;
                }

                order = _descriptors.Where(d => d.FailReason == null).ToList();
            }

            _log.Info("Load order (" + order.Count + "): " + string.Join(" -> ", order.Select(d => d.Id)));

            foreach (var d in order)
            {
                if (d.FailReason != null)
                    continue;
                try
                {
                    LoadOne(d);
                }
                catch (Exception ex)
                {
                    d.FailReason = "Load threw: " + ex.Message;
                    _log.Error("Failed to load mod " + d.Id, ex);
                }
            }

            var failed = _descriptors.Where(x => x.FailReason != null).ToList();
            if (failed.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Mod load report: " + failed.Count + " failed ===");
                foreach (var f in failed)
                    sb.AppendLine("  - " + f.Id + " (" + Path.GetFileName(f.Path) + "): " + f.FailReason);
                _log.Warn(sb.ToString().TrimEnd());
            }

            // Publish the loaded-mod registry for hubs / settings UIs.
            var registry = new ModRegistry();
            foreach (var d in order)
            {
                if (d.Loaded && d.Instance != null)
                    registry.Add(new ModInfo(d.Id, d.Instance.Name, d.Version, d.Instance));
            }

            _services.Register<IModRegistry>(registry);
            _log.Info("Registered IModRegistry with " + registry.Mods.Count + " mod(s)");

            _log.Info("Loaded " + _mods.Count + " mod(s); " + failed.Count + " failed/skipped");
        }

        /// <summary>
        /// Collect candidate mod DLLs. Each subfolder of Mods/ is treated as one mod package:
        /// only the DLL matching the folder name (or the single DLL present) is probed as an entry,
        /// so bundled dependency DLLs don't get scanned as mods. Legacy: loose DLLs in Mods/ root.
        /// </summary>
        private List<string> CollectModDlls()
        {
            var result = new List<string>();

            // Legacy flat layout: DLLs directly in Mods/
            foreach (var f in Directory.GetFiles(_modsDir, "*.dll", SearchOption.TopDirectoryOnly))
                result.Add(f);

            // Preferred layout: one folder per mod.
            foreach (var dir in Directory.GetDirectories(_modsDir))
            {
                var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
                if (dlls.Length == 0)
                    continue;

                var folderName = Path.GetFileName(dir);
                // Entry candidate: DLL named like the folder; else if exactly one DLL, use it;
                // else probe all (FindModType will ignore non-IMod assemblies).
                var match = dlls.FirstOrDefault(d =>
                    string.Equals(Path.GetFileNameWithoutExtension(d), folderName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    result.Add(match);
                else if (dlls.Length == 1)
                    result.Add(dlls[0]);
                else
                    result.AddRange(dlls);
            }

            return result
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsFrameworkFile(string name)
        {
            return name.StartsWith("TIMF.Core", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("TIMF.Abstractions", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("0Harmony.dll", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("TIMF.Bootstrap.dll", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("TIMF.Launcher.exe", StringComparison.OrdinalIgnoreCase);
        }

        private void DiscoverOne(string path)
        {
            var asm = Assembly.LoadFrom(path);
            var entryType = FindModType(asm);
            if (entryType == null)
            {
                _log.Warn("No IMod entry type in " + Path.GetFileName(path));
                return;
            }

            var d = ModDescriptor.FromType(path, asm, entryType);
            _descriptors.Add(d);

            var hard = string.Join(", ", d.HardDepIds);
            var soft = string.Join(", ", d.SoftAfterIds);
            _log.Info("Discovered mod id=" + d.Id + " v" + d.Version
                      + " deps=[" + hard + "]"
                      + " after=[" + soft + "]"
                      + " file=" + Path.GetFileName(path));
        }

        private void LoadOne(ModDescriptor d)
        {
            var mod = (IMod)Activator.CreateInstance(d.EntryType);
            d.Instance = mod;

            var idAttr = (TimfModAttribute)Attribute.GetCustomAttribute(d.EntryType, typeof(TimfModAttribute));
            if (idAttr == null || string.IsNullOrWhiteSpace(idAttr.Id))
            {
                if (!string.IsNullOrWhiteSpace(mod.Name))
                    d.Id = mod.Name;
            }

            d.Version = mod.Version ?? d.Version;

            var modDir = Path.GetDirectoryName(d.Path) ?? _modsDir;
            var modLog = new Logging.FileLogger(
                Path.Combine(_home, "logs", "mod-" + Sanitize(d.Id) + ".log"),
                d.Id);
            var ctx = new ModContext(modLog, _home, _configDir, modDir, d.Path, _services);

            _log.Info("Loading mod " + d.Id + " v" + d.Version + " from " + Path.GetFileName(d.Path));
            mod.Load(ctx);
            d.Loaded = true;
            _mods.Add(mod);
            _log.Info("Mod ready: " + d.Id);
        }

        internal static bool TryTopoSort(
            List<ModDescriptor> candidates,
            out List<ModDescriptor> ordered,
            out string cycleDescription)
        {
            ordered = new List<ModDescriptor>();
            cycleDescription = null;

            var byId = candidates.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
            var indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var d in candidates)
            {
                indegree[d.Id] = 0;
                edges[d.Id] = new List<string>();
            }

            Action<string, string> addEdge = (from, to) =>
            {
                if (!byId.ContainsKey(from) || !byId.ContainsKey(to))
                    return;
                if (edges[from].Contains(to))
                    return;
                edges[from].Add(to);
                indegree[to] = indegree[to] + 1;
            };

            foreach (var d in candidates)
            {
                foreach (var dep in d.Deps)
                    addEdge(dep.Id, d.Id);
            }

            // Deterministic: SortedSet by id
            var ready = new List<string>();
            foreach (var kv in indegree)
            {
                if (kv.Value == 0)
                    ready.Add(kv.Key);
            }

            ready.Sort(StringComparer.OrdinalIgnoreCase);

            while (ready.Count > 0)
            {
                var id = ready[0];
                ready.RemoveAt(0);
                ordered.Add(byId[id]);
                foreach (var to in edges[id])
                {
                    indegree[to] = indegree[to] - 1;
                    if (indegree[to] == 0)
                    {
                        ready.Add(to);
                        ready.Sort(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            if (ordered.Count != candidates.Count)
            {
                var left = indegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(x => x).ToList();
                cycleDescription = string.Join(" -> ", left);
                return false;
            }

            return true;
        }

        private static bool VersionOk(string actual, string minRequired)
        {
            Version a, b;
            if (Version.TryParse(NormalizeVer(actual), out a) && Version.TryParse(NormalizeVer(minRequired), out b))
                return a >= b;
            return string.Compare(actual ?? "", minRequired ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeVer(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return "0.0.0";
            var parts = v.Trim().Split('.');
            if (parts.Length == 1) return parts[0] + ".0.0";
            if (parts.Length == 2) return parts[0] + "." + parts[1] + ".0";
            return v.Trim();
        }

        private static Type FindModType(Assembly asm)
        {
            var types = SafeGetTypes(asm);
            var marked = types.FirstOrDefault(t =>
                typeof(IMod).IsAssignableFrom(t)
                && !t.IsAbstract
                && t.IsClass
                && t.GetCustomAttributes(typeof(TimfModAttribute), false).Length > 0);
            if (marked != null)
                return marked;

            return types.FirstOrDefault(t =>
                typeof(IMod).IsAssignableFrom(t)
                && !t.IsAbstract
                && t.IsClass
                && t.GetConstructor(Type.EmptyTypes) != null);
        }

        private static Type[] SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToArray();
            }
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public void UnloadAll()
        {
            for (var i = _mods.Count - 1; i >= 0; i--)
            {
                try
                {
                    _mods[i].Unload();
                }
                catch (Exception ex)
                {
                    _log.Error("Unload failed for " + _mods[i].Name, ex);
                }
            }

            _mods.Clear();
        }
    }
}
