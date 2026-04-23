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
 /// tests for the visual designer infrastructure: the
 /// pseudo-properties on @FORMSET and @FORMGET that the
 /// BTM-authored designer in will sit on top of.
 ///
 /// Form-level: <c>design_mode</c> and <c>selected</c> flow
 /// through the existing prop-bag fall-through .
 ///
 /// Control-level: <c>position</c> / <c>size</c> set absolute
 /// coordinates from a "x,y" / "w,h" pair; <c>moveby</c> /
 /// <c>resizeby</c> apply a delta. <c>position</c> and
 /// <c>size</c> reads return the comma-pair shape.
 /// </summary>
 public class DesignerPrimitivesTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public DesignerPrimitivesTests()
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

 private string OpenForm()
 {
 var args = Buf("form,test,10,20,400,300");
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

 private string Set(string handle, string ctrl, string prop, string value)
 {
 var args = Buf($"{handle},{ctrl},{prop},{value}");
 _plugin.f_FORMSET(args);
 return args.ToString();
 }

 private string Get(string handle, string ctrl, string prop)
 {
 var args = Buf($"{handle},{ctrl},{prop}");
 _plugin.f_FORMGET(args);
 return args.ToString();
 }

 // -----------------------------------------------------------------
 // Form-level: design_mode and selected
 // -----------------------------------------------------------------

 [Fact]
 public void Form_design_mode_round_trips_through_prop_bag()
 {
 string h = OpenForm();
 Assert.Equal("0", Set(h, ".", "design_mode", "1"));
 Assert.Equal("1", Get(h, ".", "design_mode"));
 }

 [Fact]
 public void Form_selected_round_trips_through_prop_bag()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON");
 Assert.Equal("0", Set(h, ".", "selected", "btn"));
 Assert.Equal("btn", Get(h, ".", "selected"));
 }

 // -----------------------------------------------------------------
 // Control-level: position / size absolute setters
 // -----------------------------------------------------------------

 [Fact]
 public void Position_setter_writes_X_and_Y()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 10, y: 20, w: 80, h: 24);
 Assert.Equal("0", Set(h, "btn", "position", "100:200"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var btn = form.Controls.First(c => c.Id == "btn");
 Assert.Equal(100, btn.X);
 Assert.Equal(200, btn.Y);
 }

 [Fact]
 public void Size_setter_writes_Width_and_Height()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", w: 80, h: 24);
 Assert.Equal("0", Set(h, "btn", "size", "200:40"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var btn = form.Controls.First(c => c.Id == "btn");
 Assert.Equal(200, btn.Width);
 Assert.Equal(40, btn.Height);
 }

 [Fact]
 public void Position_getter_returns_comma_pair()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 10, y: 20);
 Assert.Equal("10:20", Get(h, "btn", "position"));
 }

 [Fact]
 public void Size_getter_returns_comma_pair()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", w: 80, h: 24);
 Assert.Equal("80:24", Get(h, "btn", "size"));
 }

 // -----------------------------------------------------------------
 // Control-level: moveby / resizeby deltas
 // -----------------------------------------------------------------

 [Fact]
 public void Moveby_applies_delta_to_X_and_Y()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 10, y: 20);
 Assert.Equal("0", Set(h, "btn", "moveby", "5:7"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var btn = form.Controls.First(c => c.Id == "btn");
 Assert.Equal(15, btn.X);
 Assert.Equal(27, btn.Y);
 }

 [Fact]
 public void Moveby_with_negative_delta_moves_in_opposite_direction()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", x: 50, y: 50);
 Set(h, "btn", "moveby", "-10:-15");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var btn = form.Controls.First(c => c.Id == "btn");
 Assert.Equal(40, btn.X);
 Assert.Equal(35, btn.Y);
 }

 [Fact]
 public void Resizeby_applies_delta_to_Width_and_Height()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON", w: 80, h: 24);
 Set(h, "btn", "resizeby", "20:8");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var btn = form.Controls.First(c => c.Id == "btn");
 Assert.Equal(100, btn.Width);
 Assert.Equal(32, btn.Height);
 }

 // -----------------------------------------------------------------
 // Successive operations compose
 // -----------------------------------------------------------------

 [Fact]
 public void Position_then_moveby_composes()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON");
 Set(h, "btn", "position", "100:100");
 Set(h, "btn", "moveby", "5:5");
 Assert.Equal("105:105", Get(h, "btn", "position"));
 }

 [Fact]
 public void Size_then_resizeby_composes()
 {
 string h = OpenForm();
 Add(h, "btn", "BUTTON");
 Set(h, "btn", "size", "200:50");
 Set(h, "btn", "resizeby", "-10:5");
 Assert.Equal("190:55", Get(h, "btn", "size"));
 }

 // -----------------------------------------------------------------
 // Designer workflow: enable mode, select, move, deselect
 // -----------------------------------------------------------------

 [Fact]
 public void Designer_workflow_select_then_move_via_selection()
 {
 // The BTM designer reads "selected" to know the active
 // control, then drives moveby on it. The plugin doesn't
 // know about the indirection -- it's a BTM-side
 // convention -- but the test pins the round-trip so a
 // future change does not break the contract.
 string h = OpenForm();
 Add(h, "btnA", "BUTTON", x: 10, y: 10);
 Add(h, "btnB", "BUTTON", x: 10, y: 50);

 Set(h, ".", "design_mode", "1");
 Set(h, ".", "selected", "btnA");

 string current = Get(h, ".", "selected");
 Set(h, current, "moveby", "30:0");

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Assert.Equal(40, form.Controls.First(c => c.Id == "btnA").X);
 // btnB unchanged
 Assert.Equal(10, form.Controls.First(c => c.Id == "btnB").X);
 }
 }
}
