// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;

using FormCast.Host;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the FormCast.Host.exe scaffold:
 /// HostOptions parser, HostMutex name builder, version banner
 /// formatter, and the entry-point return-code surface.
 /// </summary>
 public class HostScaffoldTests
 {
 // -----------------------------------------------------------------
 // HostOptions.Parse
 // -----------------------------------------------------------------

 [Fact]
 public void Parse_with_no_args_returns_defaults()
 {
 var opts = HostOptions.Parse(Array.Empty<string>());
 Assert.False(opts.ShowHelp);
 Assert.False(opts.ShowVersion);
 Assert.Equal(string.Empty, opts.SessionId);
 Assert.Equal(0, opts.RunSeconds);
 }

 [Theory]
 [InlineData("--help")]
 [InlineData("-h")]
 [InlineData("/?")]
 public void Parse_help_flag_in_any_form_sets_ShowHelp(string flag)
 {
 var opts = HostOptions.Parse(new[] { flag });
 Assert.True(opts.ShowHelp);
 }

 [Theory]
 [InlineData("--version")]
 [InlineData("-v")]
 public void Parse_version_flag_in_any_form_sets_ShowVersion(string flag)
 {
 var opts = HostOptions.Parse(new[] { flag });
 Assert.True(opts.ShowVersion);
 }

 [Fact]
 public void Parse_session_id_consumes_the_following_arg()
 {
 var opts = HostOptions.Parse(new[] { "--session-id", "abc123" });
 Assert.Equal("abc123", opts.SessionId);
 }

 [Fact]
 public void Parse_session_id_without_value_throws()
 {
 Assert.Throws<ArgumentException>(() =>
 HostOptions.Parse(new[] { "--session-id" }));
 }

 [Fact]
 public void Parse_run_seconds_consumes_the_following_arg()
 {
 var opts = HostOptions.Parse(new[] { "--run-seconds", "5" });
 Assert.Equal(5, opts.RunSeconds);
 }

 [Fact]
 public void Parse_run_seconds_zero_is_allowed()
 {
 var opts = HostOptions.Parse(new[] { "--run-seconds", "0" });
 Assert.Equal(0, opts.RunSeconds);
 }

 [Fact]
 public void Parse_run_seconds_rejects_negative()
 {
 Assert.Throws<ArgumentException>(() =>
 HostOptions.Parse(new[] { "--run-seconds", "-1" }));
 }

 [Fact]
 public void Parse_run_seconds_rejects_garbage()
 {
 Assert.Throws<ArgumentException>(() =>
 HostOptions.Parse(new[] { "--run-seconds", "abc" }));
 }

 [Fact]
 public void Parse_unknown_option_throws()
 {
 Assert.Throws<ArgumentException>(() =>
 HostOptions.Parse(new[] { "--no-such-flag" }));
 }

 [Fact]
 public void Parse_combination_round_trips_via_ToDebugString()
 {
 var opts = HostOptions.Parse(new[] {
 "--session-id", "s1", "--run-seconds", "3", "--version" });
 // idle-seconds defaults to 60 and is part of
 // the debug string.
 Assert.Equal("help=0 version=1 session-id=s1 run-seconds=3 idle-seconds=60",
 opts.ToDebugString());
 }

 [Fact]
 public void UsageText_mentions_each_option()
 {
 string usage = HostOptions.UsageText;
 Assert.Contains("--help", usage);
 Assert.Contains("--version", usage);
 Assert.Contains("--session-id", usage);
 Assert.Contains("--run-seconds", usage);
 }

 // -----------------------------------------------------------------
 // HostMutex.BuildName
 // -----------------------------------------------------------------

 [Fact]
 public void BuildName_uses_explicit_session_id_when_supplied()
 {
 string name = HostMutex.BuildName("custom");
 Assert.Equal("Local\\FormCast.Host.custom", name);
 }

 [Fact]
 public void BuildName_falls_back_to_current_session_when_empty()
 {
 string name = HostMutex.BuildName(string.Empty);
 Assert.StartsWith("Local\\FormCast.Host.", name);
 // Suffix should be either an integer session id or the
 // "default" fallback for non-Windows hosts.
 string suffix = name.Substring("Local\\FormCast.Host.".Length);
 Assert.False(string.IsNullOrEmpty(suffix));
 }

 [Fact]
 public void BuildName_falls_back_to_current_session_when_null()
 {
 string name = HostMutex.BuildName(null);
 Assert.StartsWith("Local\\FormCast.Host.", name);
 }

 // -----------------------------------------------------------------
 // BuildVersionBanner
 // -----------------------------------------------------------------

 [Fact]
 public void BuildVersionBanner_includes_FormCast_Host_and_version()
 {
 string banner = Program.BuildVersionBanner();
 Assert.StartsWith("FormCast.Host ", banner);
 Assert.Contains("Cross-process daemon", banner);
 Assert.Contains("MIT License", banner);
 }
 }
}
