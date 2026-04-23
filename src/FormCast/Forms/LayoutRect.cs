// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Globalization;

namespace FormCast.Forms
{
 /// <summary>
 /// Immutable rectangle used by the logical layout managers. Defined
 /// here (rather than reusing <c>System.Drawing.Rectangle</c>) so the
 /// logical layer has zero dependency on GDI+ or WinForms. The realizer
 /// The realizer is what bridges these to <c>System.Drawing</c> when
 /// constructing real controls.
 /// </summary>
    public readonly struct LayoutRect : IEquatable<LayoutRect>
    {
 /// <summary>X coordinate of the top-left corner.</summary>
        public int X { get; }

 /// <summary>Y coordinate of the top-left corner.</summary>
        public int Y { get; }

 /// <summary>Width in pixels. May be zero or negative for degenerate inputs.</summary>
        public int Width { get; }

 /// <summary>Height in pixels. May be zero or negative for degenerate inputs.</summary>
        public int Height { get; }

 /// <summary>Construct a rectangle with the given top-left corner and size.</summary>
        public LayoutRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

 /// <summary>X + Width.</summary>
        public int Right => X + Width;

 /// <summary>Y + Height.</summary>
        public int Bottom => Y + Height;

 /// <inheritdoc/>
        public bool Equals(LayoutRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

 /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is LayoutRect other && Equals(other);
        }

 /// <inheritdoc/>
        public override int GetHashCode()
        {
 // net48 has no HashCode.Combine; use a stable manual mix.
            unchecked
            {
                int h = 17;
                h = (h * 31) + X;
                h = (h * 31) + Y;
                h = (h * 31) + Width;
                h = (h * 31) + Height;
                return h;
            }
        }

 /// <summary>Equality operator.</summary>
        public static bool operator ==(LayoutRect a, LayoutRect b) => a.Equals(b);

 /// <summary>Inequality operator.</summary>
        public static bool operator !=(LayoutRect a, LayoutRect b) => !a.Equals(b);

 /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0},{1} {2}x{3})",
                X, Y, Width, Height);
        }
    }
}
