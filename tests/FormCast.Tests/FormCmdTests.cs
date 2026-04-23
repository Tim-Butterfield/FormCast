// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Text;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Validation-path tests for <c>@FORMCMD</c>. The happy path
    /// (running a real TCC command via the callback worker) requires
    /// <c>TakeCmd.dll</c> in the host process and is proven by the
    /// bridge BTM submitted alongside this milestone.
    /// </summary>
    public class FormCmdTests
    {
        private static StringBuilder Buf(string s = "") => new StringBuilder(s);

        [Fact]
        public void FORMCMD_empty_buffer_returns_bad_args()
        {
            var p = new global::FormCast.Plugin();
            var args = Buf(string.Empty);
            p.f_FORMCMD(args);
            Assert.Equal("20101", args.ToString());
        }

        [Fact]
        public void FORMCMD_whitespace_only_returns_bad_args()
        {
            var p = new global::FormCast.Plugin();
            var args = Buf("   ");
            p.f_FORMCMD(args);
            Assert.Equal("20101", args.ToString());
        }
    }
}
