// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using FormCast.Forms;
using FormCast.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the MEMO control: multiline TextBox in both
 /// read-only and editable variants. The descriptor side reuses
 /// the universal Text property and stashes <c>readonly</c> /
 /// <c>nowrap</c> in the prop bag; the realizer turns those into a
 /// <see cref="TextBox"/> with <c>Multiline = true</c>.
 /// </summary>
 public class MemoTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;
 private readonly GuiHostThread _host;

 public MemoTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 _host = new GuiHostThread();
 _host.Start();
 }

 public void Dispose()
 {
 _host.Stop();
 _host.Dispose();
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "memotest")
 {
 var args = Buf($"form,{name},10,20,400,300");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private static int SeqOf(string handle) =>
 int.Parse(handle.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);

 // -----------------------------------------------------------------
 // Recognition
 // -----------------------------------------------------------------

 [Fact]
 public void MEMO_is_a_recognized_control_type()
 {
 Assert.True(ControlBuilders.IsRecognizedType("MEMO"));
 Assert.True(ControlBuilders.IsRecognizedType("memo"));
 }

 [Fact]
 public void FORMADD_MEMO_succeeds_with_initial_text()
 {
 string h = OpenForm();
 var args = Buf($"{h},notes,MEMO,5,5,380,200,Initial text");
 _plugin.f_FORMADD(args);
 Assert.Equal("0", args.ToString());

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var memo = form.Controls.First(c => c.Id == "notes");
 Assert.Equal("MEMO", memo.Type);
 Assert.Equal("Initial text", memo.Text);
 }

 // -----------------------------------------------------------------
 // Realize: editable variant
 // -----------------------------------------------------------------

 [Fact]
 public void Realize_MEMO_creates_multiline_TextBox()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},notes,MEMO,5,5,380,200,line1"));
 var form = _plugin.LookupDescriptor(SeqOf(h))!;

 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var tb = (TextBox)realized.Controls[0];
 Assert.True(tb.Multiline);
 Assert.False(tb.ReadOnly);
 Assert.True(tb.WordWrap);
 Assert.Equal(ScrollBars.Vertical, tb.ScrollBars);
 Assert.Equal("line1", tb.Text);
 Assert.True(tb.AcceptsReturn);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 // -----------------------------------------------------------------
 // Realize: read-only variant
 // -----------------------------------------------------------------

 [Fact]
 public void Realize_MEMO_with_readonly_prop_creates_readonly_TextBox()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},notes,MEMO,5,5,380,200,locked"));
 _plugin.f_FORMSET(Buf($"{h},notes,readonly,1"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var tb = (TextBox)realized.Controls[0];
 Assert.True(tb.ReadOnly);
 Assert.Equal("locked", tb.Text);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 // -----------------------------------------------------------------
 // Realize: nowrap switches to both scroll bars
 // -----------------------------------------------------------------

 [Fact]
 public void Realize_MEMO_with_nowrap_uses_both_scrollbars()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},notes,MEMO,5,5,380,200,"));
 _plugin.f_FORMSET(Buf($"{h},notes,nowrap,1"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var tb = (TextBox)realized.Controls[0];
 Assert.False(tb.WordWrap);
 Assert.Equal(ScrollBars.Both, tb.ScrollBars);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 // -----------------------------------------------------------------
 // FORMSET text writes back to the descriptor and a re-realize
 // picks it up.
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSET_text_updates_descriptor_and_next_realize_uses_new_value()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},notes,MEMO,5,5,380,200,old"));
 _plugin.f_FORMSET(Buf($"{h},notes,text,new value"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Assert.Equal("new value", form.Controls.First(c => c.Id == "notes").Text);

 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var tb = (TextBox)realized.Controls[0];
 Assert.Equal("new value", tb.Text);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 // -----------------------------------------------------------------
 // FormSerializer round-trip preserves MEMO type, text, and the
 // readonly / nowrap prop bag entries.
 // -----------------------------------------------------------------

 [Fact]
 public void MEMO_round_trips_through_FormSerializer()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},notes,MEMO,5,5,380,200,line1"));
 _plugin.f_FORMSET(Buf($"{h},notes,readonly,1"));
 _plugin.f_FORMSET(Buf($"{h},notes,nowrap,1"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 string json = FormSerializer.Serialize(form);
 FormDescriptor reloaded = FormSerializer.Deserialize(json);

 var memo = reloaded.Controls.First(c => c.Id == "notes");
 Assert.Equal("MEMO", memo.Type);
 Assert.Equal("line1", memo.Text);
 Assert.Equal("1", memo.Properties["readonly"]);
 Assert.Equal("1", memo.Properties["nowrap"]);
 }

 // -----------------------------------------------------------------
 // The MEMO TextBox still wires the existing TextBox event
 // capture (TextChanged -> "change", KeyPress -> "keypress")
 // because Multiline TextBox matches the TextBox case in the
 // realizer event switch.
 // -----------------------------------------------------------------

 [Fact]
 public void Simulate_keypress_on_MEMO_enqueues_keypress_event()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},notes,MEMO,5,5,380,200,"));
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},notes,keypress,m"));

 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);
 var events = queue!.DrainAll();
 Assert.Single(events);
 Assert.Equal("keypress", events[0].EventType);
 Assert.Equal("m", events[0].Value);
 }

 [Fact]
 public void Simulate_type_on_MEMO_enqueues_change_event()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},notes,MEMO,5,5,380,200,"));
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},notes,type,first line"));

 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);
 var events = queue!.DrainAll();
 // Filter for "change" events -- the resize event from the
 // EventWiringTable is also enqueued but not relevant here.
 var change = Assert.Single(events, e => e.EventType == "change");
 Assert.Equal("first line", change.Value);
 }
 }
}
