// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Ipc/HostClient.cs
// =================
//
// Client side of the FormCast.Host named-pipe IPC. Connects to the
// per-session pipe, performs the version handshake, exposes Ping and
// Goodbye, and offers a small SpawnHostIfMissing helper used to
// lazy-start the host on the first @FORMOPEN[..., Global\..., ...]
// call.
//
// The client is intentionally synchronous: every operation blocks
// the calling thread until the server replies or a timeout fires.
// The binding contract from PLUGIN_DESIGN.md section 7 #8 says
// cross-process IPC runs on the CallbackWorker, not the GUI thread,
// so blocking the worker is fine.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace FormCast.Ipc
{
 /// <summary>
 /// Synchronous client for the FormCast.Host named-pipe IPC.
 /// Construct, call <see cref="Connect"/>, then issue
 /// <see cref="Ping"/> / <see cref="Goodbye"/> as needed.
 /// Dispose closes the pipe cleanly.
 /// </summary>
    internal sealed class HostClient : IDisposable
    {
        private readonly string _sessionId;
        private readonly string _clientName;
        private NamedPipeClientStream? _pipe;
        private string? _serverName;
        private uint _serverVersion;

 /// <summary>
 /// Construct a client targeting the host singleton for the
 /// given session id. <paramref name="clientName"/> is the
 /// short identifier the server logs and surfaces in error
 /// messages (e.g. <c>"FormCast.Plugin"</c>).
 /// </summary>
        public HostClient(string sessionId, string clientName)
        {
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _clientName = clientName ?? throw new ArgumentNullException(nameof(clientName));
        }

 /// <summary>
 /// Server name reported in the HelloResponse, or
 /// <c>null</c> until <see cref="Connect"/> succeeds.
 /// </summary>
        public string? ServerName => _serverName;

 /// <summary>
 /// Server protocol version reported in the HelloResponse,
 /// or 0 until <see cref="Connect"/> succeeds.
 /// </summary>
        public uint ServerVersion => _serverVersion;

 /// <summary>
 /// Connect to the host pipe and perform the version
 /// handshake. Throws <see cref="IOException"/> if the pipe
 /// cannot be opened within <paramref name="connectTimeoutMs"/>,
 /// or <see cref="InvalidOperationException"/> if the server
 /// reports a version mismatch.
 /// </summary>
        public void Connect(int connectTimeoutMs = 5000)
        {
            if (_pipe is not null)
            {
                throw new InvalidOperationException("HostClient is already connected");
            }

            string pipeName = PipeProtocol.BuildPipeName(_sessionId);
            var pipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.None);
            try
            {
                pipe.Connect(connectTimeoutMs);
            }
            catch (TimeoutException ex)
            {
                pipe.Dispose();
                throw new IOException(
                    "HostClient.Connect: timed out waiting for FormCast.Host pipe '" +
                    pipeName + "'", ex);
            }
            _pipe = pipe;

 // Handshake: send hello, read reply, validate.
            byte[] helloReq = PipeProtocol.BuildHelloRequest(_clientName);
            PipeProtocol.WriteFrame(_pipe, helloReq);

            byte[]? helloRespBytes = PipeProtocol.ReadFrame(_pipe);
            if (helloRespBytes is null)
            {
                throw new IOException("HostClient.Connect: server closed before HelloResponse");
            }
            (uint version, byte status, string name) = PipeProtocol.ParseHelloResponse(helloRespBytes);
            _serverVersion = version;
            _serverName = name;
            if (status != PipeProtocol.HelloOk)
            {
                throw new InvalidOperationException(
                    "HostClient.Connect: protocol version mismatch (server " +
                    version + ", client " + PipeProtocol.ProtocolVersion + ")");
            }
        }

 /// <summary>
 /// Send a Ping with the given nonce and wait for the
 /// matching Pong. Returns the echoed nonce. Throws if the
 /// echoed nonce does not match.
 /// </summary>
        public uint Ping(uint nonce)
        {
            if (_pipe is null)
            {
                throw new InvalidOperationException("HostClient is not connected");
            }
            PipeProtocol.WriteFrame(_pipe, PipeProtocol.BuildPing(nonce));
            byte[]? resp = PipeProtocol.ReadFrame(_pipe);
            if (resp is null)
            {
                throw new IOException("HostClient.Ping: server closed before Pong");
            }
            uint echoed = PipeProtocol.ParsePong(resp);
            if (echoed != nonce)
            {
                throw new IOException(
                    "HostClient.Ping: nonce mismatch (sent " + nonce +
                    ", got " + echoed + ")");
            }
            return echoed;
        }

 /// <summary>
 /// Send a Goodbye message and close the connection. Idempotent.
 /// </summary>
        public void Goodbye()
        {
            if (_pipe is null) { return; }
            try
            {
                PipeProtocol.WriteFrame(_pipe, PipeProtocol.BuildGoodbye());
            }
            catch (IOException) { /* swallow: server may have already gone */ }
            catch (ObjectDisposedException) { /* swallow */ }
        }

 /// <inheritdoc />
        public void Dispose()
        {
            if (_pipe is null) { return; }
            try { Goodbye(); } catch { /* swallow */ }
            try { _pipe.Dispose(); } catch { /* swallow */ }
            _pipe = null;
        }

 // -----------------------------------------------------------------
 // Singleton spawn helper
 // -----------------------------------------------------------------

 /// <summary>
 /// Ensure FormCast.Host.exe is running for the given session
 /// id. If the singleton mutex is already held by an
 /// existing host, returns immediately. Otherwise launches
 /// <paramref name="hostExePath"/> as a detached process and
 /// polls for the mutex (or, equivalently, the pipe) to come
 /// up within <paramref name="timeoutMs"/>.
 /// </summary>
 /// <returns>
 /// <c>true</c> when the host is reachable; <c>false</c> on
 /// timeout. Throws on a spawn failure.
 /// </returns>
        public static bool SpawnHostIfMissing(
            string hostExePath,
            string sessionId,
            int timeoutMs = 5000)
        {
            if (hostExePath is null) { throw new ArgumentNullException(nameof(hostExePath)); }
            if (sessionId is null) { throw new ArgumentNullException(nameof(sessionId)); }

 // Fast path: try to connect immediately. If it works,
 // the host is already up.
            if (TryConnectOnce(sessionId)) { return true; }

 // Spawn the exe detached. We pass --session-id so the
 // host's mutex / pipe agree with the client's.
            var psi = new ProcessStartInfo
            {
                FileName = hostExePath,
                Arguments = "--session-id " + sessionId,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            using (Process.Start(psi)) { /* don't wait; daemon */ }

 // Poll for connectability. We tear down each probe
 // immediately so we don't accidentally hold a connection
 // the host considers "live".
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (TryConnectOnce(sessionId)) { return true; }
                Thread.Sleep(50);
            }
            return false;
        }

        private static bool TryConnectOnce(string sessionId)
        {
            try
            {
                using var probe = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: PipeProtocol.BuildPipeName(sessionId),
                    direction: PipeDirection.InOut);
                probe.Connect(timeout: 100);
                return true;
            }
            catch (TimeoutException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
