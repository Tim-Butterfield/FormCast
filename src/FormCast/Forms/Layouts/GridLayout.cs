// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;
using System.Globalization;

namespace FormCast.Forms.Layouts
{
 /// <summary>
 /// Cell-based layout: the container is divided into a fixed
 /// <c>Rows x Cols</c> grid of equally sized cells, and children are
 /// placed at <c>row=N|col=N|rowspan=N|colspan=N</c> coordinates read
 /// from each control's <see cref="ControlDescriptor.Properties"/> bag.
 /// Children that omit <c>row</c> / <c>col</c> are auto-placed in the
 /// next free cell, scanning left-to-right then top-to-bottom.
 /// </summary>
 /// <remarks>
 /// This is the math layer for <c>TableLayoutPanel</c>. It deliberately
 /// supports only the equal-sized-cells case (matching the simplest
 /// configuration of <c>TableLayoutPanel</c>); the richer mix of
 /// <c>fixed</c> / <c>auto</c> / <c>fill</c> column and row sizing
 /// described in PLUGIN_DESIGN.md section 4.7 is deferred until
 /// after the realizer lands and we can drive a real
 /// <c>TableLayoutPanel</c> for ground-truth comparison.
 /// </remarks>
    public sealed class GridLayout : ILayoutManager
    {
        private readonly int _rows;
        private readonly int _cols;
        private readonly int _hgap;
        private readonly int _vgap;
        private readonly int _padding;

 /// <summary>
 /// Construct a grid layout.
 /// </summary>
 /// <param name="rows">Number of rows. Clamped to a minimum of 1.</param>
 /// <param name="cols">Number of columns. Clamped to a minimum of 1.</param>
 /// <param name="hgap">Horizontal gap between adjacent columns. Default 4.</param>
 /// <param name="vgap">Vertical gap between adjacent rows. Default 4.</param>
 /// <param name="padding">Padding around the container's interior on all four sides. Default 0.</param>
        public GridLayout(
            int rows,
            int cols,
            int hgap = 4,
            int vgap = 4,
            int padding = 0)
        {
            _rows = rows < 1 ? 1 : rows;
            _cols = cols < 1 ? 1 : cols;
            _hgap = hgap;
            _vgap = vgap;
            _padding = padding;
        }

 /// <inheritdoc/>
        public string Mode => "grid";

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

 // Compute cell size. Use floating subtraction then floor; any
 // rounding remainder ends up as unused space at the right /
 // bottom edge of the grid. Floor (not round) so that
 // colspan*cellW never exceeds the available width.
            int innerW = containerWidth - (2 * _padding) - ((_cols - 1) * _hgap);
            int innerH = containerHeight - (2 * _padding) - ((_rows - 1) * _vgap);
            int cellW = innerW > 0 ? innerW / _cols : 0;
            int cellH = innerH > 0 ? innerH / _rows : 0;

 // Track occupied cells for auto-placement. Index = row*cols + col.
            bool[] occupied = new bool[_rows * _cols];
            int autoCursor = 0;

            for (int i = 0; i < controls.Count; i++)
            {
                ControlDescriptor c = controls[i];

                int row, col;
                int rowspan = ReadIntProp(c, "rowspan", 1);
                int colspan = ReadIntProp(c, "colspan", 1);
                if (rowspan < 1) { rowspan = 1; }
                if (colspan < 1) { colspan = 1; }

                int explicitRow = ReadIntProp(c, "row", -1);
                int explicitCol = ReadIntProp(c, "col", -1);
                if (explicitRow >= 0 && explicitCol >= 0)
                {
                    row = explicitRow;
                    col = explicitCol;
                }
                else
                {
 // Auto-place into the next free cell. Skip cells that
 // are already occupied or where the rowspan/colspan
 // would extend off the grid.
                    int found = -1;
                    for (int slot = autoCursor; slot < occupied.Length; slot++)
                    {
                        int r = slot / _cols;
                        int co = slot % _cols;
                        if (occupied[slot]) { continue; }
                        if (r + rowspan > _rows) { continue; }
                        if (co + colspan > _cols) { continue; }
                        found = slot;
                        break;
                    }
                    if (found < 0)
                    {
 // Out of cells: place at the bottom-right corner
 // of the grid with zero size. The dictionary still
 // contains an entry so callers can detect overflow
 // by checking the rect.
                        row = _rows - 1;
                        col = _cols - 1;
                    }
                    else
                    {
                        row = found / _cols;
                        col = found % _cols;
                        autoCursor = found + 1;
                    }
                }

 // Mark the cells we just consumed (for auto-placement
 // bookkeeping). Out-of-grid spans are silently clipped.
                for (int rr = row; rr < row + rowspan && rr < _rows; rr++)
                {
                    for (int cc = col; cc < col + colspan && cc < _cols; cc++)
                    {
                        if (rr >= 0 && cc >= 0)
                        {
                            occupied[(rr * _cols) + cc] = true;
                        }
                    }
                }

                int x = _padding + (col * (cellW + _hgap));
                int y = _padding + (row * (cellH + _vgap));
                int w = (colspan * cellW) + ((colspan - 1) * _hgap);
                int h = (rowspan * cellH) + ((rowspan - 1) * _vgap);

                string key = string.IsNullOrEmpty(c.Id)
                    ? "#" + i.ToString(CultureInfo.InvariantCulture)
                    : c.Id;
                result[key] = new LayoutRect(x, y, w, h);
            }

            return result;
        }

        private static int ReadIntProp(ControlDescriptor c, string name, int defaultValue)
        {
            if (!c.Properties.TryGetValue(name, out string? raw) || string.IsNullOrEmpty(raw))
            {
                return defaultValue;
            }
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v
                : defaultValue;
        }
    }
}
