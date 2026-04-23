// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

using FormCast.Forms;
using FormCast.Forms.Layouts;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FlowLayout"/>: declaration-order packing,
    /// wrap behavior, gap handling, both flow directions, and edge cases.
    /// </summary>
    public class FlowLayoutTests
    {
        private static ControlDescriptor C(string id, int w, int h)
        {
            return new ControlDescriptor { Type = "BUTTON", Id = id, Width = w, Height = h };
        }

        [Fact]
        public void Mode_is_flow()
        {
            Assert.Equal("flow", new FlowLayout().Mode);
        }

        [Fact]
        public void Empty_input_returns_empty_dictionary()
        {
            var result = new FlowLayout().Compute(new List<ControlDescriptor>(), 200, 100);
            Assert.Empty(result);
        }

        [Fact]
        public void Horizontal_packs_left_to_right_with_gap()
        {
            var layout = new FlowLayout(hgap: 5, vgap: 10);
            var children = new List<ControlDescriptor>
            {
                C("a", 50, 20),
                C("b", 30, 20),
                C("c", 40, 20),
            };
            var result = layout.Compute(children, 500, 100);
            Assert.Equal(new LayoutRect(0, 0, 50, 20), result["a"]);
            Assert.Equal(new LayoutRect(55, 0, 30, 20), result["b"]);
            Assert.Equal(new LayoutRect(90, 0, 40, 20), result["c"]);
        }

        [Fact]
        public void Horizontal_wraps_when_next_item_overflows()
        {
            var layout = new FlowLayout(hgap: 5, vgap: 10);
            var children = new List<ControlDescriptor>
            {
                C("a", 60, 20),
                C("b", 60, 20),
                C("c", 60, 20),
            };
            // Container width 130 fits a + b (60 + 5 + 60 = 125) but
            // not c (would need 125 + 5 + 60 = 190). c wraps to row 2.
            var result = layout.Compute(children, 130, 200);
            Assert.Equal(new LayoutRect(0, 0, 60, 20), result["a"]);
            Assert.Equal(new LayoutRect(65, 0, 60, 20), result["b"]);
            Assert.Equal(new LayoutRect(0, 30, 60, 20), result["c"]); // y = 20 + vgap 10
        }

        [Fact]
        public void Wrapped_row_uses_max_height_of_previous_row()
        {
            var layout = new FlowLayout(hgap: 0, vgap: 0);
            var children = new List<ControlDescriptor>
            {
                C("a", 50, 20),
                C("b", 50, 40),  // tallest in row 1
                C("c", 50, 20),
            };
            // Row 1 fits a + b at width 100. c wraps to y = 40 (max of row 1).
            var result = layout.Compute(children, 100, 200);
            Assert.Equal(new LayoutRect(0, 0, 50, 20), result["a"]);
            Assert.Equal(new LayoutRect(50, 0, 50, 40), result["b"]);
            Assert.Equal(new LayoutRect(0, 40, 50, 20), result["c"]);
        }

        [Fact]
        public void First_item_on_a_row_is_placed_even_when_oversize()
        {
            var layout = new FlowLayout(hgap: 0, vgap: 0);
            var children = new List<ControlDescriptor>
            {
                C("big", 500, 20),
            };
            var result = layout.Compute(children, 100, 100);
            // Single oversize child still gets placed at origin.
            Assert.Equal(new LayoutRect(0, 0, 500, 20), result["big"]);
        }

        [Fact]
        public void Padding_offsets_starting_position()
        {
            var layout = new FlowLayout(hgap: 0, vgap: 0, padding: 8);
            var children = new List<ControlDescriptor> { C("a", 30, 20) };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(8, 8, 30, 20), result["a"]);
        }

        [Fact]
        public void Padding_affects_wrap_threshold()
        {
            var layout = new FlowLayout(hgap: 0, vgap: 0, padding: 5);
            var children = new List<ControlDescriptor>
            {
                C("a", 50, 20),
                C("b", 50, 20),
            };
            // Container 100, padding 5 -> inner edge at x=95.
            // a at x=5, ends at x=55. b would start at 55, end 105 > 95 -> wrap.
            var result = layout.Compute(children, 100, 200);
            Assert.Equal(new LayoutRect(5, 5, 50, 20), result["a"]);
            Assert.Equal(new LayoutRect(5, 25, 50, 20), result["b"]);
        }

        [Fact]
        public void Vertical_packs_top_to_bottom()
        {
            var layout = new FlowLayout(hgap: 5, vgap: 5, direction: FlowDirection.Vertical);
            var children = new List<ControlDescriptor>
            {
                C("a", 30, 20),
                C("b", 30, 25),
                C("c", 30, 15),
            };
            var result = layout.Compute(children, 100, 200);
            Assert.Equal(new LayoutRect(0, 0, 30, 20), result["a"]);
            Assert.Equal(new LayoutRect(0, 25, 30, 25), result["b"]);
            Assert.Equal(new LayoutRect(0, 55, 30, 15), result["c"]);
        }

        [Fact]
        public void Vertical_wraps_to_new_column()
        {
            var layout = new FlowLayout(hgap: 10, vgap: 5, direction: FlowDirection.Vertical);
            var children = new List<ControlDescriptor>
            {
                C("a", 30, 60),
                C("b", 30, 60),
                C("c", 30, 60),
            };
            // Container height 130 fits a + b (60 + 5 + 60 = 125) but not c.
            // c wraps to a new column at x = 30 + 10 (hgap as column gap) = 40.
            var result = layout.Compute(children, 200, 130);
            Assert.Equal(new LayoutRect(0, 0, 30, 60), result["a"]);
            Assert.Equal(new LayoutRect(0, 65, 30, 60), result["b"]);
            Assert.Equal(new LayoutRect(40, 0, 30, 60), result["c"]);
        }

        [Fact]
        public void Wrap_disabled_keeps_packing_past_edge()
        {
            var layout = new FlowLayout(hgap: 0, vgap: 0, wrap: false);
            var children = new List<ControlDescriptor>
            {
                C("a", 60, 20),
                C("b", 60, 20),
                C("c", 60, 20),
            };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 60, 20), result["a"]);
            Assert.Equal(new LayoutRect(60, 0, 60, 20), result["b"]);
            Assert.Equal(new LayoutRect(120, 0, 60, 20), result["c"]);
        }
    }
}
