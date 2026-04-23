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
 /// tests for PANEL recursion and nested layouts. Covers the
 /// descriptor side (ControlDescriptor.Children, parent/child id
 /// syntax in @FORMADD, recursive FindControl for FORMSET/FORMGET),
 /// the FormSerializer round-trip of nested controls, and the
 /// realizer-side recursion that builds child WinForms.Control
 /// instances inside the parent Panel.
 /// </summary>
 public class PanelNestedTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;
 private readonly GuiHostThread _host;

 public PanelNestedTests()
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

 private string OpenForm(string name = "nested", int w = 400, int h = 300)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private static int SeqOf(string handle) =>
 int.Parse(handle.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);

 private void Add(string handle, string id, string type,
 int x = 5, int y = 5, int w = 100, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 // -----------------------------------------------------------------
 // Adding a child to a PANEL via parent/child id syntax
 // -----------------------------------------------------------------

 [Fact]
 public void FORMADD_with_panel_slash_id_syntax_nests_under_parent()
 {
 string h = OpenForm();
 Add(h, "side", "PANEL", 0, 0, 200, 300);
 Add(h, "side/btnA", "BUTTON", 5, 5, 80, 24, "A");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Assert.Single(form.Controls);
 var side = form.Controls[0];
 Assert.Equal("side", side.Id);
 Assert.Single(side.Children);
 Assert.Equal("btnA", side.Children[0].Id);
 Assert.Equal("BUTTON", side.Children[0].Type);
 }

 [Fact]
 public void FORMADD_two_levels_deep_nests_correctly()
 {
 string h = OpenForm();
 Add(h, "outer", "PANEL", 0, 0, 300, 200);
 Add(h, "outer/inner", "PANEL", 5, 5, 280, 180);
 Add(h, "outer/inner/btn", "BUTTON", 10, 10, 60, 20, "X");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var outer = form.Controls[0];
 var inner = outer.Children[0];
 Assert.Equal("inner", inner.Id);
 Assert.Single(inner.Children);
 Assert.Equal("btn", inner.Children[0].Id);
 }

 [Fact]
 public void FORMADD_with_unknown_parent_path_returns_unknown_control_id()
 {
 string h = OpenForm();
 // No "missing" panel exists yet.
 var args = Buf($"{h},missing/btn,BUTTON,5,5,80,24,X");
 _plugin.f_FORMADD(args);
 Assert.Equal("20103", args.ToString());
 }

 [Fact]
 public void FORMADD_with_non_panel_parent_returns_unknown_control_id()
 {
 string h = OpenForm();
 Add(h, "lbl", "LABEL", 0, 0, 100, 20, "");
 // LABEL is now a container (needed for MENUSTRIP menu item
 // nesting). Nesting a child under it succeeds.
 var args = Buf($"{h},lbl/child,BUTTON,5,5,80,24,X");
 _plugin.f_FORMADD(args);
 Assert.Equal("0", args.ToString());
 }

 // -----------------------------------------------------------------
 // FindControl recursive: FORMSET on a nested control
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSET_text_on_nested_control_via_short_id_works()
 {
 string h = OpenForm();
 Add(h, "panel1", "PANEL", 0, 0, 200, 200);
 Add(h, "panel1/btn", "BUTTON", 5, 5, 80, 24, "old");

 // Short id (just "btn") should find the nested control
 // because FindControl recurses.
 var args = Buf($"{h},btn,text,new");
 _plugin.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var btn = form.Controls[0].Children[0];
 Assert.Equal("new", btn.Text);
 }

 [Fact]
 public void FORMSET_text_on_nested_control_via_full_path_works()
 {
 string h = OpenForm();
 Add(h, "panel1", "PANEL", 0, 0, 200, 200);
 Add(h, "panel1/btn", "BUTTON", 5, 5, 80, 24, "old");

 var args = Buf($"{h},panel1/btn,text,new");
 _plugin.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Assert.Equal("new", form.Controls[0].Children[0].Text);
 }

 [Fact]
 public void Same_short_id_at_two_nesting_levels_finds_first_match()
 {
 // FindControlRecursive does a depth-first walk and returns
 // the first match. The full-path form is the way to
 // disambiguate when two controls share a short id.
 string h = OpenForm();
 Add(h, "btn", "BUTTON", 0, 0, 80, 24, "outer");
 Add(h, "panel1", "PANEL", 0, 30, 200, 200);
 Add(h, "panel1/btn", "BUTTON", 5, 5, 80, 24, "inner");

 // Short id matches the form-level "btn" first (Controls
 // is iterated in order, "btn" is at index 0 before
 // "panel1").
 var args = Buf($"{h},btn,text");
 _plugin.f_FORMGET(args);
 Assert.Equal("outer", args.ToString());

 // Full-path form selects the nested one.
 args = Buf($"{h},panel1/btn,text");
 _plugin.f_FORMGET(args);
 Assert.Equal("inner", args.ToString());
 }

 // -----------------------------------------------------------------
 // FormSerializer round-trip of nested controls
 // -----------------------------------------------------------------

 [Fact]
 public void Nested_panels_round_trip_through_FormSerializer()
 {
 string h = OpenForm();
 Add(h, "outer", "PANEL", 0, 0, 300, 200);
 Add(h, "outer/inner", "PANEL", 5, 5, 280, 180);
 Add(h, "outer/inner/btn", "BUTTON", 10, 10, 60, 20, "X");
 Add(h, "outer/inner/lbl", "LABEL", 80, 10, 100, 20, "Label text");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 string json = FormSerializer.Serialize(form);
 FormDescriptor reloaded = FormSerializer.Deserialize(json);

 Assert.Single(reloaded.Controls);
 var outer = reloaded.Controls[0];
 Assert.Equal("outer", outer.Id);
 Assert.Single(outer.Children);
 var inner = outer.Children[0];
 Assert.Equal("inner", inner.Id);
 Assert.Equal(2, inner.Children.Count);
 Assert.Equal("btn", inner.Children[0].Id);
 Assert.Equal("lbl", inner.Children[1].Id);
 Assert.Equal("Label text", inner.Children[1].Text);
 }

 [Fact]
 public void Plain_form_without_nesting_still_round_trips_byte_stable()
 {
 // Regression: WriteControl extraction must not
 // change the output format for non-nested forms.
 string h = OpenForm();
 Add(h, "btn", "BUTTON", 5, 5, 80, 24, "Hi");
 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 string a = FormSerializer.Serialize(form);
 FormDescriptor reloaded = FormSerializer.Deserialize(a);
 string b = FormSerializer.Serialize(reloaded);
 Assert.Equal(a, b);
 }

 // -----------------------------------------------------------------
 // FormRealizer recursion: nested controls become real WinForms
 // Control instances inside the parent Panel.
 // -----------------------------------------------------------------

 [Fact]
 public void Realize_PANEL_creates_WinForms_Panel_with_child_controls()
 {
 string h = OpenForm();
 Add(h, "side", "PANEL", 0, 0, 200, 300);
 Add(h, "side/btnA", "BUTTON", 5, 5, 80, 24, "A");
 Add(h, "side/btnB", "BUTTON", 5, 35, 80, 24, "B");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 Assert.Single(realized.Controls);
 var panel = (Panel)realized.Controls[0];
 Assert.Equal("side", panel.Name);
 Assert.Equal(2, panel.Controls.Count);
 var a = (Button)panel.Controls[0];
 var b = (Button)panel.Controls[1];
 Assert.Equal("btnA", a.Name);
 Assert.Equal("btnB", b.Name);
 Assert.Equal("A", a.Text);
 Assert.Equal("B", b.Text);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 [Fact]
 public void Realize_two_levels_deep_creates_nested_Panels()
 {
 string h = OpenForm();
 Add(h, "outer", "PANEL", 0, 0, 300, 200);
 Add(h, "outer/inner", "PANEL", 5, 5, 280, 180);
 Add(h, "outer/inner/btn", "BUTTON", 10, 10, 60, 20, "X");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var outer = (Panel)realized.Controls[0];
 Assert.Single(outer.Controls);
 var inner = (Panel)outer.Controls[0];
 Assert.Equal("inner", inner.Name);
 Assert.Single(inner.Controls);
 var btn = (Button)inner.Controls[0];
 Assert.Equal("btn", btn.Name);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 // -----------------------------------------------------------------
 // FORMSIMULATE on a nested control via short id works because
 // the underlying FindControlByName helper is recursive.
 // -----------------------------------------------------------------

 [Fact]
 public void Simulate_click_on_nested_button_fires_event()
 {
 string h = OpenForm();
 int seq = SeqOf(h);
 Add(h, "side", "PANEL", 0, 0, 200, 200);
 Add(h, "side/go", "BUTTON", 5, 5, 80, 24, "Go");

 var args = Buf($"{h},go,click");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());

 var queue = _plugin.TryGetEventQueue(seq);
 Assert.NotNull(queue);
 var events = queue!.DrainAll();
 Assert.Single(events);
 Assert.Equal("click", events[0].EventType);
 Assert.Equal("go", events[0].ControlId);
 }
 }
}
