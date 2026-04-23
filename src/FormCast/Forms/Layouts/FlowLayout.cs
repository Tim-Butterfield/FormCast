// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;
using System.Globalization;

namespace FormCast.Forms.Layouts
{
 /// <summary>
 /// Flow direction for <see cref="FlowLayout"/>.
 /// </summary>
    public enum FlowDirection
    {
 /// <summary>Children flow left-to-right and wrap to a new row at the right edge.</summary>
        Horizontal = 0,

 /// <summary>Children flow top-to-bottom and wrap to a new column at the bottom edge.</summary>
        Vertical = 1,
    }

 /// <summary>
 /// Children flow in declaration order along the major axis, wrapping
 /// when the next child would exceed the container's edge. Each child
 /// keeps the width and height it brings in via its
 /// <see cref="ControlDescriptor"/>.
 /// </summary>
 /// <remarks>
 /// This is the math layer for <c>FlowLayoutPanel</c>. The realizer
 /// picks <c>FlowLayoutPanel</c> as the backing control, but the
 /// bounds returned here are what the descriptor round-trip and the
 /// headless layout tests use.
 /// </remarks>
    public sealed class FlowLayout : ILayoutManager
    {
        private readonly int _hgap;
        private readonly int _vgap;
        private readonly int _padding;
        private readonly FlowDirection _direction;
        private readonly bool _wrap;

 /// <summary>
 /// Construct a flow layout.
 /// </summary>
 /// <param name="hgap">Horizontal gap between adjacent items in the same row. Default 4.</param>
 /// <param name="vgap">Vertical gap between rows when wrapping. Default 4.</param>
 /// <param name="padding">Padding around the container's interior on all four sides. Default 0.</param>
 /// <param name="direction">Major axis direction. Default <see cref="FlowDirection.Horizontal"/>.</param>
 /// <param name="wrap">Whether to wrap when the next item would exceed the container edge. Default <see langword="true"/>.</param>
        public FlowLayout(
            int hgap = 4,
            int vgap = 4,
            int padding = 0,
            FlowDirection direction = FlowDirection.Horizontal,
            bool wrap = true)
        {
            _hgap = hgap;
            _vgap = vgap;
            _padding = padding;
            _direction = direction;
            _wrap = wrap;
        }

 /// <inheritdoc/>
        public string Mode => "flow";

 /// <inheritdoc/>
        public IReadOnlyDictionary<string, LayoutRect> Compute(
            IReadOnlyList<ControlDescriptor> controls,
            int containerWidth,
            int containerHeight)
        {
            var result = new Dictionary<string, LayoutRect>(controls.Count);
            if (controls.Count == 0)
            {
                return result;
            }

 // The two directions are mirror images of each other. Implement
 // horizontal in primary code and transpose for vertical so the
 // wrap math only lives in one place.
            bool horizontal = _direction == FlowDirection.Horizontal;
            int majorLimit = horizontal ? containerWidth : containerHeight;
            int innerLimit = majorLimit - _padding;
 // Inter-item gap is along the major axis; inter-line gap is along the minor axis.
 // For horizontal flow: itemGap=hgap, lineGap=vgap. For vertical: swapped.
            int itemGap = horizontal ? _hgap : _vgap;
            int lineGap = horizontal ? _vgap : _hgap;

            int cursorMajor = _padding;   // x for horizontal, y for vertical
            int cursorMinor = _padding;   // y for horizontal, x for vertical
            int currentLineExtent = 0;    // max minor-axis size of items on this line
            bool lineHasItems = false;

            for (int i = 0; i < controls.Count; i++)
            {
                ControlDescriptor c = controls[i];
                int majorSize = horizontal ? c.Width : c.Height;
                int minorSize = horizontal ? c.Height : c.Width;

 // Wrap if (a) wrapping enabled, (b) line already has at
 // least one item (we always place the first item on a
 // line even if it overflows), and (c) appending would
 // cross the inner edge.
                if (_wrap && lineHasItems && (cursorMajor + majorSize) > innerLimit)
                {
                    cursorMinor += currentLineExtent + lineGap;
                    cursorMajor = _padding;
                    currentLineExtent = 0;
                    lineHasItems = false;
                }

                int x = horizontal ? cursorMajor : cursorMinor;
                int y = horizontal ? cursorMinor : cursorMajor;

                string key = string.IsNullOrEmpty(c.Id)
                    ? "#" + i.ToString(CultureInfo.InvariantCulture)
                    : c.Id;
                result[key] = new LayoutRect(x, y, c.Width, c.Height);

                cursorMajor += majorSize + itemGap;
                if (minorSize > currentLineExtent)
                {
                    currentLineExtent = minorSize;
                }
                lineHasItems = true;
            }

            return result;
        }
    }
}
