using System;
using System.Collections.Generic;
using System.Linq;
using TIMF.Abstractions;
using TIMF.Core.Modding;
using TIMF.Core.Net;

namespace TIMF.Core.Session
{
    /// <summary>
    /// Tracks Main.netMode / dedServ / gameMenu and drives server-mod Activate/Deactivate
    /// plus TIMF handshake (only when local Server/Both mods exist).
    /// </summary>
    internal sealed class SessionService : ITimfSession
    {
        private readonly ILogger _log;
        private readonly ModLoader _mods;
        private readonly object _lock = new object();

        private TimfSessionKind _kind = TimfSessionKind.Menu;
        private bool _serverLogicEnabled;
        private bool _remoteTimfConfirmed;
        private List<ITimfRemoteModInfo> _enabledServerMods = new List<ITimfRemoteModInfo>();

        private TimfNetTransport _net;
        private bool _handshakeArmed;
        private DateTime _clientHelloDeadlineUtc = DateTime.MinValue;
        private DateTime _lastClientHelloUtc = DateTime.MinValue;
        private int _lastNetMode = -1;
        private bool _lastGameMenu = true;
        private bool _lastDedServ;
        private readonly HashSet<int> _greetedClients = new HashSet<int>();
        /// <summary>
        /// Host-side: clients we greeted that still must prove TIMF (ClientHello)
        /// before the deadline. Only used when the host has RequiredOnJoin mods.
        /// Vanilla / TIMF-without-server-mods never send ClientHello → timeout kick.
        /// </summary>
        private readonly Dictionary<int, DateTime> _pendingClientHello =
            new Dictionary<int, DateTime>();

        private const double ClientHelloTimeoutSeconds = 10.0;
        private const double HostExpectClientHelloSeconds = 10.0;

        public SessionService(ILogger log, ModLoader mods)
        {
            _log = log;
            _mods = mods;
        }

        public TimfSessionKind Kind
        {
            get { lock (_lock) return _kind; }
        }

        public bool ServerLogicEnabled
        {
            get { lock (_lock) return _serverLogicEnabled; }
        }

        public bool RemoteTimfConfirmed
        {
            get { lock (_lock) return _remoteTimfConfirmed; }
        }

        public IReadOnlyList<ITimfRemoteModInfo> EnabledServerMods
        {
            get { lock (_lock) return _enabledServerMods; }
        }

        public bool HasLocalServerSideMods => _mods.HasLocalServerSideMods;

        /// <summary>Install net hooks only when local Server/Both mods exist.</summary>
        public void Start()
        {
            if (!_mods.HasLocalServerSideMods)
            {
                _log.Info("SessionService: no local server-side mods — handshake disabled");
                return;
            }

            _net = new TimfNetTransport(_log, this);
            _net.Install();
            DedServSessionPollPatch.SetSession(this, _log);
            _log.Info("SessionService: handshake transport armed (MessageID " + TimfNetProtocol.MessageId + ")");

            // Dedicated server may already be "in session" at inject time.
            try
            {
                if (Terraria.Main.dedServ)
                    EnterHostLikeSession(TimfSessionKind.DedicatedServer);
            }
            catch (Exception ex)
            {
                _log.Error("SessionService.Start dedicated check failed", ex);
            }
        }

        public void Stop()
        {
            try
            {
                LeaveSession("stop");
            }
            catch { /* ignore */ }

            try { _net?.Uninstall(); }
            catch { /* ignore */ }
            _net = null;
        }

        /// <summary>Called each UI / main-thread tick (from GameHooks).</summary>
        public void Poll()
        {
            try
            {
                bool gameMenu;
                int netMode;
                bool dedServ;
                try
                {
                    gameMenu = Terraria.Main.gameMenu;
                    netMode = Terraria.Main.netMode;
                    dedServ = Terraria.Main.dedServ;
                }
                catch
                {
                    return;
                }

                if (netMode != _lastNetMode || gameMenu != _lastGameMenu || dedServ != _lastDedServ)
                {
                    _lastNetMode = netMode;
                    _lastGameMenu = gameMenu;
                    _lastDedServ = dedServ;
                    OnWorldFlagsChanged(gameMenu, netMode, dedServ);
                }

                // Client join: retry ClientHello until HostHello or timeout.
                if (_kind == TimfSessionKind.MultiplayerClient && _handshakeArmed && !_remoteTimfConfirmed
                    && _clientHelloDeadlineUtc != DateTime.MinValue)
                {
                    if (DateTime.UtcNow > _clientHelloDeadlineUtc)
                    {
                        _log.Info("SessionService: HostHello timeout — treating remote as non-TIMF (no server mods)");
                        _clientHelloDeadlineUtc = DateTime.MinValue;
                        _handshakeArmed = false;
                    }
                    else if (_lastClientHelloUtc == DateTime.MinValue
                             || (DateTime.UtcNow - _lastClientHelloUtc).TotalSeconds >= 2.0)
                    {
                        try
                        {
                            SendClientHello();
                            _lastClientHelloUtc = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            _log.Error("ClientHello retry failed", ex);
                        }
                    }
                }

                // Host: greet newly connected clients that look ready;
                // then enforce ClientHello deadline (kicks pure vanilla when RequiredOnJoin).
                if ((_kind == TimfSessionKind.Host || _kind == TimfSessionKind.DedicatedServer)
                    && _serverLogicEnabled && _net != null)
                {
                    TryGreetNewClients();
                    EnforcePendingClientHellos();
                }
            }
            catch (Exception ex)
            {
                _log.Error("SessionService.Poll failed", ex);
            }
        }

        private void OnWorldFlagsChanged(bool gameMenu, int netMode, bool dedServ)
        {
            if (dedServ)
            {
                if (_kind != TimfSessionKind.DedicatedServer)
                    EnterHostLikeSession(TimfSessionKind.DedicatedServer);
                return;
            }

            if (gameMenu)
            {
                if (_kind != TimfSessionKind.Menu)
                    LeaveSession("menu");
                return;
            }

            // In world / multiplayer session.
            if (netMode == 0)
            {
                if (_kind != TimfSessionKind.SinglePlayer)
                    EnterHostLikeSession(TimfSessionKind.SinglePlayer);
            }
            else if (netMode == 2)
            {
                if (_kind != TimfSessionKind.Host)
                    EnterHostLikeSession(TimfSessionKind.Host);
            }
            else if (netMode == 1)
            {
                if (_kind != TimfSessionKind.MultiplayerClient)
                    EnterMultiplayerClient();
            }
        }

        private void EnterHostLikeSession(TimfSessionKind kind)
        {
            LeaveSession("switch->" + kind);

            lock (_lock)
            {
                _kind = kind;
                _serverLogicEnabled = true;
                _remoteTimfConfirmed = true; // local authority
                _handshakeArmed = _mods.HasLocalServerSideMods;
                _greetedClients.Clear();
                _pendingClientHello.Clear();
            }

            _log.Info("SessionService: enter " + kind + " — activating all local server mods");
            try
            {
                _mods.ActivateAllLocalServerMods();
                lock (_lock)
                {
                    _enabledServerMods = _mods.GetActiveServerModInfos().ToList();
                }
            }
            catch (Exception ex)
            {
                _log.Error("ActivateAllLocalServerMods failed", ex);
            }
        }

        private void EnterMultiplayerClient()
        {
            LeaveSession("switch->client");

            lock (_lock)
            {
                _kind = TimfSessionKind.MultiplayerClient;
                _serverLogicEnabled = false;
                _remoteTimfConfirmed = false;
                _enabledServerMods = new List<ITimfRemoteModInfo>();
                _handshakeArmed = _mods.HasLocalServerSideMods;
                _clientHelloDeadlineUtc = DateTime.MinValue;
            }

            if (!_mods.HasLocalServerSideMods)
            {
                _log.Info("SessionService: multiplayer client without local server mods — no handshake");
                return;
            }

            _log.Info("SessionService: multiplayer client — sending ClientHello");
            try
            {
                SendClientHello();
                _clientHelloDeadlineUtc = DateTime.UtcNow.AddSeconds(ClientHelloTimeoutSeconds);
            }
            catch (Exception ex)
            {
                _log.Error("ClientHello send failed", ex);
            }
        }

        private void LeaveSession(string reason)
        {
            TimfSessionKind prev;
            lock (_lock)
            {
                prev = _kind;
                if (prev == TimfSessionKind.Menu && !_serverLogicEnabled && _enabledServerMods.Count == 0)
                    return;
            }

            if (prev != TimfSessionKind.Menu)
                _log.Info("SessionService: leave " + prev + " (" + reason + ")");

            try { _mods.DeactivateAllServerMods(); }
            catch (Exception ex) { _log.Error("DeactivateAllServerMods failed", ex); }

            lock (_lock)
            {
                _kind = TimfSessionKind.Menu;
                _serverLogicEnabled = false;
                _remoteTimfConfirmed = false;
                _enabledServerMods = new List<ITimfRemoteModInfo>();
                _handshakeArmed = false;
                _clientHelloDeadlineUtc = DateTime.MinValue;
                _greetedClients.Clear();
                _pendingClientHello.Clear();
            }
        }

        private void SendClientHello()
        {
            if (_net == null)
                return;

            var msg = new TimfNetMessage
            {
                Proto = TimfNetProtocol.ProtoVersion,
                Kind = TimfNetKind.ClientHello,
                Mods = _mods.ServerCatalog.Snapshot().Select(ToNetEntry).ToList(),
            };
            _net.SendToServer(msg);
        }

        private void SendHostHello(int remoteClient)
        {
            if (_net == null)
                return;

            var msg = new TimfNetMessage
            {
                Proto = TimfNetProtocol.ProtoVersion,
                Kind = TimfNetKind.HostHello,
                Mods = _mods.ServerCatalog.Snapshot().Select(ToNetEntry).ToList(),
            };
            _net.SendToClient(remoteClient, msg);
        }

        private void SendHostKick(int remoteClient, string reason)
        {
            var detail = string.IsNullOrWhiteSpace(reason)
                ? "Missing required TIMF server mods."
                : reason.Trim();

            // Host-local visibility (who was kicked and why).
            NotifyUser("Kicking client slot " + remoteClient + " — " + detail, true);

            if (_net != null)
            {
                var msg = new TimfNetMessage
                {
                    Proto = TimfNetProtocol.ProtoVersion,
                    Kind = TimfNetKind.HostKick,
                    KickReason = detail,
                    Mods = new List<TimfNetModEntry>(),
                };
                try { _net.SendToClient(remoteClient, msg); }
                catch (Exception ex) { _log.Error("HostKick packet failed", ex); }
            }

            try
            {
                // Vanilla kick dialog on the client shows this NetworkText.
                var text = Terraria.Localization.NetworkText.FromLiteral("TIMF: " + detail);
                Terraria.NetMessage.BootPlayer(remoteClient, text);
            }
            catch (Exception ex)
            {
                _log.Error("BootPlayer failed for client " + remoteClient, ex);
            }
        }

        private static TimfNetModEntry ToNetEntry(ServerModEntry e)
        {
            return new TimfNetModEntry
            {
                Id = e.Id,
                Version = e.Version,
                RequiredOnJoin = e.RequiredOnJoin,
            };
        }

        private static ServerModEntry FromNetEntry(TimfNetModEntry e)
        {
            return new ServerModEntry(
                e != null ? e.Id : "",
                e != null ? e.Version : "0.0.0",
                e != null && e.RequiredOnJoin);
        }

        private void TryGreetNewClients()
        {
            try
            {
                var clients = Terraria.Netplay.Clients;
                if (clients == null)
                    return;

                var requireProof = HostHasRequiredJoinMods();

                for (var i = 0; i < clients.Length; i++)
                {
                    var c = clients[i];
                    if (c == null || !c.IsActive)
                    {
                        _greetedClients.Remove(i);
                        _pendingClientHello.Remove(i);
                        continue;
                    }

                    // State >= 10 is roughly "playing" in vanilla; greet a bit earlier (>= 3) so handshake can finish.
                    if (c.State < 3)
                        continue;

                    if (_greetedClients.Contains(i))
                        continue;

                    _greetedClients.Add(i);
                    _log.Info("SessionService: HostHello -> client slot " + i + " (" + (c.Name ?? "?") + ")");
                    SendHostHello(i);

                    // Pure vanilla never replies with ClientHello. If any host server mod is
                    // RequiredOnJoin, arm a deadline so we can BootPlayer with a clear reason
                    // instead of silently leaving them in an incomplete TIMF session.
                    if (requireProof)
                    {
                        _pendingClientHello[i] = DateTime.UtcNow.AddSeconds(HostExpectClientHelloSeconds);
                        _log.Info("SessionService: expecting ClientHello from slot " + i
                                  + " within " + HostExpectClientHelloSeconds + "s (RequiredOnJoin active)");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("TryGreetNewClients failed", ex);
            }
        }

        /// <summary>
        /// True when the host catalog has at least one Server/Both mod with RequiredOnJoin.
        /// Only then must joining clients prove TIMF (ClientHello); otherwise vanilla is allowed.
        /// </summary>
        private bool HostHasRequiredJoinMods()
        {
            try
            {
                foreach (var e in _mods.ServerCatalog.Entries)
                {
                    if (e != null && e.RequiredOnJoin)
                        return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        /// <summary>
        /// Kick clients that never answered ClientHello after HostHello, when RequiredOnJoin is set.
        /// Covers pure vanilla and TIMF installs with zero server-side mods (both never send Hello).
        /// </summary>
        private void EnforcePendingClientHellos()
        {
            if (_pendingClientHello.Count == 0)
                return;

            try
            {
                var now = DateTime.UtcNow;
                List<int> expired = null;
                foreach (var kv in _pendingClientHello)
                {
                    if (now <= kv.Value)
                        continue;
                    if (expired == null)
                        expired = new List<int>();
                    expired.Add(kv.Key);
                }

                if (expired == null)
                    return;

                foreach (var slot in expired)
                {
                    _pendingClientHello.Remove(slot);

                    // Slot may already have left.
                    try
                    {
                        var clients = Terraria.Netplay.Clients;
                        if (clients == null || slot < 0 || slot >= clients.Length
                            || clients[slot] == null || !clients[slot].IsActive)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    var reason = "This server requires TIMF with matching server mods. "
                                 + "Install TIMF + the required Server/Both mods listed by the host, then rejoin. "
                                 + "Missing: " + FormatMissingMods(
                                     _mods.ServerCatalog.Snapshot()
                                         .Where(e => e.RequiredOnJoin)
                                         .Select(e => e.Id + " (>=" + e.Version + ")")
                                         .ToList());
                    _log.Warn("SessionService: no ClientHello from slot " + slot
                              + " within deadline — treating as vanilla/non-TIMF; kicking");
                    SendHostKick(slot, reason);
                }
            }
            catch (Exception ex)
            {
                _log.Error("EnforcePendingClientHellos failed", ex);
            }
        }


        /// <summary>
        /// Surface a TIMF message to the local player (chat / status). Logs always; UI when possible.
        /// </summary>
        private void NotifyUser(string message, bool isError = true)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (isError)
                _log.Warn(message);
            else
                _log.Info(message);

            try
            {
                if (Terraria.Main.dedServ)
                    return;

                // Multi-line friendly for long missing-mod lists.
                var color = isError
                    ? new Microsoft.Xna.Framework.Color(255, 80, 80)
                    : new Microsoft.Xna.Framework.Color(100, 200, 255);

                var text = message;
                if (!text.StartsWith("TIMF:", StringComparison.OrdinalIgnoreCase))
                    text = "TIMF: " + text;

                // Prefer multiline so long "missing: a, b, c" lists stay readable.
                try
                {
                    Terraria.Main.NewTextMultiline(text, false, color, 600);
                }
                catch
                {
                    Terraria.Main.NewText(text, color);
                }
            }
            catch (Exception ex)
            {
                _log.Debug("NotifyUser UI failed: " + ex.Message);
            }
        }

        private static string FormatMissingMods(IList<string> missing)
        {
            if (missing == null || missing.Count == 0)
                return "(none)";
            return string.Join(", ", missing);
        }
        /// <summary>Called from TimfNetTransport when a valid TIMF frame is received.</summary>
        internal void OnNetMessage(int fromWho, TimfNetMessage msg)
        {
            if (msg == null)
                return;

            if (!_mods.HasLocalServerSideMods)
                return; // should never be installed, but belt-and-suspenders

            try
            {
                switch (msg.Kind)
                {
                    case TimfNetKind.ClientHello:
                        OnClientHello(fromWho, msg);
                        break;
                    case TimfNetKind.HostHello:
                        OnHostHello(msg);
                        break;
                    case TimfNetKind.HostKick:
                        OnHostKick(msg);
                        break;
                    case TimfNetKind.ClientAck:
                        _log.Info("SessionService: ClientAck from " + fromWho);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Error("OnNetMessage failed kind=" + msg.Kind, ex);
            }
        }

        private void OnClientHello(int fromWho, TimfNetMessage msg)
        {
            if (_kind != TimfSessionKind.Host && _kind != TimfSessionKind.DedicatedServer)
                return;

            // Any ClientHello proves the peer has TIMF + server-side protocol; clear expect-timer.
            _pendingClientHello.Remove(fromWho);

            var clientMods = (msg.Mods ?? new List<TimfNetModEntry>())
                .Select(FromNetEntry).ToList();

            // Host dictates: required host mods must be present on client with VersionOk.
            var missing = new List<string>();
            foreach (var hostMod in _mods.ServerCatalog.Snapshot())
            {
                if (!hostMod.RequiredOnJoin)
                    continue;

                var client = clientMods.FirstOrDefault(c =>
                    string.Equals(c.Id, hostMod.Id, StringComparison.OrdinalIgnoreCase));
                if (client == null)
                {
                    missing.Add(hostMod.Id);
                    continue;
                }

                // Client must have version >= host advertised version.
                if (!ModLoader.VersionOk(client.Version, hostMod.Version))
                    missing.Add(hostMod.Id + " (need >=" + hostMod.Version + ", have " + client.Version + ")");
            }

            if (missing.Count > 0)
            {
                var reason = "You are missing required TIMF server mods: " + FormatMissingMods(missing);
                _log.Warn("SessionService: kicking client " + fromWho + " — " + reason);
                SendHostKick(fromWho, reason);
                return;
            }

            _log.Info("SessionService: ClientHello ok from " + fromWho + "; sending HostHello");
            SendHostHello(fromWho);
            _greetedClients.Add(fromWho);
        }

        private void OnHostHello(TimfNetMessage msg)
        {
            if (_kind != TimfSessionKind.MultiplayerClient)
                return;

            var hostList = (msg.Mods ?? new List<TimfNetModEntry>())
                .Select(FromNetEntry).ToList();

            List<string> missingRequired;
            var enabled = ServerModCatalog.IntersectWithHost(hostList, _mods.ServerCatalog, out missingRequired);

            if (missingRequired.Count > 0)
            {
                var reason = "Missing required host server mods: " + FormatMissingMods(missingRequired)
                    + ". Install matching Server/Both mods and rejoin.";
                NotifyUser(reason, true);
                try
                {
                    Terraria.Netplay.Disconnect = true;
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to set Netplay.Disconnect", ex);
                }

                return;
            }

            _log.Info("SessionService: HostHello accepted; enabling "
                      + enabled.Count + " server mod(s): "
                      + string.Join(", ", enabled.Select(e => e.Id)));

            try
            {
                _mods.ActivateServerMods(enabled.Select(e => e.Id));
            }
            catch (Exception ex)
            {
                _log.Error("ActivateServerMods after HostHello failed", ex);
            }

            {
                var enabledIds = string.Join(", ", enabled.Select(e => e.Id));
                if (enabled.Count > 0)
                    NotifyUser("Server mods enabled: " + enabledIds, false);
                else
                    NotifyUser("Host TIMF handshake ok; no matching server mods to enable.", false);
            }

            lock (_lock)
            {
                _serverLogicEnabled = enabled.Count > 0;
                _remoteTimfConfirmed = true;
                _enabledServerMods = enabled.Cast<ITimfRemoteModInfo>().ToList();
                _clientHelloDeadlineUtc = DateTime.MinValue;
            }

            // Ack
            if (_net != null)
            {
                var ack = new TimfNetMessage
                {
                    Proto = TimfNetProtocol.ProtoVersion,
                    Kind = TimfNetKind.ClientAck,
                    Mods = enabled.Select(ToNetEntry).ToList(),
                };
                _net.SendToServer(ack);
            }
        }

        private void OnHostKick(TimfNetMessage msg)
        {
            var reason = (msg != null && !string.IsNullOrWhiteSpace(msg.KickReason))
                ? msg.KickReason.Trim()
                : "Kicked by host (TIMF server mod mismatch).";
            NotifyUser(reason, true);
            try { Terraria.Netplay.Disconnect = true; }
            catch { /* ignore */ }
        }
    }
}
