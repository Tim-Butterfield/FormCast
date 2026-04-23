// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

namespace FormCast.Forms
{
 /// <summary>
 /// In-memory description of a FormCast form. This is a logical-layer
 /// POCO; it does not own any WinForms object, HWND, or message pump.
 /// The <c>FormRealizer</c> is what turns a
 /// <see cref="FormDescriptor"/> into a real <c>System.Windows.Forms.Form</c>
 /// on the GUI host thread.
 /// </summary>
 /// <remarks>
 /// The design deliberately keeps the entire form-building API
 /// working in pure C# objects so the register/build/query/save
 /// flow can be fully exercised through xUnit and bridge BTMs
 /// without ever touching WinForms. The realizer layer is added
 /// later, and the descriptor is what flows between the two.
 /// </remarks>
    public sealed class FormDescriptor
    {
 /// <summary>
 /// Form type token from the first positional argument of
 /// <c>@FORMOPEN</c>. Recognized values include <c>"form"</c>
 /// (default), <c>"dialog"</c>, <c>"messagebox"</c>; the full
 /// list is documented in PLUGIN_DESIGN.md section 4.1. The
 /// The realizer uses this to pick a WinForms base class
 /// and default chrome.
 /// </summary>
        public string Type { get; set; } = "form";

 /// <summary>
 /// User-supplied name for the form. May be plain (e.g. <c>"settings"</c>)
 /// or scope-qualified (<c>"Local\\settings"</c>, <c>"Global\\foo"</c>,
 /// <c>"User\\sid\\bar"</c>). Used for <c>@FORMFIND</c> lookup.
 /// </summary>
        public string Name { get; set; } = string.Empty;

 /// <summary>
 /// Display title shown in the form's title bar when realized.
 /// Defaults to <see cref="Name"/> if not explicitly set.
 /// </summary>
        public string Title { get; set; } = string.Empty;

 /// <summary>X coordinate of the form's top-left corner, in pixels.</summary>
        public int X { get; set; }

 /// <summary>Y coordinate of the form's top-left corner, in pixels.</summary>
        public int Y { get; set; }

 /// <summary>Width of the form's client area, in pixels.</summary>
        public int Width { get; set; }

 /// <summary>Height of the form's client area, in pixels.</summary>
        public int Height { get; set; }

 /// <summary>
 /// Layout manager mode. Recognized values: <c>"absolute"</c>,
 /// <c>"flow"</c>, <c>"grid"</c>, <c>"dock"</c>. Defaults to
 /// <c>"absolute"</c>.
 /// </summary>
        public string LayoutMode { get; set; } = "absolute";

 /// <summary>
 /// Ordered list of controls owned by this form. Controls are
 /// added via <c>@FORMADD</c>; the descriptor is the source of
 /// truth for what controls exist and in what order. Layout
 /// managers consume this list when computing positions.
 /// </summary>
        public List<ControlDescriptor> Controls { get; } = new List<ControlDescriptor>();

 /// <summary>
 /// Free-form property bag for form-level attributes not covered
 /// by the strongly-typed properties above. Holds layout-manager
 /// configuration knobs that
 /// <see cref="Layouts.FormLayoutFactory"/> reads when
 /// <c>@FORMRELAYOUT</c> instantiates the right manager: e.g.
 /// <c>grid_rows</c>, <c>grid_cols</c>, <c>grid_hgap</c>,
 /// <c>flow_direction</c>, <c>dock_padding</c>. Case-insensitive
 /// keys to match <see cref="ControlDescriptor.Properties"/>.
 /// </summary>
        public Dictionary<string, string> Properties { get; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
