using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TIMF.Abstractions;
using TIMF.Core.Localization;
using TIMF.Core.Prefix;
using TIMF.Core.Session;
using TIMF.Core.Security;
using TIMF.Abstractions.Security;
using TIMF.Core.Weather;

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
        private readonly WeatherService _weatherService;
        private readonly PrefixService _prefixService;
        private readonly AuthorityServices _authorityServices;
        private readonly Content.ContentManager _content;
        private readonly SecurityManager _security;
        private IClientServices _clientServices;
        private LanguageService _language;
        private bool _isDedicated;
        private bool _serverWarningShown;
        private TimfSessionKind _sessionKind = TimfSessionKind.Menu;
        private List<string> _sessionAuthorityAllowList = new List<string>();
        private bool _sessionRemoteDecisionFinal = true;

        public IReadOnlyList<IMod> Mods => _mods;
        public IServiceRegistry Services => _services;
        public IReadOnlyList<ModDescriptor> Descriptors => _descriptors;
        public LanguageService Language => _language;
        public ServerModCatalog ServerCatalog => _serverCatalog;
        public IAuthorityServices AuthorityServices => _authorityServices;
        public IWeatherService Weather => _weatherService;
        public IPrefixService Prefix => _prefixService;
        public IClientServices ClientServices => _clientServices;
        internal SecurityManager Security => _security;

        /// <summary>
        /// Runtime gate used by every framework-dispatched callback. A loaded Both mod may stay
        /// resident for stable content ids while a remote server suppresses its execution.
        /// </summary>
        internal bool IsExecutionAllowed(object participant)
        {
            if (participant == null)
                return false;

            foreach (var d in _descriptors)
                if (ReferenceEquals(d.Instance, participant))
                    return d.UserEnabled && d.SessionAllowed && d.Loaded;

            var assembly = participant.GetType().Assembly;
            foreach (var d in _descriptors)
            {
                if (d.Assembly != assembly)
                    continue;
                // Helper hook objects cannot be assigned safely when one assembly contains
                // multiple entry points with different policies, so fail closed unless all
                // entries in that assembly are executable.
                if (!d.UserEnabled || !d.SessionAllowed || !d.Loaded)
                    return false;
            }
            return true;
        }

        /// <summary>Registered custom content and the id space it occupies.</summary>
        internal Content.ContentManager Content => _content;

        /// <summary>Directory holding a loaded mod's assembly, or null when it is not loaded.</summary>
        internal string ResolveModDirectory(string modId)
        {
            foreach (var d in _descriptors)
            {
                if (d != null && string.Equals(d.Id, modId, StringComparison.OrdinalIgnoreCase))
                    return System.IO.Path.GetDirectoryName(d.Path);
            }
            return null;
        }

        /// <summary>True when any handshake-profile mod is enabled (arms the TIMF net protocol).</summary>
        public bool HasLocalServerSideMods => _serverCatalog.HasAny;

        /// <summary>True when any authority-capable mod is enabled (may need host authority activation).</summary>
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
            _weatherService = new WeatherService(_log);
            _prefixService = new PrefixService();
            _authorityServices = new AuthorityServices(_weatherService, _prefixService);
            _security = new SecurityManager(_log, _configDir);
            _content = new Content.ContentManager(_log, _configDir, IsModSessionAllowed);
            _services.Register<ILanguageService>(_language);
            _services.Register<IWeatherService>(_weatherService);
            _services.Register<IPrefixService>(_prefixService);
            _services.Register<IAuthorityServices>(_authorityServices);
            _services.Register<ITerrariaReflection>(new TerrariaReflectionService());
            _services.Register<ISecurityCenter>(_security);
            _services.Register<TIMF.Content.IContentLookup>(_content);
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

        private bool IsModSessionAllowed(string modId)
        {
            var d = _descriptors.FirstOrDefault(x =>
                string.Equals(x.Id, modId, StringComparison.OrdinalIgnoreCase));
            return d != null && d.UserEnabled && d.SessionAllowed;
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

            // Surface bad version strings at discovery. Left as a warning, not a load failure:
            // a mod nobody depends on and that never joins a handshake still works fine.
            foreach (var d in _descriptors)
            {
                if (d.FailReason != null || string.IsNullOrEmpty(d.Id))
                    continue;
                if (IsParsableVersion(d.Version))
                    continue;

                _log.Warn("Mod '" + d.Id + "' reports version '" + d.Version
                          + "' which TIMF cannot compare (expected 1-4 dotted numbers, optional "
                          + "-prerelease suffix). Any MinVersion dependency on it will fail, and it "
                          + "cannot satisfy a handshake version check.");
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

                    if (!string.IsNullOrWhiteSpace(dep.MinVersion))
                    {
                        if (!IsParsableVersion(dep.MinVersion))
                        {
                            d.FailReason = "Dependency " + dep.Id + " declares MinVersion '" + dep.MinVersion
                                           + "' which is not a valid version (expected 1-4 dotted numbers, "
                                           + "optional -prerelease suffix)";
                        }
                        else if (!IsParsableVersion(target.Version))
                        {
                            d.FailReason = "Dependency " + dep.Id + " reports version '" + target.Version
                                           + "' which is not a valid version, so MinVersion "
                                           + dep.MinVersion + " cannot be verified";
                        }
                        else if (!VersionOk(target.Version, dep.MinVersion))
                        {
                            d.FailReason = "Dependency " + dep.Id + " version " + target.Version
                                           + " < required " + dep.MinVersion;
                        }

                        if (d.FailReason != null)
                        {
                            _log.Error("Mod '" + d.Id + "' " + d.FailReason);
                            break;
                        }
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
                    _log.Info("Deferred authority mod: " + d.Id + " v" + d.Version
                              + " [" + d.Side + "/" + d.NetProfile + "]");
                    continue;
                }

                if (_isDedicated && !TimfSides.IsAuthorityCapable(d.Side))
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

            // Every content mod has now declared itself, so ids can be allocated for the whole
            // set at once and the vanilla arrays grown to fit before anything indexes them.
            try { _content.FinalizeRegistration(); }
            catch (Exception ex) { _log.Error("Content finalisation failed", ex); }

            // Handshake catalog: Optional/Required profiles only (Vanilla excluded by definition).
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
                    _log.Warn("ActivateServerMods: " + id + " has no authority half");
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

                if (!d.UserEnabled || !d.SessionAllowed)
                {
                    _log.Info("ActivateServerMods: skip disabled/session-blocked " + d.Id);
                    continue;
                }

                try
                {
                    // Authority-only mods are deferred, so this is where they load (and they
                    // unload again on deactivate). Mods with a client half are already loaded
                    // and this only brings up their authority path.
                    if (!d.Loaded)
                        LoadOne(d);

                    d.ServerActive = true;
                    _activeServerIds.Add(d.Id);
                    newlyActivated++;

                    var lifecycle = d.Instance as IAuthorityLifecycle;
                    if (lifecycle != null && d.Context != null)
                    {
                        try { lifecycle.OnAuthorityActivate(d.Context); }
                        catch (Exception ex) { _log.Error("OnAuthorityActivate failed for " + d.Id, ex); }
                    }

                    _log.Info("Authority path activated: " + d.Id
                              + " [" + d.Side + "/" + d.NetProfile + "]");
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
            // Host/SP/dedicated: activate every enabled authority-capable mod, whatever its net profile.
            // Vanilla-profile mods are not in the handshake catalog but still need host authority.
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
        /// Applies the current world's execution policy without changing persisted user
        /// preferences. On join clients, authority-capable mods run only when advertised by
        /// the host; pure client mods remain local and freely switchable.
        /// </summary>
        public void ApplySessionPolicy(
            TimfSessionKind kind,
            IEnumerable<string> allowedAuthorityIds,
            bool remoteDecisionFinal)
        {
            _sessionKind = kind;
            var allowed = new HashSet<string>(
                allowedAuthorityIds ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            _sessionAuthorityAllowList = allowed.ToList();
            _sessionRemoteDecisionFinal = remoteDecisionFinal;

            foreach (var d in _descriptors)
            {
                d.SessionAllowed = true;
                d.SessionLockReason = null;
                if (kind != TimfSessionKind.MultiplayerClient || !d.ParticipatesInServer)
                    continue;

                d.SessionAllowed = allowed.Contains(d.Id);
                if (!d.SessionAllowed)
                    d.SessionLockReason = remoteDecisionFinal
                        ? "This mod is not enabled by the current server."
                        : "Waiting for the server mod handshake.";
            }

            // A client-only companion must not execute when one of its hard dependencies was
            // suppressed by the server. Resolve transitively and retain the first clear reason.
            bool changed;
            do
            {
                changed = false;
                foreach (var d in _descriptors)
                {
                    if (!d.SessionAllowed)
                        continue;
                    foreach (var dep in d.Deps)
                    {
                        if (dep.Soft)
                            continue;
                        var target = _descriptors.FirstOrDefault(x =>
                            string.Equals(x.Id, dep.Id, StringComparison.OrdinalIgnoreCase));
                        if (target == null || (target.UserEnabled && target.SessionAllowed))
                            continue;
                        d.SessionAllowed = false;
                        d.SessionLockReason = "Dependency '" + dep.Id
                                              + "' is unavailable in the current session.";
                        changed = true;
                        break;
                    }
                }
            } while (changed);

            RebuildRegistry();
            var blocked = _descriptors.Count(d => d.UserEnabled && !d.SessionAllowed);
            _log.Info("Session mod policy: kind=" + kind + ", authority allow-list="
                      + allowed.Count + ", session-blocked=" + blocked
                      + (remoteDecisionFinal ? " [final]" : " [pending]"));
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

            if (_sessionKind != TimfSessionKind.Menu && d.ParticipatesInServer)
            {
                message = d.Id + " cannot be enabled or disabled while a world/session is active. "
                          + "Return to the main menu first.";
                _log.Warn("Enablement change rejected: " + message);
                return false;
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
                ReapplySessionPolicy();
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
                    if (_isDedicated && !TimfSides.IsAuthorityCapable(d.Side))
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
                else
                {
                    message = d.Id + " enabled (authority-only — loads when the session grants authority).";
                }

                // If we are already in a host-like session, activate server path now.
                if (d.ParticipatesInServer && ShouldActivateServerNow())
                {
                    ActivateServerMods(new[] { d.Id });
                    message += " Authority path activated for current session.";
                }
                else if (d.ParticipatesInHandshake)
                {
                    message += " Activates in SP / host / dedicated / TIMF handshake.";
                }
                else if (TimfSides.IsAuthorityCapable(d.Side))
                {
                    message += " Activates in SP / host / dedicated only (never on join clients).";
                }
            }
            catch (Exception ex)
            {
                _log.Error("Enable/load failed for " + d.Id, ex);
                message = "Enabled in config but load failed: " + ex.Message;
                ReapplySessionPolicy();
                return false;
            }

            ReapplySessionPolicy();
            _log.Info(message);
            return true;
        }

        private void ReapplySessionPolicy()
        {
            ApplySessionPolicy(
                _sessionKind,
                _sessionAuthorityAllowList.ToArray(),
                _sessionRemoteDecisionFinal);
        }

        private static bool ShouldActivateServerNow()
        {
            try
            {
                if (Terraria.Main.dedServ)
                    return true;
                if (Terraria.Main.gameMenu)
                    return false;
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

            var lifecycle = d.Instance as IAuthorityLifecycle;
            if (lifecycle != null)
            {
                try { lifecycle.OnAuthorityDeactivate(); }
                catch (Exception ex) { _log.Error("OnAuthorityDeactivate failed for " + d.Id, ex); }
            }

            if (d.IsDeferredServerAuthority && d.Loaded && d.Instance != null)
                UnloadOne(d);

            d.ServerActive = false;
            _activeServerIds.Remove(d.Id);
            _log.Info("Authority path deactivated: " + d.Id);
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
                if (TimfNetProfiles.IsVanillaHostCompatible(d.NetProfile))
                    hasPlugin = true;
                else if (d.RequiredOnJoin)
                    hasRequired = true;
            }

            var msg =
                "TIMF server-authority mods are active (" + names + "). ";
            if (hasPlugin && !hasRequired)
            {
                msg += "All active authority mods are vanilla-profile; the host stays joinable by pure vanilla clients. ";
            }
            else
            {
                msg += "Joining a pure vanilla server will not enable handshake-profile mods. "
                       + "Hosting with Net=Required mods will reject pure vanilla clients. ";
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
                    d.NetProfile,
                    d.UserEnabled,
                    d.SessionAllowed,
                    CanChangeEnabled(d),
                    InteractionLockReason(d),
                    d.Loaded,
                    d.ServerActive,
                    typeof(IModSettings).IsAssignableFrom(d.EntryType),
                    d.UserEnabled && d.SessionAllowed && d.Loaded
                        && d.Instance is IModSettings,
                    d.Instance));
            }
            _services.Register<IModRegistry>(registry);
            _log.Debug("IModRegistry updated: " + registry.Mods.Count + " mod(s)");
        }

        private bool CanChangeEnabled(ModDescriptor d)
        {
            return d != null
                   && !IsFrameworkProtected(d.Id)
                   && (_sessionKind == TimfSessionKind.Menu || !d.ParticipatesInServer);
        }

        private string InteractionLockReason(ModDescriptor d)
        {
            if (d == null) return "Mod information is unavailable.";
            if (IsFrameworkProtected(d.Id))
                return "Framework library mods are always enabled.";
            if (!d.SessionAllowed)
                return d.SessionLockReason ?? "Unavailable in the current session.";
            if (_sessionKind != TimfSessionKind.Menu && d.ParticipatesInServer)
                return "Enablement is locked while a world/session is active.";
            if (!d.UserEnabled)
                return "Enable the mod from the main menu to open its settings.";
            if (!d.Loaded)
                return "The mod is not loaded in this process.";
            return null;
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
            var safetyFindings = IsTrustedFrameworkComponent(path)
                ? new List<TIMF.Core.Security.AssemblySafetyFinding>()
                : TIMF.Core.Security.AssemblySafetyScanner.ScanModPackage(path);
            if (safetyFindings.Count > 0)
            {
                _security.RecordBlockedLoad(safetyFindings);
                _log.Error("Security audit rejected " + Path.GetFileName(path) + ": " +
                           string.Join(" | ", safetyFindings.Take(8).Select(x => x.ToString())));
                return;
            }

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

        private bool IsTrustedFrameworkComponent(string path)
        {
            try
            {
                var packageDir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(packageDir) ||
                    Directory.GetFiles(packageDir, "*.dll", SearchOption.TopDirectoryOnly).Length != 1)
                {
                    _log.Error("Trusted framework component package contains unexpected DLLs: " + packageDir);
                    return false;
                }
                var relative = Path.GetFullPath(path).Substring(Path.GetFullPath(_home)
                    .TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar)
                    .Replace('\\', '/');
                var manifest = Path.Combine(_home, "trusted-framework-components.v1");
                if (!File.Exists(manifest)) return false;
                foreach (var line in File.ReadAllLines(manifest))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 2 || !string.Equals(parts[1], relative, StringComparison.OrdinalIgnoreCase))
                        continue;
                    using (var sha = SHA256.Create())
                    using (var stream = File.OpenRead(path))
                    {
                        var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        if (string.Equals(actual, parts[0], StringComparison.OrdinalIgnoreCase))
                        {
                            _log.Info("Trusted framework component hash verified: " + relative);
                            return true;
                        }
                    }
                    _log.Error("Trusted framework component hash mismatch: " + relative);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Error("Framework component trust verification failed for " + path, ex);
            }
            return false;
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
            var contentDir = Directory.Exists(Path.Combine(modDir, "Content"))
                ? Path.Combine(modDir, "Content") : modDir;
            var modLog = new Logging.FileLogger(
                Path.Combine(_home, "logs", "mod-" + Sanitize(d.Id) + ".log"),
                d.Id);
            var loc = new ModLocalization(modLog, modDir, _language);

            // Dedicated server: no client services. Client process: share the wired bag.
            IClientServices client = _isDedicated ? null : _clientServices;
            var ctx = new ModContext(
                modLog, _home, _configDir, modDir, d.Path, _services, loc,
                client, _authorityServices, _security.CreateFacade(d.Id, d.Path),
                new Security.ModStorage(_configDir, d.Id, contentDir),
                new Security.ModPatchService(d.Id, d.Assembly),
                new ModServicePublisher(_services, d.Assembly, d.Id));
            d.Context = ctx;

            _log.Info("Loading mod " + d.Id + " v" + d.Version + " side=" + d.Side + " from " + Path.GetFileName(d.Path));

            // Declarations are collected before Load so ids can be allocated for the whole set
            // at once. Ids therefore are not available yet inside Load — mods resolve
            // IContentLookup lazily, the same way IModRegistry already works.
            var contentMod = mod as TIMF.Content.IContentMod;
            if (contentMod != null)
                _content.Collect(contentMod, d.Id);

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

        /// <summary>
        /// True when <paramref name="actual"/> is at least <paramref name="minRequired"/>.
        ///
        /// Fails closed: an unparseable version on either side never satisfies a requirement.
        /// This matters because handshake callers compare a version string received from an
        /// untrusted peer — a lenient fallback would let a client send a garbage version and
        /// walk straight through a <see cref="TimfNetProfile.Required"/> gate.
        /// </summary>
        internal static bool VersionOk(string actual, string minRequired)
        {
            ModVersion a, b;
            if (!ModVersion.TryParse(actual, out a) || !ModVersion.TryParse(minRequired, out b))
                return false;
            return a.CompareTo(b) >= 0;
        }

        /// <summary>True when the string is a version TIMF can compare. See <see cref="ModVersion"/>.</summary>
        internal static bool IsParsableVersion(string version)
        {
            ModVersion parsed;
            return ModVersion.TryParse(version, out parsed);
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
