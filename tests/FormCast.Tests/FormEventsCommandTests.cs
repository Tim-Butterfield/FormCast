// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Text;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the <c>FORMEVENTS</c> streaming command. Two
 /// flavors:
 /// <list type="bullet">
 /// <item><description>Direct calls into the test-only
 /// <c>Plugin.DrainEventLines</c> accessor that pin the drain
 /// semantics and the formatter integration.</description></item>
 /// <item><description>Calls into the public <c>FORMEVENTS</c>
 /// dispatch method itself, exercising the argument parser
 /// and the return-code surface.</description></item>
 /// </list>
 /// The dispatch method writes the formatted lines to <c>wwriteXP</c>
 /// which is unreachable in a headless test process; both flavors
 /// rely on <c>DrainEventLines</c> for content assertions and use
 /// the dispatch method only for parse / return-code coverage.
 /// </summary>
 public class FormEventsCommandTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormEventsCommandTests()
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

 private static int SeqOf(string handle) => int.Parse(handle.Split(':')[2]);

 private void AddControl(string handle, string id, string type,
 int x = 5, int y = 5, int w = 100, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 // -------- DrainEventLines: content semantics --------

 [Fact]
 public void Drain_with_no_events_returns_empty_list()
 {
 var lines = _plugin.DrainEventLines(handle: null);
 Assert.Empty(lines);
 }

 [Fact]
 public void Drain_specific_handle_returns_only_that_forms_lines()
 {
 string h1 = OpenForm("formA");
 AddControl(h1, "btnA", "BUTTON");
 string h2 = OpenForm("formB");
 AddControl(h2, "btnB", "BUTTON");
 int seq1 = SeqOf(h1);
 int seq2 = SeqOf(h2);

 _plugin.f_FORMSIMULATE(Buf($"{h1},btnA,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h2},btnB,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h2},btnB,click"));

 var lines = _plugin.DrainEventLines(seq1);
 Assert.Single(lines);
 Assert.Equal($"{seq1} click btnA", lines[0]);

 // The other form's queue is untouched.
 var leftover = _plugin.DrainEventLines(seq2);
 Assert.Equal(2, leftover.Count);
 Assert.All(leftover, l => Assert.StartsWith($"{seq2} click btnB", l));
 }

 [Fact]
 public void Drain_all_returns_lines_from_every_form()
 {
 string h1 = OpenForm("formA");
 AddControl(h1, "btnA", "BUTTON");
 string h2 = OpenForm("formB");
 AddControl(h2, "btnB", "BUTTON");

 _plugin.f_FORMSIMULATE(Buf($"{h1},btnA,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h2},btnB,click"));

 var lines = _plugin.DrainEventLines(handle: null);
 Assert.Equal(2, lines.Count);
 }

 [Fact]
 public void Drain_preserves_per_form_FIFO_order()
 {
 string h = OpenForm("multi");
 AddControl(h, "name", "EDIT", text: "");
 AddControl(h, "agree", "CHECKBOX");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},name,type,hello"));
 _plugin.f_FORMSIMULATE(Buf($"{h},agree,check"));
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 var lines = _plugin.DrainEventLines(seq);
 Assert.Equal(3, lines.Count);
 Assert.Equal($"{seq} change name hello", lines[0]);
 Assert.Equal($"{seq} change agree true", lines[1]);
 Assert.Equal($"{seq} click go", lines[2]);
 }

 [Fact]
 public void Drain_empties_the_queue()
 {
 string h = OpenForm("once");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 var first = _plugin.DrainEventLines(seq);
 Assert.Single(first);

 var second = _plugin.DrainEventLines(seq);
 Assert.Empty(second);
 }

 [Fact]
 public void Drain_unknown_handle_returns_empty_list()
 {
 var lines = _plugin.DrainEventLines(handle: 9999);
 Assert.Empty(lines);
 }

 [Fact]
 public void Drain_includes_close_event_after_FORMCLOSE()
 {
 string h = OpenForm("closing");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 // Realize the queue via a synthetic click, then drain
 // the click out so the close event lands as the only
 // remaining record. We have to capture the queue
 // reference indirectly via the test accessor since
 // FORMCLOSE removes it from the plugin's map.
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));
 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);
 queue!.DrainAll();

 _plugin.f_FORMCLOSE(Buf(h));

 // The plugin's map no longer holds this handle, so a
 // drain by handle returns empty. But the queue object
 // we captured before FORMCLOSE has the close record.
 Assert.Empty(_plugin.DrainEventLines(seq));
 var residual = queue!.DrainAll();
 Assert.Single(residual);
 Assert.Equal("close", residual[0].EventType);
 }

 // -------- FORMEVENTS dispatch surface --------

 [Fact]
 public void FORMEVENTS_with_no_args_returns_zero_and_drains_all()
 {
 string h = OpenForm("noargs");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 int rc = _plugin.FORMEVENTS(Buf(""));
 Assert.Equal(0, rc);

 // Drain ran, queue is now empty.
 var leftover = _plugin.DrainEventLines(seq);
 Assert.Empty(leftover);
 }

 [Fact]
 public void FORMEVENTS_with_quoted_empty_sentinel_drains_all()
 {
 string h = OpenForm("quoted");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 int rc = _plugin.FORMEVENTS(Buf("\"\""));
 Assert.Equal(0, rc);

 Assert.Empty(_plugin.DrainEventLines(seq));
 }

 [Fact]
 public void FORMEVENTS_with_handle_arg_returns_zero_and_drains_only_that_form()
 {
 string h1 = OpenForm("a");
 AddControl(h1, "btn", "BUTTON");
 string h2 = OpenForm("b");
 AddControl(h2, "btn", "BUTTON");
 int seq1 = SeqOf(h1);
 int seq2 = SeqOf(h2);

 _plugin.f_FORMSIMULATE(Buf($"{h1},btn,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h2},btn,click"));

 int rc = _plugin.FORMEVENTS(Buf(h1));
 Assert.Equal(0, rc);

 // form1 drained, form2 still has its event.
 Assert.Empty(_plugin.DrainEventLines(seq1));
 Assert.Single(_plugin.DrainEventLines(seq2));
 }

 [Fact]
 public void FORMEVENTS_with_unparseable_handle_returns_20100()
 {
 int rc = _plugin.FORMEVENTS(Buf("not-a-handle"));
 Assert.Equal(20100, rc);
 }

 [Fact]
 public void FORMEVENTS_with_unknown_handle_returns_20100()
 {
 // Well-formed handle that has never been realized.
 int rc = _plugin.FORMEVENTS(Buf("L:12345:99"));
 Assert.Equal(20100, rc);
 }

 [Fact]
 public void FORMEVENTS_with_too_many_args_returns_20101()
 {
 string h = OpenForm("too");
 AddControl(h, "go", "BUTTON");
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 int rc = _plugin.FORMEVENTS(Buf($"{h} local extra"));
 Assert.Equal(20101, rc);
 }

 [Fact]
 public void FORMEVENTS_with_handle_and_scope_arg_accepted_in_M5_3()
 {
 // Section 4.2 documents `FORMEVENTS [handle|"" [scope]]`.
 // accepts the scope token but ignores it; Phase 10
 // gives it meaning. The call must succeed (rc=0) so
 // forward-compatible BTM scripts work today.
 string h = OpenForm("withscope");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 int rc = _plugin.FORMEVENTS(Buf($"{h} local"));
 Assert.Equal(0, rc);
 Assert.Empty(_plugin.DrainEventLines(seq));
 }

 [Fact]
 public void FORMEVENTS_with_quoted_empty_and_scope_drains_all()
 {
 string h = OpenForm("scopeall");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 int rc = _plugin.FORMEVENTS(Buf("\"\" local"));
 Assert.Equal(0, rc);
 Assert.Empty(_plugin.DrainEventLines(seq));
 }

 [Fact]
 public void FORMEVENTS_clears_args_buffer()
 {
 // The dispatch contract is that the buffer is consumed.
 // For commands the return value is the rc, but the
 // buffer should still be cleared so the host doesn't
 // re-display the args back to the caller.
 var args = Buf("");
 int rc = _plugin.FORMEVENTS(args);
 Assert.Equal(0, rc);
 Assert.Equal(string.Empty, args.ToString());
 }
 }
}
