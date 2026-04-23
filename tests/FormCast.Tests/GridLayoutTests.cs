// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

using FormCast.Forms;
using FormCast.Forms.Layouts;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="GridLayout"/>: equal-cell sizing,
    /// explicit row/col placement, auto-placement, rowspan/colspan,
    /// padding/gap math, edge cases.
    /// </summary>
    public class GridLayoutTests
    {
        private static ControlDescriptor C(string id, params (string k, string v)[] props)
        {
            var c = new ControlDescriptor { Type = "LABEL", Id = id };
            foreach (var (k, v) in props) { c.Properties[k] = v; }
            return c;
        }

        [Fact]
        public void Mode_is_grid()
        {
            Assert.Equal("grid", new GridLayout(2, 2).Mode);
        }

        [Fact]
        public void Empty_input_returns_empty_dictionary()
        {
            var result = new GridLayout(2, 2).Compute(new List<ControlDescriptor>(), 100, 100);
            Assert.Empty(result);
        }

        [Fact]
        public void Two_by_two_with_auto_placement_fills_left_to_right_top_to_bottom()
        {
            // 100 wide, 100 tall, no gaps, no padding -> cell 50x50.
            var layout = new GridLayout(rows: 2, cols: 2, hgap: 0, vgap: 0);
            var children = new List<ControlDescriptor>
            {
                C("a"), C("b"), C("c"), C("d"),
            };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 50, 50), result["a"]);
            Assert.Equal(new LayoutRect(50, 0, 50, 50), result["b"]);
            Assert.Equal(new LayoutRect(0, 50, 50, 50), result["c"]);
            Assert.Equal(new LayoutRect(50, 50, 50, 50), result["d"]);
        }

        [Fact]
        public void Explicit_row_and_col_position_correctly()
        {
            // 3x3 grid, no gaps, 90x90 -> cell 30x30.
            var layout = new GridLayout(3, 3, hgap: 0, vgap: 0);
            var children = new List<ControlDescriptor>
            {
                C("center", ("row", "1"), ("col", "1")),
                C("br",     ("row", "2"), ("col", "2")),
            };
            var result = layout.Compute(children, 90, 90);
            Assert.Equal(new LayoutRect(30, 30, 30, 30), result["center"]);
            Assert.Equal(new LayoutRect(60, 60, 30, 30), result["br"]);
        }

        [Fact]
        public void Gaps_offset_subsequent_cells()
        {
            // 2x2, hgap=10, vgap=20.
            // innerW = 100 - 0 - (1*10) = 90 -> cellW = 45
            // innerH = 100 - 0 - (1*20) = 80 -> cellH = 40
            var layout = new GridLayout(2, 2, hgap: 10, vgap: 20);
            var children = new List<ControlDescriptor> { C("a"), C("b"), C("c"), C("d") };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 45, 40), result["a"]);
            Assert.Equal(new LayoutRect(55, 0, 45, 40), result["b"]);    // 45 + 10
            Assert.Equal(new LayoutRect(0, 60, 45, 40), result["c"]);    // 40 + 20
            Assert.Equal(new LayoutRect(55, 60, 45, 40), result["d"]);
        }

        [Fact]
        public void Padding_offsets_origin_and_shrinks_cells()
        {
            // 2x2, padding=5, no gap.
            // innerW = 100 - 10 - 0 = 90 -> cellW = 45
            var layout = new GridLayout(2, 2, hgap: 0, vgap: 0, padding: 5);
            var children = new List<ControlDescriptor> { C("a") };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(5, 5, 45, 45), result["a"]);
        }

        [Fact]
        public void Colspan_widens_cell_including_internal_gap()
        {
            // 3 cols, hgap=10, vgap=0, container 100x100.
            // innerW = 100 - 0 - (2*10) = 80 -> cellW = 26 (floored from 80/3)
            var layout = new GridLayout(2, 3, hgap: 10, vgap: 0);
            var children = new List<ControlDescriptor>
            {
                C("wide", ("row", "0"), ("col", "0"), ("colspan", "2")),
            };
            var result = layout.Compute(children, 100, 100);
            // colspan=2: width = 2*26 + 1*10 = 62
            Assert.Equal(new LayoutRect(0, 0, 62, 50), result["wide"]);
        }

        [Fact]
        public void Rowspan_extends_cell_including_internal_gap()
        {
            // 3 rows, vgap=5, container 60x95.
            // innerH = 95 - 0 - (2*5) = 85 -> cellH = 28 (floored)
            var layout = new GridLayout(3, 2, hgap: 0, vgap: 5);
            var children = new List<ControlDescriptor>
            {
                C("tall", ("row", "0"), ("col", "0"), ("rowspan", "2")),
            };
            var result = layout.Compute(children, 60, 95);
            // rowspan=2: height = 2*28 + 1*5 = 61
            Assert.Equal(new LayoutRect(0, 0, 30, 61), result["tall"]);
        }

        [Fact]
        public void Auto_placement_skips_explicitly_occupied_cells()
        {
            // 2x2 grid, no gap, 100x100.
            var layout = new GridLayout(2, 2, hgap: 0, vgap: 0);
            var children = new List<ControlDescriptor>
            {
                C("explicit", ("row", "0"), ("col", "1")),
                C("auto1"),  // first auto -> (0,0)
                C("auto2"),  // (0,1) is occupied -> (1,0)
                C("auto3"),  // -> (1,1)
            };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(50, 0, 50, 50), result["explicit"]);
            Assert.Equal(new LayoutRect(0, 0, 50, 50), result["auto1"]);
            Assert.Equal(new LayoutRect(0, 50, 50, 50), result["auto2"]);
            Assert.Equal(new LayoutRect(50, 50, 50, 50), result["auto3"]);
        }

        [Fact]
        public void Rows_and_cols_are_clamped_to_minimum_one()
        {
            var layout = new GridLayout(0, 0);
            var children = new List<ControlDescriptor> { C("a") };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 100, 100), result["a"]);
        }

        [Fact]
        public void Empty_id_falls_back_to_index_key()
        {
            var layout = new GridLayout(1, 2, hgap: 0, vgap: 0);
            var children = new List<ControlDescriptor> { C(""), C("") };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("#0"));
            Assert.True(result.ContainsKey("#1"));
        }
    }
}
