// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

using FormCast.Forms;
using FormCast.Forms.Layouts;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DockLayout"/>: edge consumption order,
    /// strip sizing from descriptor W/H, fill behavior, padding, and the
    /// IDE-style top + bottom + left + right + fill composite.
    /// </summary>
    public class DockLayoutTests
    {
        private static ControlDescriptor C(string id, string dock, int w, int h)
        {
            var c = new ControlDescriptor
            {
                Type = "PANEL",
                Id = id,
                Width = w,
                Height = h,
            };
            c.Properties["dock"] = dock;
            return c;
        }

        [Fact]
        public void Mode_is_dock()
        {
            Assert.Equal("dock", new DockLayout().Mode);
        }

        [Fact]
        public void Empty_input_returns_empty_dictionary()
        {
            var result = new DockLayout().Compute(new List<ControlDescriptor>(), 100, 100);
            Assert.Empty(result);
        }

        [Fact]
        public void Top_consumes_full_width_strip()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor> { C("toolbar", "top", 0, 30) };
            var result = layout.Compute(children, 200, 100);
            Assert.Equal(new LayoutRect(0, 0, 200, 30), result["toolbar"]);
        }

        [Fact]
        public void Bottom_consumes_full_width_strip_at_bottom()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor> { C("status", "bottom", 0, 25) };
            var result = layout.Compute(children, 200, 100);
            Assert.Equal(new LayoutRect(0, 75, 200, 25), result["status"]);
        }

        [Fact]
        public void Left_consumes_full_height_strip()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor> { C("nav", "left", 40, 0) };
            var result = layout.Compute(children, 200, 100);
            Assert.Equal(new LayoutRect(0, 0, 40, 100), result["nav"]);
        }

        [Fact]
        public void Right_consumes_full_height_strip_at_right()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor> { C("props", "right", 60, 0) };
            var result = layout.Compute(children, 200, 100);
            Assert.Equal(new LayoutRect(140, 0, 60, 100), result["props"]);
        }

        [Fact]
        public void Fill_consumes_remaining_interior()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor> { C("main", "fill", 0, 0) };
            var result = layout.Compute(children, 200, 100);
            Assert.Equal(new LayoutRect(0, 0, 200, 100), result["main"]);
        }

        [Fact]
        public void IDE_layout_top_bottom_left_right_fill_composes_correctly()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor>
            {
                C("toolbar", "top",    0, 30),  // (0,0,400,30)
                C("status",  "bottom", 0, 20),  // (0,250,400,20) inside remaining (0,30,400,240)
                C("nav",     "left",   50, 0),  // (0,30,50,220)
                C("props",   "right",  70, 0),  // (330,30,70,220)
                C("main",    "fill",   0, 0),   // (50,30,280,220)
            };
            var result = layout.Compute(children, 400, 270);
            Assert.Equal(new LayoutRect(0, 0, 400, 30), result["toolbar"]);
            Assert.Equal(new LayoutRect(0, 250, 400, 20), result["status"]);
            Assert.Equal(new LayoutRect(0, 30, 50, 220), result["nav"]);
            Assert.Equal(new LayoutRect(330, 30, 70, 220), result["props"]);
            Assert.Equal(new LayoutRect(50, 30, 280, 220), result["main"]);
        }

        [Fact]
        public void Children_after_fill_get_zero_size()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor>
            {
                C("main", "fill", 0, 0),
                C("ghost", "top", 0, 30),
            };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 100, 100), result["main"]);
            Assert.Equal(0, result["ghost"].Width);
            Assert.Equal(0, result["ghost"].Height);
        }

        [Fact]
        public void Strip_thickness_clamped_to_remaining_space()
        {
            var layout = new DockLayout();
            var children = new List<ControlDescriptor>
            {
                C("a", "top", 0, 60),
                C("b", "top", 0, 60),  // only 40 remains
            };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 100, 60), result["a"]);
            Assert.Equal(new LayoutRect(0, 60, 100, 40), result["b"]);
        }

        [Fact]
        public void Padding_offsets_interior()
        {
            var layout = new DockLayout(padding: 10);
            var children = new List<ControlDescriptor> { C("main", "fill", 0, 0) };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(10, 10, 80, 80), result["main"]);
        }

        [Fact]
        public void Default_dock_value_is_top_when_property_missing()
        {
            var layout = new DockLayout();
            var c = new ControlDescriptor
            {
                Type = "PANEL", Id = "x", Width = 0, Height = 25,
            };
            var result = layout.Compute(new List<ControlDescriptor> { c }, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 100, 25), result["x"]);
        }
    }
}
