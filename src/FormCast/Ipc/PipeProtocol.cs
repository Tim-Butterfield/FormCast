// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Ipc/PipeProtocol.cs
// ===================
//
// Shared protocol constants and message framing for the
// FormCast.Host named-pipe IPC. Lives in the plugin project so the
// in-process plugin can reach it directly; the FormCast.Host.exe
// project picks it up via a <Compile Include="...\Link="..."/>
// reference (its csproj has the link entry). Both sides therefore
// build identical bytes for every message and never drift in version.
//
// Wire format (every message)
// ---------------------------
//
// +--------+--------+----------------+
// | u32 LE | u8 | payload bytes |
// | length | type | (length-1) |
// +--------+--------+----------------+
//
// `length` is the count of bytes that FOLLOW the length field
// itself, so it always includes the type byte. The cap is 1 MiB to
// keep a buggy or malicious peer from forcing the other side into
// allocating a multi-gigabyte buffer.
//
// Message types
// -------------
//
// 0x01 HelloRequest payload = u32 client protocol version,
// u8 client name length,
// UTF-8 client name bytes
// 0x02 HelloResponse payload = u32 server protocol version,
// u8 status (0=ok 1=mismatch),
// u8 server name length,
// UTF-8 server name bytes
// 0x03 Ping payload = u32 nonce
// 0x04 Pong payload = u32 nonce (echoed)
// 0x05 Goodbye payload = (none)
//
// These five message types make the IPC contract testable end-to-end.
// Additional IRemoteFormRegistry op codes (open / close / set / get /
// events drain) can be added as needed.
//
// Endianness is little-endian on every numeric field. The .NET
// BinaryReader/BinaryWriter default to little-endian on every
// supported platform, so we use them directly without forcing
// byte order.

using System;
using System.IO;
using System.Text;

namespace FormCast.Ipc
{
 /// <summary>
 /// Protocol constants and length-prefixed framing helpers shared
 /// by <c>HostClient</c> (in the plugin project) and the
 /// FormCast.Host <c>PipeServer</c>. Pure logic, no I/O of its
 /// own beyond the <see cref="Stream"/> argument the helpers
 /// receive. The cref to HostClient is intentionally a plain
 /// <c></c> tag because this file is compile-linked from both
 /// projects and HostClient is only resolvable in one of them.
 /// </summary>
    internal static class PipeProtocol
    {
 /// <summary>
 /// Wire-protocol version. Bumped whenever the message
 /// layout changes in a way that older peers cannot parse.
 /// Both sides exchange this in the Hello handshake; a
 /// mismatch closes the connection cleanly.
 /// </summary>
        public const uint ProtocolVersion = 1;

 /// <summary>
 /// Maximum allowed payload length in bytes. A peer that
 /// frames a longer message is rejected as malformed.
 /// </summary>
        public const int MaxPayloadBytes = 1024 * 1024;

 /// <summary>
 /// Pipe name suffix appended to the per-session base. Full
 /// pipe name is built by <see cref="BuildPipeName"/>.
 /// </summary>
        public const string PipeNamePrefix = "FormCast.Host.";

 /// <summary>Message type byte for the client-side hello.</summary>
        public const byte MsgHelloRequest = 0x01;

 /// <summary>Message type byte for the server-side hello reply.</summary>
        public const byte MsgHelloResponse = 0x02;

 /// <summary>Message type byte for a ping (nonce echo request).</summary>
        public const byte MsgPing = 0x03;

 /// <summary>Message type byte for a pong (nonce echo response).</summary>
        public const byte MsgPong = 0x04;

 /// <summary>Message type byte for a graceful goodbye.</summary>
        public const byte MsgGoodbye = 0x05;

 /// <summary>Hello status: handshake accepted.</summary>
        public const byte HelloOk = 0x00;

 /// <summary>Hello status: protocol version mismatch, close after reply.</summary>
        public const byte HelloVersionMismatch = 0x01;

 /// <summary>
 /// Build the pipe name for a given session id. Matches
 /// HostMutex.BuildName so a single session-id resolves to
 /// the same singleton mutex AND the same pipe.
 /// </summary>
        public static string BuildPipeName(string sessionId)
        {
            if (sessionId is null) { throw new ArgumentNullException(nameof(sessionId)); }
            return PipeNamePrefix + sessionId;
        }

 // -----------------------------------------------------------------
 // Length-prefixed framing
 // -----------------------------------------------------------------

 /// <summary>
 /// Write a single length-prefixed message to
 /// <paramref name="stream"/>. <paramref name="payload"/>
 /// includes the type byte at index 0.
 /// </summary>
        public static void WriteFrame(Stream stream, byte[] payload)
        {
            if (stream is null) { throw new ArgumentNullException(nameof(stream)); }
            if (payload is null) { throw new ArgumentNullException(nameof(payload)); }
            if (payload.Length == 0)
            {
                throw new ArgumentException("payload must contain at least the type byte", nameof(payload));
            }
            if (payload.Length > MaxPayloadBytes)
            {
                throw new ArgumentException(
                    "payload exceeds MaxPayloadBytes (" + MaxPayloadBytes + ")",
                    nameof(payload));
            }

            byte[] header = new byte[4];
            uint len = (uint)payload.Length;
            header[0] = (byte)(len & 0xFF);
            header[1] = (byte)((len >> 8) & 0xFF);
            header[2] = (byte)((len >> 16) & 0xFF);
            header[3] = (byte)((len >> 24) & 0xFF);
            stream.Write(header, 0, header.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

 /// <summary>
 /// Read a single length-prefixed frame from
 /// <paramref name="stream"/>. Returns the payload bytes
 /// (with the type byte at index 0). Returns <c>null</c> on
 /// clean EOF (peer closed); throws
 /// <see cref="InvalidDataException"/> on a malformed frame
 /// or oversized payload.
 /// </summary>
        public static byte[]? ReadFrame(Stream stream)
        {
            if (stream is null) { throw new ArgumentNullException(nameof(stream)); }

            byte[] header = new byte[4];
            int got = ReadFully(stream, header, 0, 4);
            if (got == 0) { return null; }
            if (got != 4)
            {
                throw new InvalidDataException(
                    "PipeProtocol: short read on length header (" + got + " bytes)");
            }

            uint len =
                (uint)header[0] |
                ((uint)header[1] << 8) |
                ((uint)header[2] << 16) |
                ((uint)header[3] << 24);
            if (len == 0)
            {
                throw new InvalidDataException("PipeProtocol: zero-length frame");
            }
            if (len > MaxPayloadBytes)
            {
                throw new InvalidDataException(
                    "PipeProtocol: frame length " + len +
                    " exceeds MaxPayloadBytes " + MaxPayloadBytes);
            }

            byte[] payload = new byte[len];
            int payloadGot = ReadFully(stream, payload, 0, (int)len);
            if (payloadGot != (int)len)
            {
                throw new InvalidDataException(
                    "PipeProtocol: short read on payload (got " + payloadGot +
                    " of " + len + ")");
            }
            return payload;
        }

        private static int ReadFully(Stream stream, byte[] buf, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n;
                try
                {
                    n = stream.Read(buf, offset + total, count - total);
                }
                catch (ObjectDisposedException) { return total; }
                catch (IOException) { return total; }
                if (n <= 0) { return total; }
                total += n;
            }
            return total;
        }

 // -----------------------------------------------------------------
 // Message builders / parsers
 // -----------------------------------------------------------------

 /// <summary>
 /// Build a HelloRequest payload.
 /// </summary>
        public static byte[] BuildHelloRequest(string clientName)
        {
            if (clientName is null) { throw new ArgumentNullException(nameof(clientName)); }
            byte[] nameBytes = Encoding.UTF8.GetBytes(clientName);
            if (nameBytes.Length > 255)
            {
                throw new ArgumentException("clientName too long (max 255 UTF-8 bytes)", nameof(clientName));
            }
            byte[] payload = new byte[1 + 4 + 1 + nameBytes.Length];
            payload[0] = MsgHelloRequest;
            uint v = ProtocolVersion;
            payload[1] = (byte)(v & 0xFF);
            payload[2] = (byte)((v >> 8) & 0xFF);
            payload[3] = (byte)((v >> 16) & 0xFF);
            payload[4] = (byte)((v >> 24) & 0xFF);
            payload[5] = (byte)nameBytes.Length;
            Buffer.BlockCopy(nameBytes, 0, payload, 6, nameBytes.Length);
            return payload;
        }

 /// <summary>
 /// Parse a HelloRequest payload (type byte at index 0).
 /// Throws <see cref="InvalidDataException"/> when malformed.
 /// </summary>
        public static (uint Version, string Name) ParseHelloRequest(byte[] payload)
        {
            if (payload is null) { throw new ArgumentNullException(nameof(payload)); }
            if (payload.Length < 6 || payload[0] != MsgHelloRequest)
            {
                throw new InvalidDataException("PipeProtocol: malformed HelloRequest");
            }
            uint v =
                (uint)payload[1] |
                ((uint)payload[2] << 8) |
                ((uint)payload[3] << 16) |
                ((uint)payload[4] << 24);
            int nameLen = payload[5];
            if (payload.Length != 6 + nameLen)
            {
                throw new InvalidDataException("PipeProtocol: HelloRequest length mismatch");
            }
            string name = Encoding.UTF8.GetString(payload, 6, nameLen);
            return (v, name);
        }

 /// <summary>
 /// Build a HelloResponse payload.
 /// </summary>
        public static byte[] BuildHelloResponse(byte status, string serverName)
        {
            if (serverName is null) { throw new ArgumentNullException(nameof(serverName)); }
            byte[] nameBytes = Encoding.UTF8.GetBytes(serverName);
            if (nameBytes.Length > 255)
            {
                throw new ArgumentException("serverName too long", nameof(serverName));
            }
            byte[] payload = new byte[1 + 4 + 1 + 1 + nameBytes.Length];
            payload[0] = MsgHelloResponse;
            uint v = ProtocolVersion;
            payload[1] = (byte)(v & 0xFF);
            payload[2] = (byte)((v >> 8) & 0xFF);
            payload[3] = (byte)((v >> 16) & 0xFF);
            payload[4] = (byte)((v >> 24) & 0xFF);
            payload[5] = status;
            payload[6] = (byte)nameBytes.Length;
            Buffer.BlockCopy(nameBytes, 0, payload, 7, nameBytes.Length);
            return payload;
        }

 /// <summary>Parse a HelloResponse payload.</summary>
        public static (uint Version, byte Status, string Name) ParseHelloResponse(byte[] payload)
        {
            if (payload is null) { throw new ArgumentNullException(nameof(payload)); }
            if (payload.Length < 7 || payload[0] != MsgHelloResponse)
            {
                throw new InvalidDataException("PipeProtocol: malformed HelloResponse");
            }
            uint v =
                (uint)payload[1] |
                ((uint)payload[2] << 8) |
                ((uint)payload[3] << 16) |
                ((uint)payload[4] << 24);
            byte status = payload[5];
            int nameLen = payload[6];
            if (payload.Length != 7 + nameLen)
            {
                throw new InvalidDataException("PipeProtocol: HelloResponse length mismatch");
            }
            string name = Encoding.UTF8.GetString(payload, 7, nameLen);
            return (v, status, name);
        }

 /// <summary>Build a Ping payload with the given nonce.</summary>
        public static byte[] BuildPing(uint nonce)
        {
            byte[] payload = new byte[1 + 4];
            payload[0] = MsgPing;
            payload[1] = (byte)(nonce & 0xFF);
            payload[2] = (byte)((nonce >> 8) & 0xFF);
            payload[3] = (byte)((nonce >> 16) & 0xFF);
            payload[4] = (byte)((nonce >> 24) & 0xFF);
            return payload;
        }

 /// <summary>Parse a Ping payload (returns the nonce).</summary>
        public static uint ParsePing(byte[] payload)
        {
            if (payload is null || payload.Length != 5 || payload[0] != MsgPing)
            {
                throw new InvalidDataException("PipeProtocol: malformed Ping");
            }
            return ParseNonce(payload, 1);
        }

 /// <summary>Build a Pong payload with the echoed nonce.</summary>
        public static byte[] BuildPong(uint nonce)
        {
            byte[] payload = new byte[1 + 4];
            payload[0] = MsgPong;
            payload[1] = (byte)(nonce & 0xFF);
            payload[2] = (byte)((nonce >> 8) & 0xFF);
            payload[3] = (byte)((nonce >> 16) & 0xFF);
            payload[4] = (byte)((nonce >> 24) & 0xFF);
            return payload;
        }

 /// <summary>Parse a Pong payload (returns the echoed nonce).</summary>
        public static uint ParsePong(byte[] payload)
        {
            if (payload is null || payload.Length != 5 || payload[0] != MsgPong)
            {
                throw new InvalidDataException("PipeProtocol: malformed Pong");
            }
            return ParseNonce(payload, 1);
        }

 /// <summary>Build a Goodbye payload (just the type byte).</summary>
        public static byte[] BuildGoodbye()
        {
            return new[] { MsgGoodbye };
        }

        private static uint ParseNonce(byte[] buf, int offset)
        {
            return
                (uint)buf[offset] |
                ((uint)buf[offset + 1] << 8) |
                ((uint)buf[offset + 2] << 16) |
                ((uint)buf[offset + 3] << 24);
        }
    }
}
