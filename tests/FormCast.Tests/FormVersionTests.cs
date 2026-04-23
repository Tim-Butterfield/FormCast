// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Text;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Unit tests for the @FORMVERSION variable function. The C# method
    /// is named <c>f_FORMVERSION</c> per the JP Software TCC plugin SDK
    /// naming convention; the test exercises it directly without going
    /// through TCC.
    /// </summary>
    public class FormVersionTests
    {
        [Fact]
        public void FORMVERSION_returns_zero_on_success()
        {
            var plugin = new global::FormCast.Plugin();
            var args = new StringBuilder();
            int result = plugin.f_FORMVERSION(args);
            Assert.Equal(0, result);
        }

        [Fact]
        public void FORMVERSION_writes_a_version_string()
        {
            var plugin = new global::FormCast.Plugin();
            var args = new StringBuilder();
            plugin.f_FORMVERSION(args);
            string output = args.ToString();

            Assert.False(string.IsNullOrWhiteSpace(output));

            // Shape: dotted version, at least major.minor.build
            string[] parts = output.Split('.');
            Assert.True(parts.Length >= 3, $"expected at least 3 dot-separated parts, got: {output}");
            foreach (string part in parts)
            {
                Assert.True(int.TryParse(part, out _), $"expected integer part, got: {part}");
            }
        }

        [Fact]
        public void FORMVERSION_clears_input_args_before_writing()
        {
            // The function must overwrite the buffer, not append to it.
            // TCC passes an unused-but-non-empty buffer in some cases,
            // and we should not produce garbage like "junk0.0.2".
            var plugin = new global::FormCast.Plugin();
            var args = new StringBuilder("junk should be cleared");
            plugin.f_FORMVERSION(args);

            Assert.DoesNotContain("junk", args.ToString());
        }

        [Fact]
        public void FORMVERSION_listed_in_PluginInfo_Functions()
        {
            // The Functions list is what TCC reads to discover what
            // dispatch entry points the plugin offers. @FORMVERSION must
            // appear there or TCC won't route the call.
            var plugin = new global::FormCast.Plugin();
            var info = plugin.GetPluginInfo();
            Assert.Contains("@FORMVERSION", info.Functions);
        }
    }
}
