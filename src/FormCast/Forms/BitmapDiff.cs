// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/BitmapDiff.cs
// ===================
//
// Pixel-by-pixel bitmap comparison helper. Used by:
//
// - The @FORMSAVEIMAGE test suite to verify that rendering
// the same hidden Form twice produces the same image (a self-
// consistency test that does not depend on a stored golden).
//
// - Designer round-trip tests, where a synthetic event stream is
// replayed against a designer form and the resulting image is
// compared against a stored reference.
//
// - Documentation auto-generation, which renders worked examples
// to PNG and only re-publishes when the rendered output differs
// from the previously-published image.
//
// Design notes:
//
// - Per-channel tolerance lets callers ignore single-bit jitter
// from font hinting, alpha-blending, and the WinForms theme
// differences between Windows versions. Tolerance 0 means strict
// pixel equality.
//
// - Size mismatches short-circuit: we report SizeMismatch=true and
// do not attempt a comparison. Callers that want resampled diffs
// have to do the resample themselves.
//
// - The implementation uses Bitmap.GetPixel for clarity. The
// bitmaps used by FormCast tests are small (a few hundred pixels
// on a side), and the test suite runs once per build, so the
// LockBits speedup is not worth the extra complexity at this
// stage; revisit if profiling shows it matters.

using System;
using System.Drawing;

namespace FormCast.Forms
{
 /// <summary>
 /// Static helpers for comparing two <see cref="Bitmap"/> instances
 /// pixel-by-pixel with optional per-channel tolerance.
 /// </summary>
    internal static class BitmapDiff
    {
 /// <summary>
 /// Result of a <see cref="Compare(Bitmap, Bitmap, int)"/> call.
 /// </summary>
        public sealed class DiffResult
        {
 /// <summary>
 /// True if the two bitmaps were not the same dimensions.
 /// When set, <see cref="DifferingPixels"/> and
 /// <see cref="TotalPixels"/> are both <c>0</c> and no
 /// per-pixel comparison was performed.
 /// </summary>
            public bool SizeMismatch { get; }

 /// <summary>
 /// Number of pixels where at least one channel differed
 /// by more than the tolerance.
 /// </summary>
            public int DifferingPixels { get; }

 /// <summary>
 /// Total pixels compared (width * height).
 /// </summary>
            public int TotalPixels { get; }

 /// <summary>
 /// Differing pixels as a percentage of total. Returns
 /// <c>0</c> for empty or size-mismatched bitmaps.
 /// </summary>
            public double PercentDiffering =>
                TotalPixels == 0 ? 0.0 : 100.0 * DifferingPixels / TotalPixels;

 /// <summary>
 /// True if the bitmaps were identical (same size and zero
 /// differing pixels).
 /// </summary>
            public bool IsIdentical => !SizeMismatch && DifferingPixels == 0;

            internal DiffResult(bool sizeMismatch, int differingPixels, int totalPixels)
            {
                SizeMismatch = sizeMismatch;
                DifferingPixels = differingPixels;
                TotalPixels = totalPixels;
            }
        }

 /// <summary>
 /// Compare two bitmaps pixel by pixel.
 /// </summary>
 /// <param name="a">First bitmap.</param>
 /// <param name="b">Second bitmap.</param>
 /// <param name="channelTolerance">
 /// Maximum per-channel difference (R, G, B, A, all in 0..255)
 /// that still counts as identical. <c>0</c> means strict
 /// equality. Default <c>0</c>.
 /// </param>
 /// <returns>A <see cref="DiffResult"/> describing the comparison.</returns>
        public static DiffResult Compare(Bitmap a, Bitmap b, int channelTolerance = 0)
        {
            if (a is null) { throw new ArgumentNullException(nameof(a)); }
            if (b is null) { throw new ArgumentNullException(nameof(b)); }
            if (channelTolerance < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channelTolerance), "Tolerance must be non-negative.");
            }

            if (a.Width != b.Width || a.Height != b.Height)
            {
                return new DiffResult(sizeMismatch: true, differingPixels: 0, totalPixels: 0);
            }

            int width = a.Width;
            int height = a.Height;
            int total = width * height;
            int diff = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pa = a.GetPixel(x, y);
                    Color pb = b.GetPixel(x, y);
                    if (Math.Abs(pa.R - pb.R) > channelTolerance ||
                        Math.Abs(pa.G - pb.G) > channelTolerance ||
                        Math.Abs(pa.B - pb.B) > channelTolerance ||
                        Math.Abs(pa.A - pb.A) > channelTolerance)
                    {
                        diff++;
                    }
                }
            }

            return new DiffResult(sizeMismatch: false, differingPixels: diff, totalPixels: total);
        }
    }
}
