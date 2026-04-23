// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ControlBuilders"/>: type recognition,
    /// builder output shape, error handling for unknown types.
    /// </summary>
    public class ControlBuildersTests
    {
        [Theory]
        [InlineData("LABEL")]
        [InlineData("EDIT")]
        [InlineData("BUTTON")]
        [InlineData("CHECKBOX")]
        [InlineData("RADIO")]
        [InlineData("PANEL")]
        public void IsRecognizedType_accepts_known_types(string type)
        {
            Assert.True(ControlBuilders.IsRecognizedType(type));
        }

        [Theory]
        [InlineData("label")]
        [InlineData("Edit")]
        [InlineData("BuTToN")]
        public void IsRecognizedType_is_case_insensitive(string type)
        {
            Assert.True(ControlBuilders.IsRecognizedType(type));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("UNKNOWN")]
        [InlineData("WIDGET")]
        [InlineData("TEXTAREA")]
        public void IsRecognizedType_rejects_unknown(string? type)
        {
            Assert.False(ControlBuilders.IsRecognizedType(type));
        }

        [Fact]
        public void BuildAbsolute_populates_all_fields()
        {
            ControlDescriptor c = ControlBuilders.BuildAbsolute(
                "BUTTON", "ok", 10, 20, 80, 30, "OK");

            Assert.Equal("BUTTON", c.Type);
            Assert.Equal("ok", c.Id);
            Assert.Equal(10, c.X);
            Assert.Equal(20, c.Y);
            Assert.Equal(80, c.Width);
            Assert.Equal(30, c.Height);
            Assert.Equal("OK", c.Text);
            Assert.Empty(c.Properties);
        }

        [Fact]
        public void BuildAbsolute_throws_on_unknown_type()
        {
            Assert.Throws<ArgumentException>(() =>
                ControlBuilders.BuildAbsolute("WIDGET", "x", 0, 0, 10, 10, ""));
        }

        [Fact]
        public void RecognizedTypes_set_is_not_empty()
        {
            Assert.NotEmpty(ControlBuilders.RecognizedTypes);
            Assert.True(ControlBuilders.RecognizedTypes.Count >= 6);
        }
    }
}
