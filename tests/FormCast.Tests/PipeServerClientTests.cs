// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Threading;

using FormCast.Host;
using FormCast.Ipc;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// end-to-end tests: PipeServer running in a background
 /// thread inside the test process, HostClient connecting to it
 /// over a real named pipe. No subprocess involved -- the
 /// FormCast.Host.exe smoke test is a separate bridge job.
 ///
 /// Each test uses a unique pipe name so concurrent test runs do
 /// not collide. We do not need PipeServer instances to share an
 /// AcceptCount across tests.
 /// </summary>
 public class PipeServerClientTests
 {
 private static string UniquePipeName(string tag)
 {
 // Must start with PipeProtocol.PipeNamePrefix so the
 // client's BuildPipeName(sessionId) round-trips to the
 // same string when we hand it the suffix as a session id.
 return PipeProtocol.PipeNamePrefix + "test." + tag + "." +
 Guid.NewGuid().ToString("N");
 }

 private sealed class ServerHarness : IDisposable
 {
 public PipeServer Server { get; }
 public Thread Thread { get; }
 public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

 public ServerHarness(string pipeName)
 {
 Server = new PipeServer(pipeName, "TestServer");
 Thread = new Thread(() => Server.Run(Cts.Token))
 { IsBackground = true, Name = "PipeServerHarness" };
 Thread.Start();
 // Tiny sleep so the server has time to call
 // WaitForConnection before the client tries to
 // connect. The client also retries internally so
 // this is just an optimization, not a correctness
 // requirement.
 Thread.Sleep(50);
 }

 public void Dispose()
 {
 try { Cts.Cancel(); } catch { /* swallow */ }
 try { Server.Stop(); } catch { /* swallow */ }
 try { Server.Dispose(); } catch { /* swallow */ }
 Thread.Join(2000);
 Cts.Dispose();
 }
 }

 // -----------------------------------------------------------------
 // Happy path: connect, handshake, ping, goodbye
 // -----------------------------------------------------------------

 [Fact]
 public void Connect_handshake_succeeds_and_reports_server_name()
 {
 string pipeName = UniquePipeName("hs");
 using var harness = new ServerHarness(pipeName);

 using var client = new HostClient(SessionIdFromPipeName(pipeName), "TestClient");
 client.Connect();

 Assert.Equal("TestServer", client.ServerName);
 Assert.Equal(PipeProtocol.ProtocolVersion, client.ServerVersion);
 }

 [Fact]
 public void Ping_round_trips_nonce()
 {
 string pipeName = UniquePipeName("ping");
 using var harness = new ServerHarness(pipeName);

 using var client = new HostClient(SessionIdFromPipeName(pipeName), "TestClient");
 client.Connect();

 uint echoed = client.Ping(0xCAFEBABE);
 Assert.Equal(0xCAFEBABEu, echoed);
 }

 [Fact]
 public void Multiple_pings_on_one_connection_all_succeed()
 {
 string pipeName = UniquePipeName("multi");
 using var harness = new ServerHarness(pipeName);

 using var client = new HostClient(SessionIdFromPipeName(pipeName), "TestClient");
 client.Connect();

 for (uint i = 1; i <= 5; i++)
 {
 Assert.Equal(i, client.Ping(i));
 }
 }

 [Fact]
 public void Goodbye_then_dispose_does_not_throw()
 {
 string pipeName = UniquePipeName("bye");
 using var harness = new ServerHarness(pipeName);

 using var client = new HostClient(SessionIdFromPipeName(pipeName), "TestClient");
 client.Connect();
 client.Goodbye();
 // Dispose runs implicitly via using; no exception escapes.
 }

 // -----------------------------------------------------------------
 // Server lifecycle: AcceptCount increments per client
 // -----------------------------------------------------------------

 [Fact]
 public void Server_accepts_three_sequential_clients()
 {
 string pipeName = UniquePipeName("accept3");
 using var harness = new ServerHarness(pipeName);

 for (int i = 0; i < 3; i++)
 {
 using var client = new HostClient(
 SessionIdFromPipeName(pipeName), "TestClient" + i);
 client.Connect();
 client.Ping(42);
 }
 // Spin until the server has incremented through all
 // three (it accepts the next connection only after the
 // previous client closes, so this can lag).
 var sw = System.Diagnostics.Stopwatch.StartNew();
 while (sw.ElapsedMilliseconds < 2000 && harness.Server.AcceptCount < 3)
 {
 Thread.Sleep(20);
 }
 Assert.Equal(3, harness.Server.AcceptCount);
 }

 // -----------------------------------------------------------------
 // Connect to a non-existent pipe times out cleanly
 // -----------------------------------------------------------------

 [Fact]
 public void Connect_to_nonexistent_pipe_throws_IOException()
 {
 using var client = new HostClient("does-not-exist-" + Guid.NewGuid().ToString("N"),
 "TestClient");
 Assert.Throws<System.IO.IOException>(() => client.Connect(connectTimeoutMs: 200));
 }

 // -----------------------------------------------------------------
 // Stop() unblocks a server parked in WaitForConnection
 // -----------------------------------------------------------------

 [Fact]
 public void Stop_breaks_server_out_of_WaitForConnection()
 {
 string pipeName = UniquePipeName("stop");
 using var server = new PipeServer(pipeName, "TestServer");
 using var cts = new CancellationTokenSource();

 var t = new Thread(() => server.Run(cts.Token)) { IsBackground = true };
 t.Start();
 Thread.Sleep(50);

 server.Stop();
 Assert.True(t.Join(2000), "PipeServer thread did not exit after Stop()");
 }

 // -----------------------------------------------------------------
 // PipeProtocol.BuildPipeName + sessionId path agrees with
 // HostClient's expectation.
 // -----------------------------------------------------------------

 private static string SessionIdFromPipeName(string pipeName)
 {
 // ServerHarness uses the pipe name verbatim; we have to
 // give the client the matching sessionId so its
 // BuildPipeName produces the same string. Strip the
 // shared prefix.
 const string prefix = PipeProtocol.PipeNamePrefix;
 if (pipeName.StartsWith(prefix, StringComparison.Ordinal))
 {
 return pipeName.Substring(prefix.Length);
 }
 // Tests use UniquePipeName("tag") which produces
 // "FormCastTest.tag.<guid>" -- not prefixed with
 // PipeNamePrefix. We need to make the client target
 // exactly this name, but HostClient builds its name as
 // PipeNamePrefix + sessionId. So fall back to telling
 // the harness to USE PipeNamePrefix as its prefix when
 // we want the client to be able to reach it.
 throw new InvalidOperationException(
 "Test pipe names must start with PipeProtocol.PipeNamePrefix " +
 "for HostClient compatibility. Got: " + pipeName);
 }
 }
}
