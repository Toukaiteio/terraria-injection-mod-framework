using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TIMF.Abstractions;
using TIMF.Core.Localization;
using TIMF.Core.Session;

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
        private readonly ServerModCatalog _serverCatalog = new ServerModCatalog();
        private readonly HashSet<string> _activeServerIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ModEnablementStore _enablement;
        private readonly AuthorityServices _authorityServices = new AuthorityServices();
        private IClientServices _clientServices;
        private LanguageService _language;
        private bool _isDedicated;
        private bool _serverWarningShown;

        public IReadOnlyList<IMod> Mods => _mods;
        public IServiceRegistry Services => _services;
        public IReadOnlyList<ModDescriptor> Descriptors => _descriptors;
        public LanguageService Language => _language;
        public ServerModCatalog ServerCatalog => _serverCatalog;
        public IAuthorityServices AuthorityServices => _authorityServices;
        public IClientServices ClientServices => _clientServices;

        /// <summary>True when any handshake-visible Server/Both mod is enabled (arms TIMF net protocol).</summary>
        public bool HasLocalServerSideMods => _serverCatalog.HasAny;

        /// <summary>True when any Server/Both/Plugin is enabled (may need host authority activation).</summary>
        public bool HasLocalServerAuthorityMods
        {
            get
            {
                foreach (var d in _descriptors)
                {
                    if (d == null || d.FailReason != null || !d.UserEnabled)
                        continue;
                    if (d.ParticipatesInServer)
                        return true;
                }
                return false;
            }
        }

        public ModLoader(ILogger log, string home)
        {
            _log = log;
            _home = home;
            _modsDir = Path.Combine(home, "Mods");
            _configDir = Path.Combine(home, "config");
            Directory.CreateDirectory(_modsDir);
            Directory.CreateDirectory(_configDir);

            _enablement = new ModEnablementStore(_log, _configDir);
            _language = new LanguageService(_log);
            _services.Register<ILanguageService>(_language);
            _services.Register<IAuthorityServices>(_authorityServices);
        }

        /// <summary>
        /// Called from GameHooks after client registries exist.
        /// Pass null on dedicated server so mods see <see cref="IModContext.Client"/> == null.
        /// </summary>
        public void SetClientServices(IClientServices client)
        {
            _clientServices = client;
            if (client != null)
                _services.Register<IClientServices>(client);
        }

        public void LoadAll()
        {
            try { _isDedicated = Terraria.Main.dedServ; }
            catch { _isDedicated = false; }

            var files = CollectModDlls();
            _log.Info("Scanning mods in " + _modsDir + " (" + files.Count + " dll candidates)"
                      + (_isDedicated ? " [dedicated server]" : ""));

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (IsFrameworkFile(name))
                {
                    _log.Debug("Skipping framework assembly: " + name);
                    continue;
                }

                try { DiscoverOne(file); }
                catch (Exception ex) { _log.Error("Failed to discover mod " + name, ex); }
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

            _log.Info("Discovery order (" + order.Count + "): " + string.Join(" -> ", order.Select(FormatSide)));

            foreach (var d in order)
            {
                if (d.FailReason != null)
                    continue;

                d.UserEnabled = _enablement.IsEnabled(d.Id);
                if (!d.UserEnabled)
                {
                    _log.Info("User-disabled mod (skipped): " + d.Id + " [" + d.Side + "]");
                    continue;
                }

                if (d.IsDeferredServerAuthority)
                {
                    _log.Info("Deferred " + d.Side + " mod: " + d.Id + " v" + d.Version
                              + (d.Side == TimfSide.Plugin ? " (vanilla-compatible plugin)" : ""));
                    continue;
                }

                if (_isDedicated && d.Side == TimfSide.Client)
                {
                    _log.Info("Skipping client-only mod on dedicated server: " + d.Id);
                    continue;
                }

                try { LoadOne(d); }
                catch (Exception ex)
                {
                    d.FailReason = "Load threw: " + ex.Message;
                    _log.Error("Failed to load mod " + d.Id, ex);
                }
            }

            // Handshake catalog: enabled Server/Both only (Plugin excluded — vanilla-safe).
            _serverCatalog.Rebuild(_descriptors.Where(d => d.UserEnabled));

            var failed = _descriptors.Where(x => x.FailReason != null).ToList();
            if (failed.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Mod load report: " + failed.Count + " failed ===");
                foreach (var f in failed)
                    sb.AppendLine("  - " + f.Id + " (" + Path.GetFileName(f.Path) + "): " + f.FailReason);
                _log.Warn(sb.ToString().TrimEnd());
            }

            RebuildRegistry();

            _log.Info("Loaded " + _mods.Count + " client-path mod(s); "
                      + _serverCatalog.Entries.Count + " server-capable; "
                      + failed.Count + " failed/skipped");
            if (_serverCatalog.HasAny)
                _log.Info("Local server-side mods: " + string.Join(", ", _serverCatalog.Entries.Select(e => e.Id)));
            else
                _log.Info("No local server-side mods — TIMF handshake protocol will stay disabled");
        }

        public void ActivateServerMods(IEnumerable<string> ids)
        {
            if (ids == null)
                return;

            var idList = ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (idList.Count == 0)
                return;

            var byId = _descriptors
                .Where(d => d.FailReason == null && !string.IsNullOrEmpty(d.Id))
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var wanted = new List<ModDescriptor>();
            foreach (var id in idList)
            {
                ModDescriptor d;
                if (!byId.TryGetValue(id, out d))
                {
                    _log.Warn("ActivateServerMods: unknown id " + id);
                    continue;
                }
                if (!d.ParticipatesInServer)
                {
                    _log.Warn("ActivateServerMods: " + id + " is not Server/Both/Plugin");
                    continue;
                }
                wanted.Add(d);
            }

            List<ModDescriptor> order;
            string cycle;
            if (!TryTopoSort(wanted, out order, out cycle))
            {
                _log.Error("ActivateServerMods: dependency cycle in enable set: " + cycle);
                order = wanted;
            }

            var newlyActivated = 0;
            foreach (var d in order)
            {
                if (_activeServerIds.Contains(d.Id))
                    continue;

                if (!d.UserEnabled)
                {
                    _log.Info("ActivateServerMods: skip user-disabled " + d.Id);
                    continue;
                }

                try
                {
                    if (d.IsDeferredServerAuthority)
                    {
                        // Server + Plugin: Load on activate, unload on deactivate.
                        if (!d.Loaded)
                            LoadOne(d);
                        d.ServerActive = true;
                        _activeServerIds.Add(d.Id);
                        newlyActivated++;

                        var sm = d.Instance as IServerMod;
                        if (sm != null && d.Context != null)
                        {
                            try { sm.OnServerActivate(d.Context); }
                            catch (Exception ex) { _log.Error("OnServerActivate failed for " + d.Id, ex); }
                        }
                        _log.Info((d.Side == TimfSide.Plugin ? "Plugin" : "Server mod")
                                  + " activated (Load): " + d.Id);
                    }
                    else if (d.Side == TimfSide.Both)
                    {
                        if (!d.Loaded)
                            LoadOne(d);

                        d.ServerActive = true;
                        _activeServerIds.Add(d.Id);
                        newlyActivated++;

                        var sm = d.Instance as IServerMod;
                        if (sm != null && d.Context != null)
                        {
                            try { sm.OnServerActivate(d.Context); }
                            catch (Exception ex) { _log.Error("OnServerActivate failed for " + d.Id, ex); }
                        }
                        else
                        {
                            _log.Debug("Both-side mod " + d.Id + " has no IServerMod; marked server-active only");
                        }
                        _log.Info("Server path activated (Both): " + d.Id);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to activate server mod " + d.Id, ex);
                }
            }

            if (newlyActivated > 0)
                NotifyServerSideModsActive();

            RebuildRegistry();
        }

        public void ActivateAllLocalServerMods()
        {
            // Host/SP/dedicated: activate every enabled Server + Both + Plugin.
            // Plugins are not in the handshake catalog but still need host authority.
            var ids = _descriptors
                .Where(d => d != null
                            && d.FailReason == null
                            && d.UserEnabled
                            && d.ParticipatesInServer
                            && !string.IsNullOrEmpty(d.Id))
                .Select(d => d.Id)
                .ToList();
            ActivateServerMods(ids);
        }

        public void DeactivateAllServerMods()
        {
            if (_activeServerIds.Count == 0)
                return;

            var active = _descriptors.Where(d => d.ServerActive).Reverse().ToList();
            foreach (var d in active)
            {
                try
                {
                    DeactivateServerPath(d);
                }
                catch (Exception ex)
                {
                    _log.Error("Deactivate failed for " + d.Id, ex);
                }
            }

            _activeServerIds.Clear();
            _serverWarningShown = false;
            RebuildRegistry();
        }

        public IReadOnlyList<ITimfRemoteModInfo> GetActiveServerModInfos()
        {
            var list = new List<ITimfRemoteModInfo>();
            foreach (var d in _descriptors)
            {
                if (d.ServerActive)
                    list.Add(new ServerModEntry(d.Id, d.Version, d.RequiredOnJoin));
            }
            return list;
        }

        /// <summary>
        /// Enable/disable a mod at runtime. Persists preference; may Load/Unload immediately.
        /// Framework mods (TIMF.UI) cannot be disabled.
        /// </summary>
        public bool TrySetModEnabled(string id, bool enabled, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                message = "Empty mod id.";
                return false;
            }

            var d = _descriptors.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (d == null)
            {
                message = "Mod not found: " + id;
                return false;
            }

            if (IsFrameworkProtected(d.Id))
            {
                message = d.Id + " is a framework library mod and cannot be disabled.";
                return false;
            }

            if (d.UserEnabled == enabled)
            {
                message = d.Id + " is already " + (enabled ? "enabled" : "disabled") + ".";
                return true;
            }

            if (!enabled)
            {
                // Disabling: tear down server path then unload if loaded.
                try
                {
                    if (d.ServerActive)
                        DeactivateServerPath(d);
                    if (d.Loaded && d.Instance != null)
                        UnloadOne(d);
                }
                catch (Exception ex)
                {
                    _log.Error("Disable failed for " + d.Id, ex);
                    message = "Failed to disable " + d.Id + ": " + ex.Message;
                    return false;
                }

                d.UserEnabled = false;
                _enablement.SetEnabled(d.Id, false);
                _serverCatalog.Rebuild(_descriptors.Where(x => x.UserEnabled));
                RebuildRegistry();
                message = d.Id + " disabled. It will stay off until re-enabled (restart not required for client/server path).";
                _log.Info(message);
                return true;
            }

            // Enabling
            d.UserEnabled = true;
            _enablement.SetEnabled(d.Id, true);
            _serverCatalog.Rebuild(_descriptors.Where(x => x.UserEnabled));

            try
            {
                if (!d.IsDeferredServerAuthority)
                {
                    if (_isDedicated && d.Side == TimfSide.Client)
                    {
                        message = d.Id + " enabled, but client-only mods do not load on dedicated servers.";
                    }
                    else if (!d.Loaded)
                    {
                        LoadOne(d);
                        message = d.Id + " enabled and loaded.";
                    }
                    else
                    {
                        message = d.Id + " enabled.";
                    }
                }
                else if (d.Side == TimfSide.Plugin)
                {
                    message = d.Id + " enabled (plugin — vanilla-compatible host authority).";
                }
                else
                {
                    message = d.Id + " enabled (server-only).";
                }

                // If we are already in a host-like session, activate server path now.
                if (d.ParticipatesInServer && ShouldActivateServerNow())
                {
                    ActivateServerMods(new[] { d.Id });
                    message += " Server path activated for current session.";
                }
                else if (d.Side == TimfSide.Server)
                {
                    message += " Activates in SP / host / dedicated / TIMF handshake.";
                }
                else if (d.Side == TimfSide.Plugin)
                {
                    message += " Activates in SP / host / dedicated only (never on join clients).";
                }
            }
            catch (Exception ex)
            {
                _log.Error("Enable/load failed for " + d.Id, ex);
                message = "Enabled in config but load failed: " + ex.Message;
                RebuildRegistry();
                return false;
            }

            RebuildRegistry();
            _log.Info(message);
            return true;
        }

        private static bool ShouldActivateServerNow()
        {
            try
            {
                if (Terraria.Main.dedServ)
                    return true;
                // 0 = singleplayer, 2 = listen server / host
                return Terraria.Main.netMode == 0 || Terraria.Main.netMode == 2;
            }
            catch
            {
                return false;
            }
        }

        private void DeactivateServerPath(ModDescriptor d)
        {
            if (d == null || !d.ServerActive)
                return;

            var sm = d.Instance as IServerMod;
            if (sm != null)
            {
                try { sm.OnServerDeactivate(); }
                catch (Exception ex) { _log.Error("OnServerDeactivate failed for " + d.Id, ex); }
            }

            if (d.IsDeferredServerAuthority && d.Loaded && d.Instance != null)
                UnloadOne(d);

            d.ServerActive = false;
            _activeServerIds.Remove(d.Id);
            _log.Info((d.Side == TimfSide.Plugin ? "Plugin" : "Server mod")
                      + " deactivated: " + d.Id);
        }

        private void UnloadOne(ModDescriptor d)
        {
            if (d == null || d.Instance == null)
                return;

            try { d.Instance.Unload(); }
            catch (Exception ex) { _log.Error("Unload failed for " + d.Id, ex); }

            _mods.Remove(d.Instance);
            d.Instance = null;
            d.Loaded = false;
            d.Context = null;
            d.ServerActive = false;
            _activeServerIds.Remove(d.Id);
            _log.Info("Unloaded mod " + d.Id);
        }

        private static bool IsFrameworkProtected(string id)
        {
            return string.Equals(id, "TIMF.UI", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Chat/log warning: server-side mods break pure-vanilla multiplayer expectations.
        /// </summary>
        public void NotifyServerSideModsActive()
        {
            if (_serverWarningShown)
                return;
            if (_activeServerIds.Count == 0)
                return;

            _serverWarningShown = true;
            var names = string.Join(", ", _activeServerIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            var hasRequired = false;
            var hasPlugin = false;
            foreach (var d in _descriptors)
            {
                if (d == null || !d.ServerActive)
                    continue;
                if (d.Side == TimfSide.Plugin)
                    hasPlugin = true;
                else if (d.ParticipatesInHandshake && d.RequiredOnJoin)
                    hasRequired = true;
            }

            var msg =
                "TIMF server-authority mods are active (" + names + "). ";
            if (hasPlugin && !hasRequired)
            {
                msg += "Plugin-only host remains vanilla-join compatible (no TIMF handshake required). ";
            }
            else
            {
                msg += "Joining a pure vanilla server will not enable Server/Both mods. "
                       + "Hosting with RequiredOnJoin Server/Both mods will reject pure vanilla clients. ";
            }
            msg += "Manage enablement in Mod Settings (F9).";

            _log.Warn(msg);
            try
            {
                if (Terraria.Main.dedServ)
                    return;
                var color = new Microsoft.Xna.Framework.Color(255, 180, 80);
                try { Terraria.Main.NewTextMultiline(msg, false, color, 600); }
                catch { Terraria.Main.NewText(msg, color); }
            }
            catch (Exception ex)
            {
                _log.Debug("NotifyServerSideModsActive UI failed: " + ex.Message);
            }
        }

        private void RebuildRegistry()
        {
            var registry = new ModRegistry(this);
            foreach (var d in _descriptors)
            {
                // List successfully discovered mods (enabled or not), including deferred Server-only.
                if (string.IsNullOrEmpty(d.Id) || d.FailReason != null)
                    continue;

                var name = d.Instance != null ? d.Instance.Name : d.Id;
                var ver = d.Instance != null ? (d.Instance.Version ?? d.Version) : d.Version;
                registry.Add(new ModInfo(
                    d.Id,
                    name ?? d.Id,
                    ver ?? "0.0.0",
                    d.Side,
                    d.UserEnabled,
                    d.Loaded,
                    d.ServerActive,
                    d.Instance));
            }
            _services.Register<IModRegistry>(registry);
            _log.Debug("IModRegistry updated: " + registry.Mods.Count + " mod(s)");
        }

        private static string FormatSide(ModDescriptor d)
        {
            return d.Id + "[" + d.Side + "]";
        }

        private List<string> CollectModDlls()
        {
            var result = new List<string>();
            foreach (var f in Directory.GetFiles(_modsDir, "*.dll", SearchOption.TopDirectoryOnly))
                result.Add(f);

            foreach (var dir in Directory.GetDirectories(_modsDir))
            {
                var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
                if (dlls.Length == 0)
                    continue;

                var folderName = Path.GetFileName(dir);
                var match = dlls.FirstOrDefault(d =>
                    string.Equals(Path.GetFileNameWithoutExtension(d), folderName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    result.Add(match);
                else if (dlls.Length == 1)
                    result.Add(dlls[0]);
                else
                    result.AddRange(dlls);
            }

            return result.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
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
            var sideNote = d.SideWasExplicit
                ? "side=" + d.Side + " (explicit, inferred=" + d.InferredSide + ")"
                : "side=" + d.Side + " (inferred)";
            _log.Info("Discovered mod id=" + d.Id + " v" + d.Version
                      + " " + sideNote
                      + " requiredOnJoin=" + d.RequiredOnJoin
                      + " caps[client=" + d.HasClientCapability + ",authority=" + d.HasAuthorityCapability + "]"
                      + " deps=[" + hard + "]"
                      + " after=[" + soft + "]"
                      + " file=" + Path.GetFileName(path));
            if (d.FailReason != null)
                _log.Error("Mod '" + d.Id + "' classification failed: " + d.FailReason);
        }

        private void LoadOne(ModDescriptor d)
        {
            if (d.Loaded && d.Instance != null)
                return;

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
            var loc = new ModLocalization(modLog, modDir, _language);

            // Dedicated server: no client services. Client process: share the wired bag.
            IClientServices client = _isDedicated ? null : _clientServices;
            var ctx = new ModContext(
                modLog, _home, _configDir, modDir, d.Path, _services, loc,
                client, _authorityServices);
            d.Context = ctx;

            _log.Info("Loading mod " + d.Id + " v" + d.Version + " side=" + d.Side + " from " + Path.GetFileName(d.Path));
            mod.Load(ctx);
            d.Loaded = true;
            if (!_mods.Contains(mod))
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

        internal static bool VersionOk(string actual, string minRequired)
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
            try { return asm.GetTypes(); }
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
            DeactivateAllServerMods();
            for (var i = _mods.Count - 1; i >= 0; i--)
            {
                try { _mods[i].Unload(); }
                catch (Exception ex) { _log.Error("Unload failed for " + _mods[i].Name, ex); }
            }
            _mods.Clear();
            foreach (var d in _descriptors)
            {
                d.Loaded = false;
                d.Instance = null;
                d.Context = null;
                d.ServerActive = false;
            }
        }
    }
}