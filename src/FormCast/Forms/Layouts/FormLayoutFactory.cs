// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Globalization;

namespace FormCast.Forms.Layouts
{
 /// <summary>
 /// Helper that maps a <see cref="FormDescriptor"/> to the concrete
 /// <see cref="ILayoutManager"/> implementation it should use,
 /// reading layout-manager configuration knobs from
 /// <see cref="FormDescriptor.Properties"/>. Each layout family has
 /// its own prefix in the property bag (<c>grid_*</c>, <c>flow_*</c>,
 /// <c>dock_*</c>); missing knobs fall back to sensible defaults.
 /// </summary>
 /// <remarks>
 /// <para>The factory is a static helper rather than a DI-style
 /// instance so the dispatch path stays allocation-light.</para>
 ///
 /// <para>Recognized property keys (case-insensitive, prefix
 /// indicates which layout mode reads them):</para>
 /// <list type="bullet">
 /// <item><description><c>grid_rows</c>, <c>grid_cols</c>,
 /// <c>grid_hgap</c>, <c>grid_vgap</c>, <c>grid_padding</c></description></item>
 /// <item><description><c>flow_hgap</c>, <c>flow_vgap</c>,
 /// <c>flow_padding</c>, <c>flow_direction</c>
 /// (<c>horizontal</c>|<c>vertical</c>), <c>flow_wrap</c>
 /// (<c>true</c>|<c>false</c>)</description></item>
 /// <item><description><c>dock_padding</c></description></item>
 /// </list>
 ///
 /// <para>The absolute layout takes no configuration; it is
 /// instantiated paramless and ignores the property bag.</para>
 /// </remarks>
    public static class FormLayoutFactory
    {
 /// <summary>
 /// Construct an <see cref="ILayoutManager"/> appropriate for
 /// <paramref name="form"/>'s <see cref="FormDescriptor.LayoutMode"/>,
 /// reading per-mode configuration from
 /// <see cref="FormDescriptor.Properties"/>. An unrecognized
 /// or empty <c>LayoutMode</c> falls back to
 /// <see cref="AbsoluteLayout"/>.
 /// </summary>
        public static ILayoutManager Create(FormDescriptor form)
        {
            if (form is null) { throw new ArgumentNullException(nameof(form)); }

            string mode = (form.LayoutMode ?? string.Empty).Trim().ToLowerInvariant();
            switch (mode)
            {
                case "grid":
                {
 // Defaults: enough rows for one control per row,
 // single column. This produces a stacked vertical
 // layout when no explicit rows/cols are set, which
 // is the most common "I have N controls, lay them
 // out top to bottom" use case.
                    int rows = ReadInt(form, "grid_rows", form.Controls.Count > 0 ? form.Controls.Count : 1);
                    int cols = ReadInt(form, "grid_cols", 1);
                    int hgap = ReadInt(form, "grid_hgap", 4);
                    int vgap = ReadInt(form, "grid_vgap", 4);
                    int padding = ReadInt(form, "grid_padding", 0);
                    return new GridLayout(rows, cols, hgap, vgap, padding);
                }

                case "flow":
                {
                    int hgap = ReadInt(form, "flow_hgap", 4);
                    int vgap = ReadInt(form, "flow_vgap", 4);
                    int padding = ReadInt(form, "flow_padding", 0);
                    FlowDirection direction = ReadString(form, "flow_direction", "horizontal")
                        .Equals("vertical", StringComparison.OrdinalIgnoreCase)
                            ? FlowDirection.Vertical
                            : FlowDirection.Horizontal;
                    bool wrap = ReadBool(form, "flow_wrap", defaultValue: true);
                    return new FlowLayout(hgap, vgap, padding, direction, wrap);
                }

                case "dock":
                {
                    int padding = ReadInt(form, "dock_padding", 0);
                    return new DockLayout(padding);
                }

                case "absolute":
                case "":
                default:
                    return new AbsoluteLayout();
            }
        }

        private static int ReadInt(FormDescriptor form, string key, int fallback)
        {
            if (!form.Properties.TryGetValue(key, out string? raw) || string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : fallback;
        }

        private static string ReadString(FormDescriptor form, string key, string fallback)
        {
            return form.Properties.TryGetValue(key, out string? raw) && raw is not null
                ? raw
                : fallback;
        }

        private static bool ReadBool(FormDescriptor form, string key, bool defaultValue)
        {
            if (!form.Properties.TryGetValue(key, out string? raw) || string.IsNullOrEmpty(raw))
            {
                return defaultValue;
            }
 // Accept the obvious truth values; everything else falls
 // back to the default rather than throwing.
            switch (raw!.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    return defaultValue;
            }
        }
    }
}
