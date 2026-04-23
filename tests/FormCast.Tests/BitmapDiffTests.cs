// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Drawing;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Tests for <see cref="BitmapDiff"/>: pixel-equality, tolerance,
    /// size mismatch, and argument validation.
    /// </summary>
    public class BitmapDiffTests
    {
        private static Bitmap SolidColor(int width, int height, Color color)
        {
            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(color);
            return bmp;
        }

        [Fact]
        public void Identical_bitmaps_report_zero_differing_pixels()
        {
            using var a = SolidColor(10, 10, Color.Red);
            using var b = SolidColor(10, 10, Color.Red);
            var result = BitmapDiff.Compare(a, b);
            Assert.False(result.SizeMismatch);
            Assert.Equal(0, result.DifferingPixels);
            Assert.Equal(100, result.TotalPixels);
            Assert.True(result.IsIdentical);
            Assert.Equal(0.0, result.PercentDiffering);
        }

        [Fact]
        public void Different_solid_colors_report_all_pixels_differing()
        {
            using var a = SolidColor(8, 8, Color.Red);
            using var b = SolidColor(8, 8, Color.Blue);
            var result = BitmapDiff.Compare(a, b);
            Assert.False(result.SizeMismatch);
            Assert.Equal(64, result.DifferingPixels);
            Assert.Equal(64, result.TotalPixels);
            Assert.False(result.IsIdentical);
            Assert.Equal(100.0, result.PercentDiffering);
        }

        [Fact]
        public void Tolerance_absorbs_small_per_channel_differences()
        {
            using var a = SolidColor(4, 4, Color.FromArgb(100, 100, 100));
            using var b = SolidColor(4, 4, Color.FromArgb(102, 100, 99));

            var strict = BitmapDiff.Compare(a, b, channelTolerance: 0);
            Assert.Equal(16, strict.DifferingPixels);

            var lenient = BitmapDiff.Compare(a, b, channelTolerance: 5);
            Assert.Equal(0, lenient.DifferingPixels);
            Assert.True(lenient.IsIdentical);
        }

        [Fact]
        public void Tolerance_does_not_absorb_changes_above_threshold()
        {
            using var a = SolidColor(4, 4, Color.FromArgb(100, 100, 100));
            using var b = SolidColor(4, 4, Color.FromArgb(120, 100, 100));
            var result = BitmapDiff.Compare(a, b, channelTolerance: 5);
            Assert.Equal(16, result.DifferingPixels);
        }

        [Fact]
        public void Size_mismatch_short_circuits()
        {
            using var a = SolidColor(10, 10, Color.Black);
            using var b = SolidColor(10, 11, Color.Black);
            var result = BitmapDiff.Compare(a, b);
            Assert.True(result.SizeMismatch);
            Assert.Equal(0, result.DifferingPixels);
            Assert.Equal(0, result.TotalPixels);
            Assert.False(result.IsIdentical);
        }

        [Fact]
        public void Single_pixel_diff_in_otherwise_identical_bitmap()
        {
            using var a = SolidColor(5, 5, Color.White);
            using var b = SolidColor(5, 5, Color.White);
            b.SetPixel(2, 2, Color.Black);
            var result = BitmapDiff.Compare(a, b);
            Assert.Equal(1, result.DifferingPixels);
            Assert.Equal(25, result.TotalPixels);
            Assert.Equal(4.0, result.PercentDiffering);
        }

        [Fact]
        public void Null_bitmap_throws_argument_null()
        {
            using var b = SolidColor(2, 2, Color.White);
            Assert.Throws<ArgumentNullException>(() => BitmapDiff.Compare(null!, b));
            Assert.Throws<ArgumentNullException>(() => BitmapDiff.Compare(b, null!));
        }

        [Fact]
        public void Negative_tolerance_throws()
        {
            using var a = SolidColor(2, 2, Color.White);
            using var b = SolidColor(2, 2, Color.White);
            Assert.Throws<ArgumentOutOfRangeException>(() => BitmapDiff.Compare(a, b, channelTolerance: -1));
        }
    }
}
