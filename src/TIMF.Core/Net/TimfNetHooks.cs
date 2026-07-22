using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;
using TIMF.Core.Session;

namespace TIMF.Core.Net
{
    /// <summary>
    /// Harmony hooks + send helpers for TIMF frames on MessageID.Unused83.
    /// Only installed when the local install has Server/Both mods.
    /// </summary>
    internal sealed class TimfNetTransport
    {
        private readonly ILogger _log;
        private readonly SessionService _session;
        private Harmony _harmony;
        private bool _installed;

        private static TimfNetTransport _current;
        private static MethodInfo _sendPacketToServer;
        private static MethodInfo _sendPacket;

        public TimfNetTransport(ILogger log, SessionService session)
        {
            _log = log;
            _session = session;
        }

        public void Install()
        {
            if (_installed)
                return;

            _current = this;
            try
            {
                _harmony = new Harmony("timf.core.net");
                var getData = AccessTools.Method(typeof(MessageBuffer), "GetData",
                    new[] { typeof(int), typeof(int), typeof(int).MakeByRefType() });
                if (getData == null)
                    throw new InvalidOperationException("MessageBuffer.GetData not found");

                var prefix = AccessTools.Method(typeof(TimfNetTransport), nameof(GetDataPrefix));
                _harmony.Patch(getData, prefix: new HarmonyMethod(prefix));
                _installed = true;
                _log.Info("TIMF net hooks installed (MessageBuffer.GetData prefix)");
            }
            catch (Exception ex)
            {
                _installed = false;
                _current = null;
                _log.Error("Failed to install TIMF net hooks", ex);
            }
        }

        public void Uninstall()
        {
            if (!_installed)
                return;

            try
            {
                _harmony?.UnpatchAll("timf.core.net");
            }
            catch (Exception ex)
            {
                _log.Error("Unpatch TIMF net hooks failed", ex);
            }

            _harmony = null;
            _installed = false;
            if (ReferenceEquals(_current, this))
                _current = null;
        }

        /// <summary>
        /// Prefix on MessageBuffer.GetData. When the packet is a TIMF frame, consume it and
        /// skip vanilla handling (return false).
        /// </summary>
        private static bool GetDataPrefix(MessageBuffer __instance, int start, int length, ref int messageType)
        {
            var self = _current;
            if (self == null || __instance == null)
                return true;

            try
            {
                // Peek message type byte at start of payload (vanilla layout: length already stripped by caller;
                // GetData's start points at the message type byte).
                if (length < 1 || __instance.readBuffer == null)
                    return true;

                if (start < 0 || start >= __instance.readBuffer.Length)
                    return true;

                var type = __instance.readBuffer[start];
                if (type != TimfNetProtocol.MessageId)
                    return true;

                messageType = TimfNetProtocol.MessageId;

                // Payload after the message-type byte.
                var payloadOffset = start + 1;
                var payloadLen = length - 1;
                if (payloadLen < 8)
                    return false; // consume garbage 83 without TIMF magic

                TimfNetMessage msg;
                if (!TimfNetProtocol.TryDecode(__instance.readBuffer, payloadOffset, payloadLen, out msg) || msg == null)
                    return false; // not our frame / wrong proto — swallow to avoid vanilla Unused83 side effects

                var who = __instance.whoAmI;
                self._session.OnNetMessage(who, msg);
                return false; // skip vanilla
            }
            catch (Exception ex)
            {
                try { self._log.Error("GetDataPrefix TIMF handler failed", ex); }
                catch { /* ignore */ }
                return true;
            }
        }

        public void SendToServer(TimfNetMessage msg)
        {
            SendRaw(msg, remoteClient: -1, toServer: true);
        }

        public void SendToClient(int remoteClient, TimfNetMessage msg)
        {
            SendRaw(msg, remoteClient: remoteClient, toServer: false);
        }

        private void SendRaw(TimfNetMessage msg, int remoteClient, bool toServer)
        {
            if (msg == null)
                return;

            try
            {
                var payload = TimfNetProtocol.Encode(msg);
                // Vanilla packet: [u16 length][u8 msgType][payload...]
                // length includes msgType + payload (not the length field itself).
                var bodyLen = 1 + payload.Length;
                if (bodyLen > ushort.MaxValue)
                    throw new InvalidOperationException("TIMF packet too large");

                using (var ms = new MemoryStream(2 + bodyLen))
                using (var w = new BinaryWriter(ms))
                {
                    w.Write((ushort)bodyLen);
                    w.Write((byte)TimfNetProtocol.MessageId);
                    w.Write(payload);
                    w.Flush();
                    var packet = ms.ToArray();

                    if (toServer)
                    {
                        // Client -> server (method is public on runtime Terraria but may be
                        // missing from the compile-time reference surface — invoke by reflection).
                        EnsureSendMethods();
                        if (_sendPacketToServer == null)
                            throw new MissingMethodException("NetMessage.SendPacketToServer(byte[])");
                        _sendPacketToServer.Invoke(null, new object[] { packet });
                    }
                    else
                    {
                        // Server -> one client
                        if (remoteClient < 0)
                            return;
                        EnsureSendMethods();
                        if (_sendPacket == null)
                            throw new MissingMethodException("NetMessage.SendPacket(byte[], int)");
                        _sendPacket.Invoke(null, new object[] { packet, remoteClient });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("SendRaw TIMF packet failed (toServer=" + toServer + ", rc=" + remoteClient + ")", ex);
            }
        }

        private static void EnsureSendMethods()
        {
            if (_sendPacketToServer != null && _sendPacket != null)
                return;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            if (_sendPacketToServer == null)
            {
                _sendPacketToServer = typeof(NetMessage).GetMethod(
                    "SendPacketToServer", flags, null, new[] { typeof(byte[]) }, null);
            }
            if (_sendPacket == null)
            {
                _sendPacket = typeof(NetMessage).GetMethod(
                    "SendPacket", flags, null, new[] { typeof(byte[]), typeof(int) }, null);
            }
        }
    }

    /// <summary>
    /// Dedicated servers never hit OnPostDraw/DrawCursor; poll session from Netplay.UpdateInMainThread.
    /// </summary>
    [HarmonyPatch]
    internal static class DedServSessionPollPatch
    {
        private static SessionService _session;
        private static ILogger _log;

        internal static void SetSession(SessionService session, ILogger log)
        {
            _session = session;
            _log = log;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Netplay), "UpdateInMainThread");
        }

        private static void Postfix()
        {
            try
            {
                if (!Main.dedServ)
                    return;
                _session?.Poll();
            }
            catch (Exception ex)
            {
                try { _log?.Error("DedServ SessionService.Poll failed", ex); }
                catch { /* ignore */ }
            }
        }
    }
}