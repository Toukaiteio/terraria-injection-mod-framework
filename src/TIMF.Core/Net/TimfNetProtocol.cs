using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TIMF.Core.Net
{
    internal enum TimfNetKind : byte
    {
        ClientHello = 1,
        HostHello = 2,
        HostKick = 3,
        ClientAck = 4,
    }

    internal sealed class TimfNetModEntry
    {
        public string Id;
        public string Version;
        public bool RequiredOnJoin;
    }

    internal sealed class TimfNetMessage
    {
        public byte Proto;
        public TimfNetKind Kind;
        public byte Flags;
        public List<TimfNetModEntry> Mods = new List<TimfNetModEntry>();
        public string KickReason;
    }

    /// <summary>
    /// Binary codec for TIMF handshake frames carried on MessageID.Unused83.
    /// Layout: 'T''I''M''F' | Proto | Kind | Flags | ModCount:u16 | entries | [reason]
    /// </summary>
    internal static class TimfNetProtocol
    {
        public const int MessageId = 83; // Terraria.ID.MessageID.Unused83
        public const byte ProtoVersion = 1;
        public const byte Magic0 = (byte)'T';
        public const byte Magic1 = (byte)'I';
        public const byte Magic2 = (byte)'M';
        public const byte Magic3 = (byte)'F';
        public const byte FlagRequired = 0x01;

        public static byte[] Encode(TimfNetMessage msg)
        {
            if (msg == null)
                throw new ArgumentNullException("msg");

            using (var ms = new MemoryStream(256))
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Magic0);
                w.Write(Magic1);
                w.Write(Magic2);
                w.Write(Magic3);
                w.Write(msg.Proto == 0 ? ProtoVersion : msg.Proto);
                w.Write((byte)msg.Kind);
                w.Write(msg.Flags);

                var mods = msg.Mods ?? new List<TimfNetModEntry>();
                if (mods.Count > ushort.MaxValue)
                    throw new InvalidOperationException("Too many mods in TIMF packet");
                w.Write((ushort)mods.Count);
                foreach (var m in mods)
                {
                    WriteShortString(w, m != null ? m.Id : null);
                    WriteShortString(w, m != null ? m.Version : null);
                    byte mf = 0;
                    if (m != null && m.RequiredOnJoin)
                        mf |= FlagRequired;
                    w.Write(mf);
                }

                if (msg.Kind == TimfNetKind.HostKick)
                    WriteShortString(w, msg.KickReason);

                w.Flush();
                return ms.ToArray();
            }
        }

        public static bool TryDecode(byte[] buffer, int offset, int length, out TimfNetMessage msg)
        {
            msg = null;
            if (buffer == null || length < 8 || offset < 0 || offset + length > buffer.Length)
                return false;

            try
            {
                using (var ms = new MemoryStream(buffer, offset, length, writable: false))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (r.ReadByte() != Magic0 || r.ReadByte() != Magic1 ||
                        r.ReadByte() != Magic2 || r.ReadByte() != Magic3)
                        return false;

                    var proto = r.ReadByte();
                    if (proto != ProtoVersion)
                        return false;

                    var kindByte = r.ReadByte();
                    if (kindByte < 1 || kindByte > 4)
                        return false;

                    var flags = r.ReadByte();
                    var count = r.ReadUInt16();
                    var list = new List<TimfNetModEntry>(count);
                    for (var i = 0; i < count; i++)
                    {
                        var id = ReadShortString(r);
                        var ver = ReadShortString(r);
                        var mf = r.ReadByte();
                        list.Add(new TimfNetModEntry
                        {
                            Id = id,
                            Version = ver,
                            RequiredOnJoin = (mf & FlagRequired) != 0,
                        });
                    }

                    string reason = null;
                    if ((TimfNetKind)kindByte == TimfNetKind.HostKick && ms.Position < ms.Length)
                        reason = ReadShortString(r);

                    msg = new TimfNetMessage
                    {
                        Proto = proto,
                        Kind = (TimfNetKind)kindByte,
                        Flags = flags,
                        Mods = list,
                        KickReason = reason,
                    };
                    return true;
                }
            }
            catch
            {
                msg = null;
                return false;
            }
        }

        private static void WriteShortString(BinaryWriter w, string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                w.Write((byte)0);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > 255)
            {
                // Truncate rather than fail the whole session.
                var trimmed = new byte[255];
                Buffer.BlockCopy(bytes, 0, trimmed, 0, 255);
                bytes = trimmed;
            }

            w.Write((byte)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadShortString(BinaryReader r)
        {
            var len = r.ReadByte();
            if (len == 0)
                return "";
            var bytes = r.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
