// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

namespace FormCast.Forms
{
 /// <summary>
 /// In-memory description of a single control inside a
 /// <see cref="FormDescriptor"/>. Like its parent, it is a pure POCO
 /// with no WinForms references; the realizer layer
 /// converts it into an actual <c>System.Windows.Forms.Control</c>.
 /// </summary>
 /// <remarks>
 /// The property bag in <see cref="Properties"/> is the extension
 /// point for control-type-specific attributes. The well-known
 /// strongly-typed properties (<see cref="Type"/>, <see cref="Id"/>,
 /// position, size, text) are convenience accessors for the
 /// universal subset; everything else lives in the bag.
 /// </remarks>
    public sealed class ControlDescriptor
    {
 /// <summary>
 /// Control type token (e.g. <c>"LABEL"</c>, <c>"EDIT"</c>,
 /// <c>"BUTTON"</c>, <c>"PANEL"</c>). Case is preserved as supplied
 /// but comparisons are case-insensitive.
 /// </summary>
        public string Type { get; set; } = string.Empty;

 /// <summary>
 /// Unique identifier for this control within its owning form.
 /// Used by <c>@FORMSET</c>, <c>@FORMGET</c>, <c>@FORMBIND</c>, etc.
 /// </summary>
        public string Id { get; set; } = string.Empty;

 /// <summary>X coordinate within the parent container.</summary>
        public int X { get; set; }

 /// <summary>Y coordinate within the parent container.</summary>
        public int Y { get; set; }

 /// <summary>Width in pixels.</summary>
        public int Width { get; set; }

 /// <summary>Height in pixels.</summary>
        public int Height { get; set; }

 /// <summary>
 /// Display text. For LABEL/BUTTON/CHECKBOX this is the caption;
 /// for EDIT it is the initial text; for PANEL it is unused.
 /// </summary>
        public string Text { get; set; } = string.Empty;

 /// <summary>
 /// Free-form property bag for control-type-specific attributes
 /// not covered by the strongly-typed properties above. Examples:
 /// <c>"checked" -> "1"</c>, <c>"readonly" -> "1"</c>,
 /// <c>"dock" -> "top"</c>, <c>"row" -> "3"</c>.
 /// </summary>
        public Dictionary<string, string> Properties { get; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

 /// <summary>
 /// Child controls owned by this control. Only meaningful
 /// for container types (PANEL); other control types leave the
 /// list empty. Children may themselves contain children, so
 /// nesting is arbitrarily deep. The realizer recurses through
 /// this collection when building the WinForms tree.
 /// </summary>
        public List<ControlDescriptor> Children { get; } = new List<ControlDescriptor>();
    }
}
