// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;
using System.Globalization;

namespace FormCast.Forms.Layouts
{
 /// <summary>
 /// Pass-through layout: each control's bounds are taken directly from
 /// its <see cref="ControlDescriptor.X"/> / Y / Width / Height properties.
 /// The container size is ignored; controls may extend past the
 /// container edge or sit at negative coordinates. This is the default
 /// for forms created via <c>@FORMOPEN</c>.
 /// </summary>
    public sealed class AbsoluteLayout : ILayoutManager
    {
 /// <inheritdoc/>
        public string Mode => "absolute";

 /// <inheritdoc/>
        public IReadOnlyDictionary<string, LayoutRect> Compute(
            IReadOnlyList<ControlDescriptor> controls,
            int containerWidth,
            int containerHeight)
        {
            var result = new Dictionary<string, LayoutRect>(controls.Count);
            for (int i = 0; i < controls.Count; i++)
            {
                ControlDescriptor c = controls[i];
                string key = string.IsNullOrEmpty(c.Id)
                    ? "#" + i.ToString(CultureInfo.InvariantCulture)
                    : c.Id;
                result[key] = new LayoutRect(c.X, c.Y, c.Width, c.Height);
            }
            return result;
        }
    }
}
