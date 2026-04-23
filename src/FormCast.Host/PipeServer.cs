// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// PipeServer.cs
// =============
//
// NamedPipeServerStream loop for FormCast.Host.exe. Wraps the
// shared FormCast.Ipc.PipeProtocol framing in a single-client-at-a-
// time loop:
//
// 1. Create a NamedPipeServerStream with a PipeSecurity ACL that
// grants the current user read/write/instance ownership and
// denies everyone else. Pipe ACLs are the only mechanism we
// have to enforce same-user access on a Local\\ pipe.
// 2. Wait synchronously for a connection.
// 3. Read the HelloRequest, validate the protocol version, send
// HelloResponse (status 0 ok or 1 mismatch).
// 4. If ok, loop on ReadFrame: Ping -> Pong, Goodbye -> close.
// 5. Disconnect, dispose, repeat.
//
// The loop is cancellable: callers pass a CancellationToken (or
// the simpler Stop() method on the wrapper) to ask the server to
// drop out at the next safe point. Cancellation between Wait calls
// is detected by checking the token; cancellation during a blocking
// WaitForConnection is induced by disposing the underlying pipe
// from the cancellation callback.

using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

using FormCast.Ipc;

namespace FormCast.Host
{
 /// <summary>
 /// Single-threaded, single-client-at-a-time named pipe server
 /// for FormCast.Host. Supports handshake + Ping/Pong + Goodbye,
 /// with an optional idle-timeout that calls
 /// <see cref="Stop"/> from a background <see cref="Timer"/>
 /// when no client has been connected for the configured
 /// duration.
 /// </summary>
    internal sealed class PipeServer : IDisposable
    {
        private readonly string _pipeName;
        private readonly string _serverName;
        private readonly Action<string>? _log;
        private readonly TimeSpan _idleTimeout;
        private Timer? _idleTimer;
        private int _idleFired;
        private NamedPipeServerStream? _currentPipe;
        private int _running;
        private int _disposed;

 /// <summary>
 /// True after the idle-timeout timer has fired and the
 /// loop is on its way out. Visible for tests so they can
 /// distinguish "exited because idle" from "exited because
 /// the test called Stop()".
 /// </summary>
        public bool ExitedFromIdleTimeout => Volatile.Read(ref _idleFired) == 1;

 /// <summary>
 /// Total connections accepted since construction. Visible
 /// for tests so they can wait for a known number of clients
 /// to round-trip before tearing the server down.
 /// </summary>
        public int AcceptCount { get; private set; }

 /// <summary>
 /// Construct a server for the given pipe name. The
 /// <paramref name="log"/> callback is invoked on this
 /// thread for every state transition; pass <c>null</c> to
 /// silence. <paramref name="idleTimeout"/> is the  /// "exit when nobody is talking to us" interval; pass
 /// <see cref="TimeSpan.Zero"/> (the default) to disable
 /// idle-out and let the loop run until <see cref="Stop"/>
 /// is called.
 /// </summary>
        public PipeServer(
            string pipeName,
            string serverName,
            Action<string>? log = null,
            TimeSpan idleTimeout = default)
        {
            _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
            _serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));
            _log = log;
            _idleTimeout = idleTimeout;
        }

 /// <summary>
 /// Run the accept loop until <paramref name="cancel"/>
 /// fires or <see cref="Stop"/> is called. Blocks the
 /// calling thread.
 /// </summary>
        public void Run(CancellationToken cancel)
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(PipeServer));
            }
            Interlocked.Exchange(ref _running, 1);
            ResetIdleTimer();

            using CancellationTokenRegistration reg = cancel.Register(Stop);
            try
            {
                while (!cancel.IsCancellationRequested && Volatile.Read(ref _running) == 1)
                {
                    NamedPipeServerStream pipe;
                    try
                    {
                        pipe = CreatePipe();
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke("PipeServer: CreatePipe threw " + ex.GetType().Name + ": " + ex.Message);
                        return;
                    }

                    _currentPipe = pipe;
                    try
                    {
                        try
                        {
                            pipe.WaitForConnection();
                        }
                        catch (ObjectDisposedException)
                        {
 // Stop() disposed the pipe to break out
 // of WaitForConnection.
                            return;
                        }
                        catch (IOException) when (cancel.IsCancellationRequested)
                        {
                            return;
                        }

 // Stop() unblocks WaitForConnection by
 // self-connecting; that phantom accept must
 // NOT be counted as a real client. If _running
 // was cleared between WaitForConnection
 // returning and here, drop out without
 // counting or handling.
                        if (Volatile.Read(ref _running) == 0)
                        {
                            return;
                        }

                        AcceptCount++;
 // a real client is now connected; pause
 // the idle countdown until they disconnect.
                        DisableIdleTimer();
                        _log?.Invoke("PipeServer: client connected (#" + AcceptCount + ")");
                        try
                        {
                            HandleClient(pipe);
                        }
                        catch (Exception ex)
                        {
                            _log?.Invoke("PipeServer: client handler threw " +
                                ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                    finally
                    {
                        _currentPipe = null;
                        try { if (pipe.IsConnected) { pipe.Disconnect(); } } catch { /* swallow */ }
                        try { pipe.Dispose(); } catch { /* swallow */ }
 // client gone, restart idle countdown.
 // Skipped if Stop() already cleared _running.
                        if (Volatile.Read(ref _running) == 1)
                        {
                            ResetIdleTimer();
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
                DisableIdleTimer();
            }
        }

 // -----------------------------------------------------------------
 // idle-timeout machinery
 // -----------------------------------------------------------------

        private void ResetIdleTimer()
        {
            if (_idleTimeout <= TimeSpan.Zero) { return; }
            if (_idleTimer is null)
            {
                _idleTimer = new Timer(OnIdleTick, state: null,
                    dueTime: _idleTimeout, period: Timeout.InfiniteTimeSpan);
            }
            else
            {
                _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
            }
        }

        private void DisableIdleTimer()
        {
            if (_idleTimer is null) { return; }
            try { _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { /* swallow */ }
        }

        private void OnIdleTick(object? state)
        {
            if (Volatile.Read(ref _running) == 0) { return; }
            Interlocked.Exchange(ref _idleFired, 1);
            _log?.Invoke("PipeServer: idle timeout " +
                _idleTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "s reached; stopping");
            Stop();
        }

 /// <summary>
 /// Ask the accept loop to exit at the next safe point.
 /// Idempotent. Self-connects to the pipe once to unblock a
 /// thread parked in
 /// <see cref="NamedPipeServerStream.WaitForConnection"/>:
 /// the .NET Framework 4.x synchronous overload does not
 /// throw on Dispose from another thread, so disposing
 /// alone is not enough. The phantom client connects, sends
 /// nothing, and disconnects; the server's HandleClient
 /// reads zero bytes and returns, then the outer loop
 /// notices _running == 0 and exits.
 /// </summary>
        public void Stop()
        {
            if (Interlocked.Exchange(ref _running, 0) == 0) { return; }
            try
            {
                using var probe = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: _pipeName,
                    direction: PipeDirection.InOut);
                probe.Connect(timeout: 500);
            }
            catch (TimeoutException) { /* server may already be gone */ }
            catch (IOException) { /* same */ }
            catch (UnauthorizedAccessException) { /* same */ }
        }

 /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) { return; }
            Stop();
            try { _idleTimer?.Dispose(); } catch { /* swallow */ }
            _idleTimer = null;
        }

 // -----------------------------------------------------------------
 // Internal helpers
 // -----------------------------------------------------------------

        private NamedPipeServerStream CreatePipe()
        {
 // PipeSecurity: only the current user gets full
 // read/write/instance access. Anyone else is denied by
 // omission. The .NET Framework 4.x NamedPipeServerStream
 // ctor that takes a PipeSecurity is the documented
 // mechanism for setting an ACL at construction time.
            var sec = new PipeSecurity();
            SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
            if (user is not null)
            {
                sec.AddAccessRule(new PipeAccessRule(
                    user,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow));
            }

            return new NamedPipeServerStream(
                pipeName: _pipeName,
                direction: PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                transmissionMode: PipeTransmissionMode.Byte,
                options: PipeOptions.None,
                inBufferSize: 4096,
                outBufferSize: 4096,
                pipeSecurity: sec);
        }

        private void HandleClient(NamedPipeServerStream pipe)
        {
            byte[]? hello = PipeProtocol.ReadFrame(pipe);
            if (hello is null) { return; }
            (uint clientVersion, string clientName) = PipeProtocol.ParseHelloRequest(hello);

            byte status = clientVersion == PipeProtocol.ProtocolVersion
                ? PipeProtocol.HelloOk
                : PipeProtocol.HelloVersionMismatch;
            PipeProtocol.WriteFrame(pipe, PipeProtocol.BuildHelloResponse(status, _serverName));
            _log?.Invoke("PipeServer: handshake " +
                (status == PipeProtocol.HelloOk ? "ok" : "version mismatch") +
                " with '" + clientName + "' v" + clientVersion);

            if (status != PipeProtocol.HelloOk) { return; }

 // Message loop until Goodbye or peer disconnect.
            while (true)
            {
                byte[]? frame;
                try
                {
                    frame = PipeProtocol.ReadFrame(pipe);
                }
                catch (InvalidDataException ex)
                {
                    _log?.Invoke("PipeServer: malformed frame, dropping client: " + ex.Message);
                    return;
                }
                if (frame is null) { return; }
                if (frame.Length == 0) { return; }

                switch (frame[0])
                {
                    case PipeProtocol.MsgPing:
                        uint nonce = PipeProtocol.ParsePing(frame);
                        PipeProtocol.WriteFrame(pipe, PipeProtocol.BuildPong(nonce));
                        break;
                    case PipeProtocol.MsgGoodbye:
                        return;
                    default:
                        _log?.Invoke("PipeServer: unknown message type 0x" +
                            frame[0].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                        return;
                }
            }
        }
    }
}
