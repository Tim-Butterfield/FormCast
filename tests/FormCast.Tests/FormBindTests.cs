// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Tests for <c>@FORMBIND</c> in declarative event handler
 /// registration plus the worker-thread dispatch path that makes
 /// callback re-entrancy safe (PLUGIN_DESIGN.md section 7 #8).
 /// The dispatch surface is validated for argument shapes and
 /// error codes; the binding registry is validated through the
 /// internal <c>BindingCount</c> accessor; and the actual dispatch
 /// path (event fires -> hook resolves binding -> worker runs the
 /// bound command) is validated by routing the bound command
 /// through the <c>TestCommandHook</c> seam so the test can
 /// observe what would have been passed to <c>TakeCmd.Command</c>.
 /// </summary>
 public class FormBindTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormBindTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "test", int w = 300, int h = 200)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private void AddControl(string handle, string id, string type,
 int x = 5, int y = 5, int w = 100, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 private string Bind(string handle, string ctrl, string evt, string command)
 {
 var args = Buf($"{handle},{ctrl},{evt},{command}");
 _plugin.f_FORMBIND(args);
 return args.ToString();
 }

 private string Simulate(string handle, string ctrl, string action, string? value = null)
 {
 var args = value is null
 ? Buf($"{handle},{ctrl},{action}")
 : Buf($"{handle},{ctrl},{action},{value}");
 _plugin.f_FORMSIMULATE(args);
 return args.ToString();
 }

 // -----------------------------------------------------------------
 // Validation paths
 // -----------------------------------------------------------------

 [Fact]
 public void FORMBIND_wrong_arg_count_returns_bad_args()
 {
 var args = Buf("L:1:1,btn,click"); // 3 args, need 4
 _plugin.f_FORMBIND(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMBIND_unparseable_handle_returns_invalid_handle()
 {
 var args = Buf("not-a-handle,btn,click,echo hi");
 _plugin.f_FORMBIND(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMBIND_unknown_handle_returns_invalid_handle()
 {
 var args = Buf("L:99999:99,btn,click,echo hi");
 _plugin.f_FORMBIND(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMBIND_unknown_control_returns_unknown_control_id()
 {
 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 string rc = Bind(h, "missing", "click", "echo hi");
 Assert.Equal("20103", rc);
 }

 [Fact]
 public void FORMBIND_empty_event_returns_bad_args()
 {
 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 string rc = Bind(h, "go", "", "echo hi");
 Assert.Equal("20101", rc);
 }

 [Fact]
 public void FORMBIND_form_level_empty_ctrl_succeeds()
 {
 string h = OpenForm();
 string rc = Bind(h, "", "close", "echo bye");
 Assert.Equal("0", rc);
 Assert.Equal(1, _plugin.BindingCount);
 }

 [Fact]
 public void FORMBIND_rebind_replaces_command_without_growing_registry()
 {
 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 Assert.Equal("0", Bind(h, "go", "click", "echo first"));
 Assert.Equal("0", Bind(h, "go", "click", "echo second"));
 Assert.Equal(1, _plugin.BindingCount);
 }

 [Fact]
 public void FORMBIND_empty_command_unbinds()
 {
 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 Bind(h, "go", "click", "echo hi");
 Assert.Equal(1, _plugin.BindingCount);
 Assert.Equal("0", Bind(h, "go", "click", ""));
 Assert.Equal(0, _plugin.BindingCount);
 }

 [Fact]
 public void FORMBIND_event_name_is_case_insensitive()
 {
 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 Assert.Equal("0", Bind(h, "go", "CLICK", "echo hi"));
 // Re-bind with lowercase: should overwrite, not add.
 Assert.Equal("0", Bind(h, "go", "click", "echo bye"));
 Assert.Equal(1, _plugin.BindingCount);
 }

 [Fact]
 public void FORMBIND_control_id_is_case_insensitive()
 {
 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 Assert.Equal("0", Bind(h, "GO", "click", "echo hi"));
 Assert.Equal("0", Bind(h, "go", "click", "echo bye"));
 Assert.Equal(1, _plugin.BindingCount);
 }

 // -----------------------------------------------------------------
 // Lifecycle: FORMCLOSE purges, Shutdown clears
 // -----------------------------------------------------------------

 [Fact]
 public void FORMCLOSE_purges_bindings_for_that_handle_only()
 {
 string ha = OpenForm("alpha");
 string hb = OpenForm("bravo");
 AddControl(ha, "go", "BUTTON");
 AddControl(hb, "go", "BUTTON");
 Bind(ha, "go", "click", "echo a");
 Bind(hb, "go", "click", "echo b");
 Bind(ha, "", "close", "echo a-close");
 Assert.Equal(3, _plugin.BindingCount);

 var closeArgs = Buf(ha);
 _plugin.f_FORMCLOSE(closeArgs);
 Assert.Equal("0", closeArgs.ToString());

 // Form B's binding survives; form A's two are gone.
 Assert.Equal(1, _plugin.BindingCount);
 }

 // -----------------------------------------------------------------
 // Dispatch: simulate fires the bound command via the worker
 // -----------------------------------------------------------------

 /// <summary>
 /// Wait helper: blocks the test thread until the worker has
 /// invoked the test hook the expected number of times, or
 /// the timeout elapses. Worker dispatch is asynchronous so
 /// the test cannot inspect captured commands inline.
 /// </summary>
 private static void WaitForCount(Func<int> getCount, int expected, int timeoutMs = 2000)
 {
 var sw = System.Diagnostics.Stopwatch.StartNew();
 while (sw.ElapsedMilliseconds < timeoutMs)
 {
 if (getCount() >= expected) { return; }
 Thread.Sleep(5);
 }
 Assert.True(getCount() >= expected,
 $"Timed out waiting for command count to reach {expected}; got {getCount()}.");
 }

 [Fact]
 public void Bound_button_click_dispatches_command_to_worker_thread()
 {
 var captured = new ConcurrentQueue<string>();
 int testThreadId = Environment.CurrentManagedThreadId;
 int workerThreadId = -1;
 _plugin.TestCommandHook = cmd =>
 {
 workerThreadId = Environment.CurrentManagedThreadId;
 captured.Enqueue(cmd);
 };

 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 Assert.Equal("0", Bind(h, "go", "click", "gosub :on_ok"));

 Assert.Equal("0", Simulate(h, "go", "click"));

 WaitForCount(() => captured.Count, 1);
 Assert.Single(captured);
 Assert.True(captured.TryDequeue(out string? cmd));
 Assert.Equal("gosub :on_ok", cmd);

 // Re-entrancy contract: bound command MUST run on a thread
 // that is neither the test (script) thread nor the GUI
 // host thread. PLUGIN_DESIGN.md section 7 #8.
 Assert.NotEqual(testThreadId, workerThreadId);
 Assert.NotEqual(_plugin.GuiHost.GuiThreadId, workerThreadId);
 }

 [Fact]
 public void Bound_checkbox_change_dispatches_command()
 {
 var captured = new ConcurrentQueue<string>();
 _plugin.TestCommandHook = cmd => captured.Enqueue(cmd);

 string h = OpenForm();
 AddControl(h, "agree", "CHECKBOX");
 Bind(h, "agree", "change", "gosub :on_change");

 Simulate(h, "agree", "check");

 WaitForCount(() => captured.Count, 1);
 Assert.Single(captured);
 }

 [Fact]
 public void Multiple_events_serialize_through_worker_in_order()
 {
 var captured = new ConcurrentQueue<string>();
 _plugin.TestCommandHook = cmd => captured.Enqueue(cmd);

 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 Bind(h, "go", "click", "echo one");

 Simulate(h, "go", "click");
 Simulate(h, "go", "click");
 Simulate(h, "go", "click");

 WaitForCount(() => captured.Count, 3);
 Assert.Equal(3, captured.Count);
 foreach (string c in captured)
 {
 Assert.Equal("echo one", c);
 }
 }

 [Fact]
 public void Unbound_event_does_not_invoke_hook()
 {
 int hookCalls = 0;
 _plugin.TestCommandHook = _ => Interlocked.Increment(ref hookCalls);

 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 // No bind.
 Simulate(h, "go", "click");

 // Give the worker a moment in case dispatch is in flight.
 Thread.Sleep(50);
 Assert.Equal(0, hookCalls);
 }

 [Fact]
 public void Form_level_close_binding_fires_on_FORMCLOSE()
 {
 var captured = new ConcurrentQueue<string>();
 _plugin.TestCommandHook = cmd => captured.Enqueue(cmd);

 string h = OpenForm();
 AddControl(h, "go", "BUTTON");
 // The form must be realized for the close-event hook to
 // exist on its queue. Forcing realization via simulate is
 // the cheapest path here (it also exercises the eager
 // queue allocation in GetOrRealize).
 Simulate(h, "go", "click");
 // The above also fires a click binding if any -- but we
 // didn't bind one, so the captured queue stays empty.
 Thread.Sleep(20);
 captured = new ConcurrentQueue<string>();

 Bind(h, "", "close", "gosub :on_close");

 var closeArgs = Buf(h);
 _plugin.f_FORMCLOSE(closeArgs);
 Assert.Equal("0", closeArgs.ToString());

 WaitForCount(() => captured.Count, 1);
 Assert.True(captured.TryDequeue(out string? cmd));
 Assert.Equal("gosub :on_close", cmd);
 }

 // -----------------------------------------------------------------
 // Re-entrancy: bound command calls back into Plugin
 // -----------------------------------------------------------------

 [Fact]
 public void Bound_command_can_call_back_into_FORMSTATE_without_deadlock()
 {
 // Re-entrancy proof: the bound action runs on the worker
 // thread; calling f_FORMSTATE from there does NOT block on
 // the GUI thread (FORMSTATE is a pure registry read), so
 // the call returns the expected value with no deadlock.
 var done = new ManualResetEventSlim(false);
 string? observedState = null;
 Exception? capturedException = null;

 _plugin.TestCommandHook = _ =>
 {
 try
 {
 var stateArgs = Buf("L:1:1"); // placeholder, replaced below
 // We need the actual handle inside the closure;
 // capture it via a field instead.
 stateArgs = Buf(_pendingHandle!);
 _plugin.f_FORMSTATE(stateArgs);
 observedState = stateArgs.ToString();
 }
 catch (Exception ex)
 {
 capturedException = ex;
 }
 finally
 {
 done.Set();
 }
 };

 string h = OpenForm();
 _pendingHandle = h;
 AddControl(h, "go", "BUTTON");
 Bind(h, "go", "click", "irrelevant -- routed via test hook");

 Simulate(h, "go", "click");

 Assert.True(done.Wait(2000), "Bound action did not complete within 2s");
 Assert.Null(capturedException);
 // FORMSTATE = 2 (Enabled) | 32 (events_pending) = 34.
 // The click event that fired this binding is still in the
 // form's event queue at the moment the bound action runs:
 // DispatchBinding hooks Enqueue synchronously, so the queue
 // already contains the event when the worker thread reads
 // FORMSTATE here. Nobody has drained via FORMEVENTS yet.
 Assert.Equal("34", observedState);
 }

 [Fact]
 public void Bound_command_can_call_back_into_FORMSIMULATE_without_deadlock()
 {
 // Stronger re-entrancy proof: the bound action calls
 // f_FORMSIMULATE, which marshals to the GUI thread via
 // host.Invoke. Because the bound action is on the worker
 // thread (not the GUI thread), the GUI thread is free to
 // service the Invoke and the call returns cleanly.
 var done = new ManualResetEventSlim(false);
 int innerSimulateRc = -1;
 Exception? capturedException = null;

 _plugin.TestCommandHook = _ =>
 {
 try
 {
 // Fire a settext on the EDIT control from inside
 // the bound action.
 var simArgs = Buf($"{_pendingHandle},edit,settext,from-bound");
 innerSimulateRc = _plugin.f_FORMSIMULATE(simArgs);
 // simulate result-code is in the buffer; rc above
 // is always 0 by convention. Read the buffer:
 string buf = simArgs.ToString();
 if (buf != "0") { throw new InvalidOperationException("inner simulate buffer = " + buf); }
 }
 catch (Exception ex)
 {
 capturedException = ex;
 }
 finally
 {
 done.Set();
 }
 };

 string h = OpenForm();
 _pendingHandle = h;
 AddControl(h, "edit", "EDIT");
 AddControl(h, "go", "BUTTON");
 Bind(h, "go", "click", "irrelevant");

 Simulate(h, "go", "click");

 Assert.True(done.Wait(2000), "Re-entrant inner simulate did not complete within 2s");
 Assert.Null(capturedException);
 Assert.Equal(0, innerSimulateRc);
 }

 // Captured between OpenForm and the test hook so the closure
 // can reference the handle string. xUnit creates a fresh
 // FormBindTests instance per test so cross-test bleed is
 // impossible.
 private string? _pendingHandle;
 }
}
