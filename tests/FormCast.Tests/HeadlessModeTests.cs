// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FormCast.HeadlessMode"/>. Verifies the
    /// FORMCAST_HEADLESS env var parsing and the truthy-value table.
    /// These tests do not load the plugin into TCC; they exercise the
    /// pure parsing logic directly.
    /// </summary>
    public class HeadlessModeTests
    {
        [Theory]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("True")]
        [InlineData("TRUE")]
        [InlineData("yes")]
        [InlineData("YES")]
        [InlineData("on")]
        [InlineData("ON")]
        [InlineData(" 1 ")]              // surrounding whitespace
        [InlineData("\ttrue\t")]         // tabs around value
        public void ParseTruthy_recognizes_truthy_values(string raw)
        {
            Assert.True(global::FormCast.HeadlessMode.ParseTruthy(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("no")]
        [InlineData("off")]
        [InlineData("2")]                // not on the truthy list
        [InlineData("enabled")]          // not on the truthy list
        [InlineData("y")]                // close but not "yes"
        public void ParseTruthy_recognizes_falsy_values(string? raw)
        {
            Assert.False(global::FormCast.HeadlessMode.ParseTruthy(raw));
        }

        [Fact]
        public void Refresh_picks_up_env_var_change()
        {
            // Save and restore the env var so the test is hermetic and
            // doesn't bleed state into other tests in the suite.
            var saved = Environment.GetEnvironmentVariable(global::FormCast.HeadlessMode.EnvVarName);
            try
            {
                Environment.SetEnvironmentVariable(global::FormCast.HeadlessMode.EnvVarName, "1");
                global::FormCast.HeadlessMode.Refresh();
                Assert.True(global::FormCast.HeadlessMode.IsEnabled);

                Environment.SetEnvironmentVariable(global::FormCast.HeadlessMode.EnvVarName, "0");
                global::FormCast.HeadlessMode.Refresh();
                Assert.False(global::FormCast.HeadlessMode.IsEnabled);

                Environment.SetEnvironmentVariable(global::FormCast.HeadlessMode.EnvVarName, null);
                global::FormCast.HeadlessMode.Refresh();
                Assert.False(global::FormCast.HeadlessMode.IsEnabled);
            }
            finally
            {
                Environment.SetEnvironmentVariable(global::FormCast.HeadlessMode.EnvVarName, saved);
                global::FormCast.HeadlessMode.Refresh();
            }
        }

        [Fact]
        public void EnvVarName_constant_is_stable()
        {
            // The env var name is a public contract; tests, docs, and
            // BTM scripts all reference "FORMCAST_HEADLESS" as a literal.
            // Pin it here so a rename is caught at test time.
            Assert.Equal("FORMCAST_HEADLESS", global::FormCast.HeadlessMode.EnvVarName);
        }
    }
}
