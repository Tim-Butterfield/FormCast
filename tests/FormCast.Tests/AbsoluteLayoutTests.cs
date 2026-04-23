// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

using FormCast.Forms;
using FormCast.Forms.Layouts;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AbsoluteLayout"/>: bounds are passed
    /// through unchanged from each <see cref="ControlDescriptor"/>; the
    /// container size is irrelevant.
    /// </summary>
    public class AbsoluteLayoutTests
    {
        private static ControlDescriptor C(string id, int x, int y, int w, int h)
        {
            return new ControlDescriptor { Type = "LABEL", Id = id, X = x, Y = y, Width = w, Height = h };
        }

        [Fact]
        public void Mode_is_absolute()
        {
            Assert.Equal("absolute", new AbsoluteLayout().Mode);
        }

        [Fact]
        public void Empty_input_returns_empty_dictionary()
        {
            var layout = new AbsoluteLayout();
            var result = layout.Compute(new List<ControlDescriptor>(), 100, 100);
            Assert.Empty(result);
        }

        [Fact]
        public void Single_control_keeps_its_bounds()
        {
            var layout = new AbsoluteLayout();
            var children = new List<ControlDescriptor> { C("a", 10, 20, 80, 30) };
            var result = layout.Compute(children, 400, 300);
            Assert.Single(result);
            Assert.Equal(new LayoutRect(10, 20, 80, 30), result["a"]);
        }

        [Fact]
        public void Multiple_controls_keep_independent_positions()
        {
            var layout = new AbsoluteLayout();
            var children = new List<ControlDescriptor>
            {
                C("a", 0, 0, 50, 20),
                C("b", 100, 50, 75, 25),
                C("c", -10, -5, 200, 200),
            };
            var result = layout.Compute(children, 400, 300);
            Assert.Equal(3, result.Count);
            Assert.Equal(new LayoutRect(0, 0, 50, 20), result["a"]);
            Assert.Equal(new LayoutRect(100, 50, 75, 25), result["b"]);
            Assert.Equal(new LayoutRect(-10, -5, 200, 200), result["c"]);
        }

        [Fact]
        public void Container_size_is_ignored_oversize_controls_pass_through()
        {
            var layout = new AbsoluteLayout();
            var children = new List<ControlDescriptor> { C("big", 0, 0, 9999, 9999) };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(new LayoutRect(0, 0, 9999, 9999), result["big"]);
        }

        [Fact]
        public void Empty_id_falls_back_to_index_key()
        {
            var layout = new AbsoluteLayout();
            var children = new List<ControlDescriptor>
            {
                C("", 0, 0, 10, 10),
                C("", 20, 20, 10, 10),
            };
            var result = layout.Compute(children, 100, 100);
            Assert.Equal(2, result.Count);
            Assert.Equal(new LayoutRect(0, 0, 10, 10), result["#0"]);
            Assert.Equal(new LayoutRect(20, 20, 10, 10), result["#1"]);
        }
    }
}
