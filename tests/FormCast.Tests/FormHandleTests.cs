// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FormHandle"/>: format and parse round
    /// trips, malformed input rejection, scope-prefix enforcement.
    /// </summary>
    public class FormHandleTests
    {
        [Fact]
        public void Format_produces_expected_shape()
        {
            string handle = FormHandle.Format(42);
            // Shape: L:<digits>:42
            string[] parts = handle.Split(':');
            Assert.Equal(3, parts.Length);
            Assert.Equal("L", parts[0]);
            Assert.True(int.TryParse(parts[1], out int pid) && pid > 0);
            Assert.Equal("42", parts[2]);
        }

        [Fact]
        public void Format_then_TryParse_round_trips()
        {
            for (int seq = 1; seq < 100; seq++)
            {
                string handle = FormHandle.Format(seq);
                Assert.True(FormHandle.TryParse(handle, out int parsedSeq));
                Assert.Equal(seq, parsedSeq);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-handle")]
        [InlineData("L:1234")]                  // missing seq segment
        [InlineData("L:1234:5:extra")]          // too many segments
        [InlineData("G:42")]                    // wrong scope (global, not yet supported)
        [InlineData("X:1234:5")]                // unknown scope
        [InlineData("L:abc:5")]                 // non-numeric pid
        [InlineData("L:1234:notanint")]         // non-numeric seq
        public void TryParse_rejects_malformed(string? handle)
        {
            Assert.False(FormHandle.TryParse(handle, out _));
        }

        [Fact]
        public void TryParse_returns_minus_one_seq_on_failure()
        {
            FormHandle.TryParse("garbage", out int seq);
            Assert.Equal(-1, seq);
        }
    }
}
