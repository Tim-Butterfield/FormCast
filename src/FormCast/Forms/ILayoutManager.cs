// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Collections.Generic;

namespace FormCast.Forms
{
 /// <summary>
 /// Pure-math layout manager. Given a list of <see cref="ControlDescriptor"/>
 /// children and a container size, computes the bounding rectangle each
 /// control should occupy. The logical layer never touches WinForms;
 /// the realizer takes the resulting bounds dictionary and applies
 /// them to real <c>Control</c> instances.
 /// </summary>
 /// <remarks>
 /// Layout managers are stateless. Per-form configuration (spacing,
 /// padding, grid dimensions, flow direction, etc.) is supplied via
 /// constructor parameters when the manager instance is created.
 /// Per-control configuration (<c>row=N</c>, <c>col=N</c>, <c>dock=top</c>,
 /// etc.) is read from each <see cref="ControlDescriptor.Properties"/>
 /// bag.
 /// </remarks>
    public interface ILayoutManager
    {
 /// <summary>
 /// Layout mode token. One of <c>"absolute"</c>, <c>"flow"</c>,
 /// <c>"grid"</c>, <c>"dock"</c>. Matches the value stored in
 /// <see cref="FormDescriptor.LayoutMode"/>.
 /// </summary>
        string Mode { get; }

 /// <summary>
 /// Compute bounds for every control in <paramref name="controls"/>.
 /// </summary>
 /// <param name="controls">
 /// Children to lay out, in declaration order. Order matters for
 /// flow and dock layouts; grid layouts use it as a fallback when
 /// a child has no <c>row</c>/<c>col</c> property.
 /// </param>
 /// <param name="containerWidth">Width of the container's client area.</param>
 /// <param name="containerHeight">Height of the container's client area.</param>
 /// <returns>
 /// A dictionary keyed by <see cref="ControlDescriptor.Id"/>, with
 /// each value being the computed <see cref="LayoutRect"/>. Every
 /// control in the input list appears exactly once. Controls with
 /// duplicate or empty IDs are still placed; the dictionary key
 /// for an empty ID falls back to a generated <c>"#index"</c> form.
 /// </returns>
        IReadOnlyDictionary<string, LayoutRect> Compute(
            IReadOnlyList<ControlDescriptor> controls,
            int containerWidth,
            int containerHeight);
    }
}
