// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Threading;
using System.Windows.Forms;

using FormCast.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Tests for <see cref="GuiHostThread"/>: STA bring-up, marshaling,
 /// re-entrancy, exception isolation, lifecycle, and the
 /// load/unload/reload cycle that Plugin lifecycle exercises in real
 /// use.
 /// </summary>
 public class GuiHostThreadTests
 {
 private static GuiHostThread NewStarted()
 {
 var g = new GuiHostThread();
 g.Start();
 return g;
 }

 // ---- Apartment / threading ----

 [Fact]
 public void Gui_thread_is_STA()
 {
 using var g = NewStarted();
 ApartmentState? observed = null;
 g.Invoke(() => observed = Thread.CurrentThread.GetApartmentState());
 Assert.Equal(ApartmentState.STA, observed);
 }

 [Fact]
 public void Gui_thread_is_a_distinct_thread()
 {
 using var g = NewStarted();
 int callerId = Thread.CurrentThread.ManagedThreadId;
 int observed = -1;
 g.Invoke(() => observed = Thread.CurrentThread.ManagedThreadId);
 Assert.NotEqual(callerId, observed);
 Assert.Equal(g.GuiThreadId, observed);
 }

 [Fact]
 public void IsRunning_is_true_after_Start()
 {
 using var g = NewStarted();
 Assert.True(g.IsRunning);
 }

 [Fact]
 public void IsRunning_is_false_before_Start()
 {
 using var g = new GuiHostThread();
 Assert.False(g.IsRunning);
 }

 // ---- Marshaling ----

 [Fact]
 public void Invoke_runs_action_on_gui_thread()
 {
 using var g = NewStarted();
 int observed = -1;
 g.Invoke(() => observed = Thread.CurrentThread.ManagedThreadId);
 Assert.Equal(g.GuiThreadId, observed);
 }

 [Fact]
 public void Invoke_from_gui_thread_runs_inline_no_deadlock()
 {
 using var g = NewStarted();
 bool nestedRan = false;
 g.Invoke(() =>
 {
 // We're now on the gui thread. Calling Invoke again
 // would deadlock if it routed through the message
 // pump; the re-entrancy guard must short-circuit.
 g.Invoke(() => nestedRan = true);
 });
 Assert.True(nestedRan);
 }

 [Fact]
 public void Invoke_rethrows_action_exception_wrapped()
 {
 using var g = NewStarted();
 var ex = Assert.Throws<GuiHostInvocationException>(() =>
 g.Invoke(() => throw new InvalidOperationException("boom")));
 Assert.IsType<InvalidOperationException>(ex.InnerException);
 Assert.Equal("boom", ex.InnerException!.Message);
 }

 [Fact]
 public void BeginInvoke_runs_async_on_gui_thread()
 {
 using var g = NewStarted();
 int observed = -1;
 using var done = new ManualResetEventSlim(false);
 g.BeginInvoke(() =>
 {
 observed = Thread.CurrentThread.ManagedThreadId;
 done.Set();
 });
 Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
 Assert.Equal(g.GuiThreadId, observed);
 }

 [Fact]
 public void BeginInvoke_action_exception_surfaces_via_event()
 {
 using var g = NewStarted();
 Exception? captured = null;
 using var done = new ManualResetEventSlim(false);
 g.UnhandledException += (s, e) =>
 {
 captured = e.Exception;
 done.Set();
 };
 g.BeginInvoke(() => throw new InvalidOperationException("async-boom"));
 Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
 Assert.IsType<InvalidOperationException>(captured);
 Assert.Equal("async-boom", captured!.Message);
 // The gui thread must still be alive after a faulted action.
 Assert.True(g.IsRunning);
 int probe = -1;
 g.Invoke(() => probe = 42);
 Assert.Equal(42, probe);
 }

 // ---- Lifecycle ----

 [Fact]
 public void Start_is_idempotent()
 {
 using var g = new GuiHostThread();
 g.Start();
 int firstId = g.GuiThreadId ?? -1;
 g.Start();
 int secondId = g.GuiThreadId ?? -1;
 Assert.Equal(firstId, secondId);
 Assert.True(g.IsRunning);
 }

 [Fact]
 public void Stop_joins_cleanly()
 {
 var g = new GuiHostThread();
 g.Start();
 Assert.True(g.IsRunning);
 bool joined = g.Stop();
 Assert.True(joined);
 Assert.False(g.IsRunning);
 g.Dispose();
 }

 [Fact]
 public void Stop_is_idempotent()
 {
 var g = new GuiHostThread();
 g.Start();
 Assert.True(g.Stop());
 Assert.True(g.Stop());
 Assert.True(g.Stop());
 g.Dispose();
 }

 [Fact]
 public void Dispose_is_idempotent()
 {
 var g = new GuiHostThread();
 g.Start();
 g.Dispose();
 g.Dispose();
 g.Dispose();
 }

 [Fact]
 public void Invoke_after_Stop_throws()
 {
 var g = new GuiHostThread();
 g.Start();
 g.Stop();
 Assert.Throws<InvalidOperationException>(() => g.Invoke(() => { }));
 g.Dispose();
 }

 [Fact]
 public void Invoke_before_Start_throws()
 {
 using var g = new GuiHostThread();
 Assert.Throws<InvalidOperationException>(() => g.Invoke(() => { }));
 }

 [Fact]
 public void BeginInvoke_null_throws_ArgumentNull()
 {
 using var g = NewStarted();
 Assert.Throws<ArgumentNullException>(() => g.BeginInvoke(null!));
 }

 [Fact]
 public void Invoke_null_throws_ArgumentNull()
 {
 using var g = NewStarted();
 Assert.Throws<ArgumentNullException>(() => g.Invoke(null!));
 }

 // ---- Forced shutdown sentinel ----

 [Fact]
 public void ForcedShutdown_defaults_to_false()
 {
 using var g = NewStarted();
 Assert.False(g.ForcedShutdown);
 }

 [Fact]
 public void SetForcedShutdown_flips_flag()
 {
 using var g = NewStarted();
 g.SetForcedShutdown();
 Assert.True(g.ForcedShutdown);
 }

 // ---- Load / unload / reload cycle ----
 //
 // This is the load-bearing test for a single host process
 // can spin up a fresh GuiHostThread, tear it down, and spin up
 // another one in sequence with no leaked state. The Plugin
 // lifecycle does this naturally on every plugin /l plugin /u
 // plugin /l sequence.

 [Fact]
 public void Load_unload_reload_cycle_three_times()
 {
 int? firstId = null;
 int? secondId = null;
 int? thirdId = null;

 using (var g = new GuiHostThread())
 {
 g.Start();
 firstId = g.GuiThreadId;
 int probe = 0;
 g.Invoke(() => probe = 1);
 Assert.Equal(1, probe);
 Assert.True(g.Stop());
 }

 using (var g = new GuiHostThread())
 {
 g.Start();
 secondId = g.GuiThreadId;
 int probe = 0;
 g.Invoke(() => probe = 2);
 Assert.Equal(2, probe);
 Assert.True(g.Stop());
 }

 using (var g = new GuiHostThread())
 {
 g.Start();
 thirdId = g.GuiThreadId;
 int probe = 0;
 g.Invoke(() => probe = 3);
 Assert.Equal(3, probe);
 Assert.True(g.Stop());
 }

 // Each cycle gets its own thread.
 Assert.NotNull(firstId);
 Assert.NotNull(secondId);
 Assert.NotNull(thirdId);
 Assert.NotEqual(firstId, secondId);
 Assert.NotEqual(secondId, thirdId);
 Assert.NotEqual(firstId, thirdId);
 }

 [Fact]
 public void WinForms_control_can_be_created_on_gui_thread()
 {
 // Sanity check that the message loop is real enough to host
 // a WinForms Control end-to-end. We never show it.
 using var g = NewStarted();
 int handleThreadId = -1;
 g.Invoke(() =>
 {
 using var f = new Form();
 _ = f.Handle; // force handle creation
 handleThreadId = Thread.CurrentThread.ManagedThreadId;
 Assert.True(f.IsHandleCreated);
 });
 Assert.Equal(g.GuiThreadId, handleThreadId);
 }
 }
}
