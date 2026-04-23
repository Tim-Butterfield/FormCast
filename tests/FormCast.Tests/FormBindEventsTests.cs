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
 /// tests for the expanded <c>@FORMBIND</c> event surface.
 /// shipped click and change; adds focus, blur, dblclick,
 /// and keypress, plus matching synthetic-event paths in
 /// <c>FormRealizer.Simulate</c> so headless tests can drive each
 /// event without needing a real visible window.
 ///
 /// Each test follows the same pattern as <c>FormBindTests</c>:
 /// register a binding via <c>TestCommandHook</c>, drive a
 /// <c>@FORMSIMULATE</c> action, wait briefly for the worker
 /// thread to dispatch, and assert the captured command line.
 /// </summary>
 public class FormBindEventsTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormBindEventsTests()
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

 private void Simulate(string handle, string ctrl, string action, string? value = null)
 {
 var args = value is null
 ? Buf($"{handle},{ctrl},{action}")
 : Buf($"{handle},{ctrl},{action},{value}");
 _plugin.f_FORMSIMULATE(args);
 }

 /// <summary>
 /// Drive a single bound event and return the captured command
 /// strings the worker thread saw. Uses TestCommandHook so
 /// nothing actually shells out to TakeCmd.dll.
 /// </summary>
 private System.Collections.Generic.List<string> CapturedCommands(
 Action driveTest, TimeSpan? timeout = null)
 {
 var captured = new BlockingCollection<string>();
 _plugin.TestCommandHook = cmd => captured.Add(cmd);
 try
 {
 driveTest();
 // Drain whatever lands within a short window. The
 // worker thread is fast in headless mode; 500ms is
 // plenty of slack.
 var list = new System.Collections.Generic.List<string>();
 var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMilliseconds(500));
 while (DateTime.UtcNow < deadline)
 {
 if (captured.TryTake(out string? cmd, 25)) { list.Add(cmd); }
 else if (list.Count > 0) { break; }
 }
 return list;
 }
 finally
 {
 _plugin.TestCommandHook = null;
 }
 }

 // -----------------------------------------------------------------
 // focus
 // -----------------------------------------------------------------

 [Fact]
 public void Focus_event_fires_bound_command_on_any_control()
 {
 string h = OpenForm("focusform");
 AddControl(h, "txt", "EDIT");
 Assert.Equal("0", Bind(h, "txt", "focus", "echo focused"));

 var cmds = CapturedCommands(() => Simulate(h, "txt", "focus"));
 Assert.Single(cmds);
 Assert.Equal("echo focused", cmds[0]);
 }

 // -----------------------------------------------------------------
 // blur
 // -----------------------------------------------------------------

 [Fact]
 public void Blur_event_fires_bound_command()
 {
 string h = OpenForm("blurform");
 AddControl(h, "txt", "EDIT");
 Assert.Equal("0", Bind(h, "txt", "blur", "echo blurred"));

 var cmds = CapturedCommands(() => Simulate(h, "txt", "blur"));
 Assert.Single(cmds);
 Assert.Equal("echo blurred", cmds[0]);
 }

 // -----------------------------------------------------------------
 // keypress
 // -----------------------------------------------------------------

 [Fact]
 public void Keypress_on_textbox_fires_bound_command()
 {
 string h = OpenForm("kpform");
 AddControl(h, "txt", "EDIT");
 Assert.Equal("0", Bind(h, "txt", "keypress", "echo got_key"));

 var cmds = CapturedCommands(() => Simulate(h, "txt", "keypress", "a"));
 Assert.Single(cmds);
 Assert.Equal("echo got_key", cmds[0]);
 }

 [Fact]
 public void Keypress_value_carries_through_FORMEVENTS_drain()
 {
 // Bind nothing -- exercise the queue side of the contract:
 // a synthetic keypress lands in the form's event queue with
 // the KeyChar as the value payload.
 string h = OpenForm("kpdrain");
 int seq = int.Parse(h.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);
 AddControl(h, "txt", "EDIT");

 // Realize the form by calling FORMSHOW (headless realize-only).
 var showArgs = Buf(h);
 _plugin.f_FORMSHOW(showArgs);

 Simulate(h, "txt", "keypress", "Z");

 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);
 var events = queue!.DrainAll();
 Assert.Single(events);
 Assert.Equal("keypress", events[0].EventType);
 Assert.Equal("Z", events[0].Value);
 Assert.Equal("txt", events[0].ControlId);
 }

 [Fact]
 public void Keypress_on_button_returns_unsupported_for_control()
 {
 string h = OpenForm("kpbtn");
 AddControl(h, "btn", "BUTTON");

 var args = Buf($"{h},btn,keypress,a");
 _plugin.f_FORMSIMULATE(args);
 // Plugin error code for "action recognized but not
 // applicable to this control": 20107 (shared with
 // UnknownAction in the dispatch surface).
 Assert.Equal("20107", args.ToString());
 }

 // -----------------------------------------------------------------
 // dblclick
 // -----------------------------------------------------------------

 [Fact]
 public void Dblclick_on_button_fires_bound_command()
 {
 string h = OpenForm("dcform");
 AddControl(h, "btn", "BUTTON");
 Assert.Equal("0", Bind(h, "btn", "dblclick", "echo dc"));

 var cmds = CapturedCommands(() => Simulate(h, "btn", "dblclick"));
 Assert.Single(cmds);
 Assert.Equal("echo dc", cmds[0]);
 }

 [Fact]
 public void Dblclick_on_textbox_returns_unsupported_for_control()
 {
 string h = OpenForm("dctxt");
 AddControl(h, "txt", "EDIT");

 var args = Buf($"{h},txt,dblclick");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20107", args.ToString());
 }

 // -----------------------------------------------------------------
 // Combined: focus -> keypress -> blur sequence on a TextBox
 // produces three distinct events in FIFO order.
 // -----------------------------------------------------------------

 [Fact]
 public void Focus_keypress_blur_sequence_arrives_in_order()
 {
 string h = OpenForm("seqform");
 int seq = int.Parse(h.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);
 AddControl(h, "name", "EDIT");

 _plugin.f_FORMSHOW(Buf(h));

 Simulate(h, "name", "focus");
 Simulate(h, "name", "keypress", "x");
 Simulate(h, "name", "blur");

 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);
 var events = queue!.DrainAll();
 Assert.Equal(3, events.Count);
 Assert.Equal("focus", events[0].EventType);
 Assert.Equal("keypress", events[1].EventType);
 Assert.Equal("x", events[1].Value);
 Assert.Equal("blur", events[2].EventType);
 }

 // -----------------------------------------------------------------
 // Per-control bindings remain isolated: a focus binding on
 // ctrlA should not fire when ctrlB receives focus.
 // -----------------------------------------------------------------

 [Fact]
 public void Focus_binding_on_one_control_does_not_fire_for_another()
 {
 string h = OpenForm("isoform");
 AddControl(h, "a", "EDIT", y: 5);
 AddControl(h, "b", "EDIT", y: 35);

 Assert.Equal("0", Bind(h, "a", "focus", "echo a_focus"));

 var cmds = CapturedCommands(() => Simulate(h, "b", "focus"));
 Assert.Empty(cmds);

 cmds = CapturedCommands(() => Simulate(h, "a", "focus"));
 Assert.Single(cmds);
 Assert.Equal("echo a_focus", cmds[0]);
 }
 }
}
