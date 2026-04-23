// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace FormCast.Forms.Layouts
{
 /// <summary>
 /// Edge-attachment layout: each child has a <c>dock</c> property
 /// (<c>top</c>, <c>bottom</c>, <c>left</c>, <c>right</c>, or
 /// <c>fill</c>) and is attached to the corresponding edge of the
 /// remaining interior rectangle, consuming a strip of the matching
 /// dimension. Children are processed in declaration order, so the
 /// first declared docks against the outermost edge. The "classic IDE
 /// layout" of toolbar + status bar + sidebars + main area.
 /// </summary>
 /// <remarks>
 /// For top/bottom strips the child's <see cref="ControlDescriptor.Height"/>
 /// is consumed; for left/right strips its <see cref="ControlDescriptor.Width"/>.
 /// Width on a top/bottom strip is forced to the full remaining width
 /// (and vice-versa) -- that's the entire point of docking. A
 /// <c>fill</c> child consumes everything that is left; any subsequent
 /// children get a zero-sized rectangle. Declaration order matches
 /// the natural BTM authoring order, which is the inverse of the
 /// WinForms add-in-reverse-z-order rule but produces the same visual
 /// result for a given layout.
 /// </remarks>
    public sealed class DockLayout : ILayoutManager
    {
        private readonly int _padding;

 /// <summary>
 /// Construct a dock layout.
 /// </summary>
 /// <param name="padding">Padding around the container's interior on all four sides. Default 0.</param>
        public DockLayout(int padding = 0)
        {
            _padding = padding;
        }

 /// <inheritdoc/>
        public string Mode => "dock";

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

            int rx = _padding;
            int ry = _padding;
            int rw = containerWidth - (2 * _padding);
            int rh = containerHeight - (2 * _padding);
            if (rw < 0) { rw = 0; }
            if (rh < 0) { rh = 0; }

            for (int i = 0; i < controls.Count; i++)
            {
                ControlDescriptor c = controls[i];
                string dock = ReadDockProp(c);

                int x, y, w, h;
                switch (dock)
                {
                    case "top":
                    {
                        int strip = Math.Min(c.Height, rh);
                        x = rx; y = ry; w = rw; h = strip;
                        ry += strip;
                        rh -= strip;
                        break;
                    }
                    case "bottom":
                    {
                        int strip = Math.Min(c.Height, rh);
                        x = rx; y = ry + rh - strip; w = rw; h = strip;
                        rh -= strip;
                        break;
                    }
                    case "left":
                    {
                        int strip = Math.Min(c.Width, rw);
                        x = rx; y = ry; w = strip; h = rh;
                        rx += strip;
                        rw -= strip;
                        break;
                    }
                    case "right":
                    {
                        int strip = Math.Min(c.Width, rw);
                        x = rx + rw - strip; y = ry; w = strip; h = rh;
                        rw -= strip;
                        break;
                    }
                    case "fill":
                    default:
                    {
                        x = rx; y = ry; w = rw; h = rh;
                        rx += rw;
                        ry += rh;
                        rw = 0;
                        rh = 0;
                        break;
                    }
                }

                string key = string.IsNullOrEmpty(c.Id)
                    ? "#" + i.ToString(CultureInfo.InvariantCulture)
                    : c.Id;
                result[key] = new LayoutRect(x, y, w, h);
            }

            return result;
        }

        private static string ReadDockProp(ControlDescriptor c)
        {
            if (c.Properties.TryGetValue("dock", out string? raw) && !string.IsNullOrEmpty(raw))
            {
                string lowered = raw!.Trim().ToLowerInvariant();
                switch (lowered)
                {
                    case "top":
                    case "bottom":
                    case "left":
                    case "right":
                    case "fill":
                        return lowered;
                    default:
                        return "top";
                }
            }
            return "top";
        }
    }
}
