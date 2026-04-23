// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Linq;
using System.Text;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// integration tests: events fired by <c>@FORMSIMULATE</c>
 /// flow through the WinForms handlers wired by FormRealizer and
 /// land in the per-form event queue. The dispatch surface that
 /// drains the queue (FORMEVENTS streaming command) lands ;
 /// these tests inspect the queue directly via the test-only
 /// <c>Plugin.TryGetEventQueue</c> accessor.
 /// </summary>
 public class FormEventCaptureTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormEventCaptureTests()
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

 private static int SeqOf(string handle)
 {
 return int.Parse(handle.Split(':')[2]);
 }

 private void AddControl(string handle, string id, string type,
 int x = 5, int y = 5, int w = 100, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 // -------- Per-control-type capture --------

 [Fact]
 public void Click_on_button_pushes_click_event()
 {
 string h = OpenForm("buttonform");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 FormEventQueue? q = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(q);
 var events = q!.DrainAll();
 Assert.Single(events);
 Assert.Equal(seq, events[0].FormHandle);
 Assert.Equal("go", events[0].ControlId);
 Assert.Equal("click", events[0].EventType);
 }

 [Fact]
 public void Type_on_textbox_pushes_change_event_with_new_text()
 {
 string h = OpenForm("textform");
 AddControl(h, "name", "EDIT", text: "abc");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},name,type,def"));

 var events = _plugin.TryGetEventQueue(seq)!.DrainAll();
 // AppendText raises TextChanged once with the new value.
 Assert.Single(events);
 Assert.Equal("name", events[0].ControlId);
 Assert.Equal("change", events[0].EventType);
 Assert.Equal("abcdef", events[0].Value);
 }

 [Fact]
 public void Check_on_checkbox_pushes_change_event_true()
 {
 string h = OpenForm("checkform");
 AddControl(h, "agree", "CHECKBOX");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},agree,check"));

 var events = _plugin.TryGetEventQueue(seq)!.DrainAll();
 Assert.Single(events);
 Assert.Equal("change", events[0].EventType);
 Assert.Equal("true", events[0].Value);
 }

 [Fact]
 public void Uncheck_on_already_checked_checkbox_pushes_change_event_false()
 {
 string h = OpenForm("uncheckform");
 AddControl(h, "agree", "CHECKBOX");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},agree,check"));
 _plugin.f_FORMSIMULATE(Buf($"{h},agree,uncheck"));

 var events = _plugin.TryGetEventQueue(seq)!.DrainAll();
 Assert.Equal(2, events.Count);
 Assert.Equal("true", events[0].Value);
 Assert.Equal("false", events[1].Value);
 }

 [Fact]
 public void Check_on_radio_pushes_change_event_true()
 {
 string h = OpenForm("radioform");
 AddControl(h, "opt", "RADIO");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},opt,check"));

 var events = _plugin.TryGetEventQueue(seq)!.DrainAll();
 Assert.Single(events);
 Assert.Equal("change", events[0].EventType);
 Assert.Equal("true", events[0].Value);
 }

 [Fact]
 public void Settext_on_label_pushes_change_event_with_new_text()
 {
 string h = OpenForm("labelform");
 AddControl(h, "lbl", "LABEL", text: "before");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},lbl,settext,after"));

 var events = _plugin.TryGetEventQueue(seq)!.DrainAll();
 Assert.Single(events);
 Assert.Equal("change", events[0].EventType);
 Assert.Equal("after", events[0].Value);
 }

 [Fact]
 public void Click_on_checkbox_via_simulate_pushes_change_event()
 {
 // The reflection-based OnClick path raises CheckBox.OnClick,
 // which auto-toggles Checked, which fires CheckedChanged,
 // which our wiring captures as a change event.
 string h = OpenForm("clickform");
 AddControl(h, "agree", "CHECKBOX");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},agree,click"));

 var events = _plugin.TryGetEventQueue(seq)!.DrainAll();
 Assert.Single(events);
 Assert.Equal("change", events[0].EventType);
 Assert.Equal("true", events[0].Value);
 }

 // -------- Multi-event ordering --------

 [Fact]
 public void Multiple_events_arrive_in_FIFO_order()
 {
 string h = OpenForm("multi");
 AddControl(h, "name", "EDIT", text: "");
 AddControl(h, "agree", "CHECKBOX");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},name,type,hello"));
 _plugin.f_FORMSIMULATE(Buf($"{h},agree,check"));
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));

 var events = _plugin.TryGetEventQueue(seq)!.DrainAll();
 Assert.Equal(3, events.Count);
 Assert.Equal("name", events[0].ControlId);
 Assert.Equal("change", events[0].EventType);
 Assert.Equal("agree", events[1].ControlId);
 Assert.Equal("change", events[1].EventType);
 Assert.Equal("go", events[2].ControlId);
 Assert.Equal("click", events[2].EventType);
 }

 // -------- Cross-form independence --------

 [Fact]
 public void Two_forms_have_independent_event_queues()
 {
 string h1 = OpenForm("formA");
 AddControl(h1, "btnA", "BUTTON");
 string h2 = OpenForm("formB");
 AddControl(h2, "btnB", "BUTTON");
 int seq1 = SeqOf(h1);
 int seq2 = SeqOf(h2);

 _plugin.f_FORMSIMULATE(Buf($"{h1},btnA,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h1},btnA,click"));
 _plugin.f_FORMSIMULATE(Buf($"{h2},btnB,click"));

 var q1 = _plugin.TryGetEventQueue(seq1)!.DrainAll();
 var q2 = _plugin.TryGetEventQueue(seq2)!.DrainAll();

 Assert.Equal(2, q1.Count);
 Assert.Equal(1, q2.Count);
 Assert.All(q1, e => Assert.Equal(seq1, e.FormHandle));
 Assert.Equal(seq2, q2[0].FormHandle);
 }

 // -------- Lifecycle --------

 [Fact]
 public void Queue_is_created_lazily_on_first_simulate()
 {
 string h = OpenForm("lazyform");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 // Before any realize / simulate, no queue exists.
 Assert.Null(_plugin.TryGetEventQueue(seq));

 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));
 Assert.NotNull(_plugin.TryGetEventQueue(seq));
 }

 [Fact]
 public void FORMCLOSE_removes_event_queue()
 {
 string h = OpenForm("closeform");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));
 Assert.NotNull(_plugin.TryGetEventQueue(seq));

 var args = Buf(h);
 _plugin.f_FORMCLOSE(args);
 Assert.Equal("0", args.ToString());

 Assert.Null(_plugin.TryGetEventQueue(seq));
 }

 [Fact]
 public void FormClosing_pushes_close_event_during_FORMCLOSE()
 {
 string h = OpenForm("closingform");
 AddControl(h, "go", "BUTTON");
 int seq = SeqOf(h);

 // Realize the form via a synthetic click. This wires up
 // both the per-control click handler and the form-level
 // FormClosing -> "close" handler.
 _plugin.f_FORMSIMULATE(Buf($"{h},go,click"));
 FormEventQueue q = _plugin.TryGetEventQueue(seq)!;
 // Capture the queue reference BEFORE FORMCLOSE removes
 // the (form, queue) pair from the plugin's map.
 int beforeClose = q.Count;

 _plugin.f_FORMCLOSE(Buf(h));

 // FORMCLOSE destroys the form, which fires FormClosing
 // with our wired handler, which enqueues a "close"
 // record into the same queue object we captured above.
 // The plugin has since dropped its reference, but our
 // captured reference still has the event.
 var allEvents = q.DrainAll();
 Assert.True(allEvents.Count > beforeClose,
 $"Expected the close event to be enqueued; saw {allEvents.Count} total, started with {beforeClose}.");
 Assert.Contains(allEvents, e => e.EventType == "close");
 }
 }
}
