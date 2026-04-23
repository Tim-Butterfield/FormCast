// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using TakeCommand.Plugin;

using Xunit;

namespace FormCast.Tests
{
    /// <summary>
    /// First-light tests for the FormCast.Tests project. These verify
    /// that the test runner is wired up correctly and that the production
    /// assembly is referenced and loadable. They are not feature tests.
    /// </summary>
    public class SmokeTests
    {
        [Fact]
        public void Trivially_true()
        {
            // The simplest possible passing test. If this fails, the test
            // host is not configured correctly.
            Assert.True(true);
        }

        [Fact]
        public void Plugin_assembly_is_referenced()
        {
            // The production assembly is referenced via ProjectReference.
            // Asking for the type forces the assembly to load and proves
            // both the reference and the type's accessibility.
            var pluginType = typeof(global::FormCast.Plugin);
            Assert.NotNull(pluginType);
            Assert.Equal("FormCast", pluginType.Assembly.GetName().Name);
        }

        [Fact]
        public void Plugin_implements_ITCCPlugin()
        {
            // FormCast.Plugin must implement TakeCommand.Plugin.ITCCPlugin
            // for the TC-DotNetPluginHost64.dll bridge to discover it via
            // reflection. This test catches accidental interface removal
            // at compile time (it would fail to even build if Plugin
            // didn't implement the interface, because of the cast below).
            ITCCPlugin instance = new global::FormCast.Plugin();
            Assert.NotNull(instance);
        }

        [Fact]
        public void GetPluginInfo_returns_expected_metadata()
        {
            // The metadata is what TCC reads when it lists plugins; the
            // values must match what FormCast publishes. Version numbers
            // are checked for shape (non-negative ints) rather than exact
            // value, because the version bumps with every milestone and
            // we don't want a smoke test to fail every release.
            var instance = new global::FormCast.Plugin();
            var info = instance.GetPluginInfo();

            Assert.NotNull(info);
            Assert.Equal("FormCast", info.Name);
            Assert.Equal("Tim Butterfield", info.Author);
            Assert.True(info.Major >= 0);
            Assert.True(info.Minor >= 0);
            Assert.True(info.Build >= 0);
            Assert.False(string.IsNullOrEmpty(info.Functions));
        }
    }
}
