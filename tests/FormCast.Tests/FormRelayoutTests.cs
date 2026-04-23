// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Text;

using FormCast.Forms;
using FormCast.Forms.Layouts;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Tests for <c>@FORMRELAYOUT</c> in re-run the layout pass
 /// over an existing form's controls and apply the resulting bounds
 /// in place. The layout manager is selected by
 /// <see cref="FormLayoutFactory"/> based on
 /// <see cref="FormDescriptor.LayoutMode"/> and the form-level
 /// property bag.
 /// </summary>
 public sealed class FormRelayoutTests
 {
 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private static string OpenForm(global::FormCast.Plugin p,
 string layout = "absolute", int w = 400, int h = 300)
 {
 var args = Buf($"form,test,10,20,{w},{h}");
 p.f_FORMOPEN(args);
 string handle = args.ToString();
 // Set layout via FORMSET so the dispatch path is the
 // same one BTM scripts will use.
 var setArgs = Buf($"{handle},.,layout,{layout}");
 p.f_FORMSET(setArgs);
 return handle;
 }

 private static void AddControl(global::FormCast.Plugin p, string handle,
 string id, int x, int y, int w, int h)
 {
 var args = Buf($"{handle},{id},BUTTON,{x},{y},{w},{h},");
 p.f_FORMADD(args);
 }

 private static int GetInt(global::FormCast.Plugin p, string handle, string ctrl, string prop)
 {
 var args = Buf($"{handle},{ctrl},{prop}");
 p.f_FORMGET(args);
 return int.Parse(args.ToString(), System.Globalization.CultureInfo.InvariantCulture);
 }

 // -----------------------------------------------------------------
 // Validation paths
 // -----------------------------------------------------------------

 [Fact]
 public void FORMRELAYOUT_empty_args_returns_bad_args()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("");
 p.f_FORMRELAYOUT(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMRELAYOUT_unparseable_handle_returns_invalid_handle()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("not-a-handle");
 p.f_FORMRELAYOUT(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMRELAYOUT_unknown_handle_returns_invalid_handle()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("L:99999:99");
 p.f_FORMRELAYOUT(args);
 Assert.Equal("20100", args.ToString());
 }

 // -----------------------------------------------------------------
 // Happy path: each layout mode mutates control bounds in place
 // -----------------------------------------------------------------

 [Fact]
 public void FORMRELAYOUT_absolute_is_idempotent()
 {
 // The pass-through layout writes existing bounds back to
 // each control. Two relayouts in a row produce identical
 // state, and the bounds we set via @FORMADD survive.
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p, "absolute");
 AddControl(p, h, "a", 10, 20, 100, 30);
 AddControl(p, h, "b", 50, 60, 80, 24);

 var args = Buf(h);
 p.f_FORMRELAYOUT(args);
 Assert.Equal("0", args.ToString());

 Assert.Equal(10, GetInt(p, h, "a", "x"));
 Assert.Equal(20, GetInt(p, h, "a", "y"));
 Assert.Equal(100, GetInt(p, h, "a", "width"));
 Assert.Equal(50, GetInt(p, h, "b", "x"));

 // Idempotent: a second relayout does not perturb anything.
 args = Buf(h);
 p.f_FORMRELAYOUT(args);
 Assert.Equal("0", args.ToString());
 Assert.Equal(10, GetInt(p, h, "a", "x"));
 Assert.Equal(50, GetInt(p, h, "b", "x"));
 }

 [Fact]
 public void FORMRELAYOUT_grid_assigns_cell_positions()
 {
 // Set up a 2x2 grid via FORMSET so the layout factory's
 // grid_rows / grid_cols knobs are exercised. Add 4
 // controls with row/col props; relayout should pin them
 // to (0,0), (0,1), (1,0), (1,1) cells.
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p, "grid", 200, 100);

 var setArgs = Buf($"{h},.,grid_rows,2");
 p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},.,grid_cols,2");
 p.f_FORMSET(setArgs);

 AddControl(p, h, "tl", 0, 0, 0, 0);
 AddControl(p, h, "tr", 0, 0, 0, 0);
 AddControl(p, h, "bl", 0, 0, 0, 0);
 AddControl(p, h, "br", 0, 0, 0, 0);

 // Per-control row/col via FORMSET (control-level prop bag).
 setArgs = Buf($"{h},tl,row,0"); p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},tl,col,0"); p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},tr,row,0"); p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},tr,col,1"); p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},bl,row,1"); p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},bl,col,0"); p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},br,row,1"); p.f_FORMSET(setArgs);
 setArgs = Buf($"{h},br,col,1"); p.f_FORMSET(setArgs);

 var args = Buf(h);
 p.f_FORMRELAYOUT(args);
 Assert.Equal("0", args.ToString());

 // tl should be at (0,0); tr should be to the right;
 // bl should be below; br should be diagonally opposite.
 int tlx = GetInt(p, h, "tl", "x");
 int try_ = GetInt(p, h, "tr", "x");
 int blx = GetInt(p, h, "bl", "x");
 int blY = GetInt(p, h, "bl", "y");
 int tly = GetInt(p, h, "tl", "y");

 Assert.Equal(0, tlx);
 Assert.Equal(0, tly);
 Assert.True(try_ > tlx, $"top-right x ({try_}) should be > top-left x ({tlx})");
 Assert.True(blY > tly, $"bottom-left y ({blY}) should be > top-left y ({tly})");
 Assert.Equal(0, blx);
 }

 [Fact]
 public void FORMRELAYOUT_flow_packs_controls_left_to_right()
 {
 // Default flow direction is horizontal with 4px gap.
 // Three 50-wide controls in a 200-wide container should
 // land at x=0, x=54, x=108 (4px gap each).
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p, "flow", 200, 100);

 AddControl(p, h, "a", 0, 0, 50, 20);
 AddControl(p, h, "b", 0, 0, 50, 20);
 AddControl(p, h, "c", 0, 0, 50, 20);

 var args = Buf(h);
 p.f_FORMRELAYOUT(args);
 Assert.Equal("0", args.ToString());

 int ax = GetInt(p, h, "a", "x");
 int bx = GetInt(p, h, "b", "x");
 int cx = GetInt(p, h, "c", "x");
 Assert.Equal(0, ax);
 Assert.Equal(54, bx);
 Assert.Equal(108, cx);
 }

 [Fact]
 public void FormLayoutFactory_picks_correct_manager_for_each_mode()
 {
 // Direct factory test (no dispatch surface). Each mode
 // returns the right concrete type.
 var f = new FormDescriptor { LayoutMode = "absolute" };
 Assert.IsType<AbsoluteLayout>(FormLayoutFactory.Create(f));

 f.LayoutMode = "grid";
 Assert.IsType<GridLayout>(FormLayoutFactory.Create(f));

 f.LayoutMode = "flow";
 Assert.IsType<FlowLayout>(FormLayoutFactory.Create(f));

 f.LayoutMode = "dock";
 Assert.IsType<DockLayout>(FormLayoutFactory.Create(f));

 // Empty / unknown falls back to absolute.
 f.LayoutMode = "";
 Assert.IsType<AbsoluteLayout>(FormLayoutFactory.Create(f));

 f.LayoutMode = "unknown";
 Assert.IsType<AbsoluteLayout>(FormLayoutFactory.Create(f));
 }

 [Fact]
 public void FormLayoutFactory_reads_flow_direction_from_property_bag()
 {
 var f = new FormDescriptor { LayoutMode = "flow" };
 f.Properties["flow_direction"] = "vertical";
 FlowLayout flow = Assert.IsType<FlowLayout>(FormLayoutFactory.Create(f));
 // We can't read direction directly off FlowLayout (private
 // field), but we can verify by behavior: a single 100x20
 // control in a 200x200 container is placed identically
 // either way, so we need a multi-control test. The
 // simplest behavior check is that two controls stack
 // vertically when direction=vertical.
 var c1 = new ControlDescriptor { Id = "a", Width = 50, Height = 20 };
 var c2 = new ControlDescriptor { Id = "b", Width = 50, Height = 20 };
 var bounds = flow.Compute(new[] { c1, c2 }, 200, 200);
 // In vertical flow, b should be BELOW a (different y),
 // not to the right (different x).
 Assert.Equal(bounds["a"].X, bounds["b"].X);
 Assert.True(bounds["b"].Y > bounds["a"].Y);
 }
 }
}
