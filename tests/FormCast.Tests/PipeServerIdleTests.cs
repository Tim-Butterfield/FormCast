// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Diagnostics;
using System.Threading;

using FormCast.Host;
using FormCast.Ipc;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the PipeServer idle-timeout. The server is
 /// configured with a short idle interval (200-500 ms) so each
 /// test runs in a couple of seconds. We assert that the loop
 /// exits, that ExitedFromIdleTimeout is set, that the idle
 /// timer pauses while a client is connected, and that
 /// idleTimeout = TimeSpan.Zero leaves the server running until
 /// Stop().
 /// </summary>
 public class PipeServerIdleTests
 {
 private static string UniquePipeName(string tag)
 {
 return PipeProtocol.PipeNamePrefix + "idle." + tag + "." +
 Guid.NewGuid().ToString("N");
 }

 // -----------------------------------------------------------------
 // Idle exit with no client connections
 // -----------------------------------------------------------------

 [Fact]
 public void Server_with_idle_timeout_exits_when_no_client_connects()
 {
 string pipeName = UniquePipeName("noclient");
 using var server = new PipeServer(
 pipeName, "TestServer", log: null,
 idleTimeout: TimeSpan.FromMilliseconds(200));
 using var cts = new CancellationTokenSource();

 var sw = Stopwatch.StartNew();
 var t = new Thread(() => server.Run(cts.Token)) { IsBackground = true };
 t.Start();

 Assert.True(t.Join(3000),
 "PipeServer thread did not exit within 3s of a 200ms idle timeout.");
 sw.Stop();

 Assert.True(server.ExitedFromIdleTimeout,
 "Server exited but ExitedFromIdleTimeout flag was not set.");
 Assert.Equal(0, server.AcceptCount);
 // Sanity bound: 200ms timeout should fire well within 3s.
 Assert.True(sw.ElapsedMilliseconds < 3000);
 }

 // -----------------------------------------------------------------
 // Client connect resets the idle timer; after disconnect the
 // timer starts counting again.
 // -----------------------------------------------------------------

 [Fact]
 public void Idle_timer_pauses_while_client_is_connected_and_resumes_after()
 {
 string pipeName = UniquePipeName("pause");
 using var server = new PipeServer(
 pipeName, "TestServer", log: null,
 idleTimeout: TimeSpan.FromMilliseconds(300));
 using var cts = new CancellationTokenSource();

 var t = new Thread(() => server.Run(cts.Token)) { IsBackground = true };
 t.Start();
 Thread.Sleep(50);

 // Connect, hold the connection past the idle timeout
 // duration, then disconnect. If the idle timer fired
 // during the connection, the server would already be
 // gone and the second client would not connect.
 string sessionId = pipeName.Substring(PipeProtocol.PipeNamePrefix.Length);
 using (var client = new HostClient(sessionId, "Tester"))
 {
 client.Connect();
 Thread.Sleep(500); // > 300ms idle window
 Assert.Equal(42u, client.Ping(42));
 client.Goodbye();
 }

 // After disconnect the idle timer should run and the
 // server should exit within idleTimeout + slack.
 Assert.True(t.Join(2000),
 "PipeServer did not exit within 2s after the client disconnected.");
 Assert.True(server.ExitedFromIdleTimeout);
 Assert.Equal(1, server.AcceptCount);
 }

 // -----------------------------------------------------------------
 // Idle timeout = Zero never fires.
 // -----------------------------------------------------------------

 [Fact]
 public void Server_with_zero_idle_timeout_does_not_exit_on_its_own()
 {
 string pipeName = UniquePipeName("zero");
 using var server = new PipeServer(
 pipeName, "TestServer", log: null,
 idleTimeout: TimeSpan.Zero);
 using var cts = new CancellationTokenSource();

 var t = new Thread(() => server.Run(cts.Token)) { IsBackground = true };
 t.Start();

 // Wait long enough that any half-baked idle timer
 // would have fired by now.
 Thread.Sleep(400);
 Assert.True(t.IsAlive,
 "Server with zero idle timeout exited unexpectedly.");
 Assert.False(server.ExitedFromIdleTimeout);

 // Now stop it explicitly.
 server.Stop();
 Assert.True(t.Join(2000));
 }

 // -----------------------------------------------------------------
 // Two sequential clients each reset the idle timer.
 // -----------------------------------------------------------------

 [Fact]
 public void Two_sequential_clients_keep_server_alive_until_final_idle_window()
 {
 string pipeName = UniquePipeName("two");
 using var server = new PipeServer(
 pipeName, "TestServer", log: null,
 idleTimeout: TimeSpan.FromMilliseconds(300));
 using var cts = new CancellationTokenSource();

 var t = new Thread(() => server.Run(cts.Token)) { IsBackground = true };
 t.Start();
 Thread.Sleep(50);

 string sessionId = pipeName.Substring(PipeProtocol.PipeNamePrefix.Length);
 for (int i = 0; i < 2; i++)
 {
 using var client = new HostClient(sessionId, "Tester" + i);
 client.Connect();
 client.Ping(7);
 client.Goodbye();
 // Sleep less than the idle window so the next
 // client gets in before the timer fires.
 Thread.Sleep(100);
 }

 Assert.True(t.Join(2000));
 Assert.True(server.ExitedFromIdleTimeout);
 Assert.Equal(2, server.AcceptCount);
 }
 }
}
