// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Text;

using FormCast.Interop;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// Validation-path tests for <c>@FORMSETENV</c> and the
    /// <see cref="TakeCmd.SetEnv(string, string?)"/> helper.
    ///
    /// The happy path (caller-scope variable write) cannot be exercised
    /// from xUnit because <c>TakeCmd.dll</c> is only present in a real
    /// TCC process. The bridge BTM submitted alongside this milestone
    /// is the proof for that path. Here we verify only the bits that
    /// short-circuit before the native call: argument count, empty
    /// name, and name-contains-equals rejection.
    /// </summary>
    public class FormSetEnvTests
    {
        private static StringBuilder Buf(string s = "") => new StringBuilder(s);

        // ---- f_FORMSETENV dispatch validation ----

        [Fact]
        public void FORMSETENV_empty_args_returns_bad_args()
        {
            var p = new global::FormCast.Plugin();
            var args = Buf(string.Empty);
            p.f_FORMSETENV(args);
            Assert.Equal("20101", args.ToString());
        }

        [Fact]
        public void FORMSETENV_too_many_args_returns_bad_args()
        {
            var p = new global::FormCast.Plugin();
            var args = Buf("a,b,c");
            p.f_FORMSETENV(args);
            Assert.Equal("20101", args.ToString());
        }

        [Fact]
        public void FORMSETENV_name_with_equals_returns_minus_one()
        {
            // Helper rejects names containing '=' before reaching the
            // native call, so this test is safe on a machine without
            // TakeCmd.dll.
            var p = new global::FormCast.Plugin();
            var args = Buf("foo=bar,value");
            p.f_FORMSETENV(args);
            Assert.Equal("-1", args.ToString());
        }

        // ---- TakeCmd.SetEnv direct validation ----

        [Fact]
        public void SetEnv_null_name_returns_minus_one()
        {
            Assert.Equal(-1, TakeCmd.SetEnv(null!, "value"));
        }

        [Fact]
        public void SetEnv_empty_name_returns_minus_one()
        {
            Assert.Equal(-1, TakeCmd.SetEnv(string.Empty, "value"));
        }

        [Fact]
        public void SetEnv_name_containing_equals_returns_minus_one()
        {
            Assert.Equal(-1, TakeCmd.SetEnv("name=oops", "value"));
        }
    }
}
