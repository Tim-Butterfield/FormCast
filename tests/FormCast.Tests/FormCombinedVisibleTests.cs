// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// combined synthetic + visible end-to-end test. Drives a real
 /// visible WinForms form through the full Phase 7+8 stack:
 /// open, FORMSHOW visible, FORMSIMULATE a sequence of events on
 /// multiple control types, then drain via FORMEVENTS (the
 /// DrainEventLines internal accessor) and assert each formatted
 /// line matches the expected handle/kind/ctrl/data shape.
 ///
 /// This is the milestone that validates "everything still works
 /// when the form is actually visible" -- wired up the
 /// expanded event surface in headless mode and wired up the
 /// events_pending bit; puts them together with real
 /// Form.Show() path.
 /// </summary>
 public class FormCombinedVisibleTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormCombinedVisibleTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 Assert.False(global::FormCast.HeadlessMode.IsEnabled);
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "combo", int w = 280, int h = 160)
 {
 var args = Buf($"form,{name},20,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private void AddControl(string handle, string id, string type,
 int x = 5, int y = 5, int w = 120, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 private static int SeqOf(string handle) =>
 int.Parse(handle.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);

 private void Simulate(string handle, string ctrl, string action, string? value = null)
 {
 var args = value is null
 ? Buf($"{handle},{ctrl},{action}")
 : Buf($"{handle},{ctrl},{action},{value}");
 _plugin.f_FORMSIMULATE(args);
 }

 // -----------------------------------------------------------------
 // Full visible form, multi-control event sequence, drain
 // -----------------------------------------------------------------

 [Fact]
 public void Visible_form_with_simulated_event_sequence_drains_in_order()
 {
 string h = OpenForm("everything", 320, 200);
 int seq = SeqOf(h);

 AddControl(h, "name", "EDIT", y: 5);
 AddControl(h, "agree", "CHECKBOX", y: 35);
 AddControl(h, "go", "BUTTON", y: 65);

 // Real Form.Show() via the visible path.
 var showArgs = Buf(h);
 _plugin.f_FORMSHOW(showArgs);
 Assert.Equal("0", showArgs.ToString());
 Assert.True(_plugin.IsRealized(seq));

 // Form.Show may auto-focus the first focusable control,
 // which would enqueue a "focus" record we did not ask
 // for. Allow a brief settle so the message loop processes
 // any deferred events from the show, then drain the
 // queue clean before the explicit synthetic sequence.
 Thread.Sleep(50);
 _ = _plugin.DrainEventLines(seq);

 // Drive a sequence of synthetic events through the now
 // visible form. Each one runs on the GUI thread via
 // host.Invoke and the resulting WinForms event handlers
 // enqueue records into the per-form FormEventQueue.
 Simulate(h, "name", "focus");
 Simulate(h, "name", "keypress", "h");
 Simulate(h, "name", "type", "ello"); // -> change "ello"
 Simulate(h, "name", "blur");
 Simulate(h, "agree", "check"); // -> change "true"
 Simulate(h, "go", "click");

 // Drain via the same code path the FORMEVENTS streaming
 // command uses. Filter to this form's handle so any
 // background queue from another test does not pollute the
 // assertion.
 IReadOnlyList<string> lines = _plugin.DrainEventLines(seq);

 // The Realizer wires:
 // focus -> "focus"
 // keypress on TextBox -> "keypress" with KeyChar
 // type on TextBox -> AppendText -> TextChanged -> "change"
 // blur -> "blur"
 // check on CheckBox -> Checked=true -> CheckedChanged -> "change" "true"
 // click on Button -> Click -> "click"
 // The expected order matches the simulate order:
 string[] expectedShape =
 {
 $"{seq} focus name",
 $"{seq} keypress name h",
 $"{seq} change name ello",
 $"{seq} blur name",
 $"{seq} change agree true",
 $"{seq} click go",
 };

 Assert.Equal(expectedShape.Length, lines.Count);
 for (int i = 0; i < expectedShape.Length; i++)
 {
 Assert.Equal(expectedShape[i], lines[i]);
 }

 // After the drain, events_pending bit (32) should be clear
 // because the queue is empty.
 var stateArgs = Buf(h);
 _plugin.f_FORMSTATE(stateArgs);
 int state = int.Parse(stateArgs.ToString(),
 System.Globalization.CultureInfo.InvariantCulture);
 Assert.Equal(0, state & 32);

 // Form is still realized (drain does not destroy).
 Assert.True(_plugin.IsRealized(seq));

 // Cleanup.
 _plugin.f_FORMCLOSE(Buf(h));
 }

 // -----------------------------------------------------------------
 // FORMEVENTS dispatch return code on the visible form path
 // -----------------------------------------------------------------

 [Fact]
 public void FORMEVENTS_handle_filtered_drain_returns_zero_on_visible_form()
 {
 string h = OpenForm("v2", 200, 100);
 AddControl(h, "btn", "BUTTON");

 _plugin.f_FORMSHOW(Buf(h));

 // Drain any auto-focus events from Show.
 Thread.Sleep(50);
 _ = _plugin.DrainEventLines(SeqOf(h));

 // Generate one event so the queue exists.
 Simulate(h, "btn", "click");

 // FORMEVENTS dispatch with handle filter; the streaming
 // path swallows the WriteStdOut DllNotFoundException in
 // the test process. Return code 0 means "drained
 // successfully".
 var args = Buf(h);
 int rc = _plugin.FORMEVENTS(args);
 Assert.Equal(0, rc);

 // Queue is now empty.
 int seq = SeqOf(h);
 Assert.Empty(_plugin.DrainEventLines(seq));

 _plugin.f_FORMCLOSE(Buf(h));
 }

 // -----------------------------------------------------------------
 // events_pending bit transitions through the visible form lifecycle
 // -----------------------------------------------------------------

 [Fact]
 public void Events_pending_bit_tracks_queue_through_show_and_drain()
 {
 string h = OpenForm("ep", 200, 100);
 int seq = SeqOf(h);
 AddControl(h, "btn", "BUTTON");

 _plugin.f_FORMSHOW(Buf(h));

 // Drain any auto-focus events that Show may have enqueued
 // before checking the bit transitions.
 Thread.Sleep(50);
 _ = _plugin.DrainEventLines(seq);

 // Initially clear.
 int s0 = ReadState(h);
 Assert.Equal(0, s0 & 32);
 Assert.NotEqual(0, s0 & 1); // visible

 // After two simulates: bit set.
 Simulate(h, "btn", "click");
 Simulate(h, "btn", "click");
 int s1 = ReadState(h);
 Assert.NotEqual(0, s1 & 32);
 Assert.NotEqual(0, s1 & 1);

 // Drain: bit clears.
 _ = _plugin.DrainEventLines(seq);
 int s2 = ReadState(h);
 Assert.Equal(0, s2 & 32);
 Assert.NotEqual(0, s2 & 1); // still visible

 _plugin.f_FORMCLOSE(Buf(h));
 }

 private int ReadState(string handle)
 {
 var args = Buf(handle);
 _plugin.f_FORMSTATE(args);
 return int.Parse(args.ToString(), System.Globalization.CultureInfo.InvariantCulture);
 }
 }
}
