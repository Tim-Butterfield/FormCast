// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.Text;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Unit tests for the @FORMOPEN / @FORMCLOSE / @FORMSTATE dispatch
 /// methods on <see cref="FormCast.Plugin"/>. These exercise the
 /// methods directly without going through TCC; the bridge BTM tests
 /// (see bridge/inbox/) cover the host-loader path end-to-end.
 /// </summary>
 public class PluginDispatchTests
 {
 private static StringBuilder Buf(string text = "") => new StringBuilder(text);

 [Fact]
 public void FORMOPEN_returns_a_handle_string()
 {
 var plugin = new global::FormCast.Plugin();
 var args = Buf("form,settings,10,20,400,300");

 int rc = plugin.f_FORMOPEN(args);
 Assert.Equal(0, rc);

 string handle = args.ToString();
 Assert.True(FormHandle.TryParse(handle, out int seq));
 Assert.True(seq >= 1);
 }

 // Convention: every dispatch method returns 0 from C# (so TCC
 // doesn't emit "System error N" to stderr) and signals errors
 // via the StringBuilder buffer value:
 // @FORMOPEN -> handle string on success / empty on failure
 // @FORMCLOSE -> "0" on success / error code as string on failure
 // @FORMSTATE -> bitmask as string / "-1" on invalid handle

 [Fact]
 public void FORMOPEN_returns_empty_buffer_on_too_few_arguments()
 {
 var plugin = new global::FormCast.Plugin();
 var args = Buf("form,settings,10,20"); // missing w, h

 int rc = plugin.f_FORMOPEN(args);
 Assert.Equal(0, rc);
 Assert.Equal(string.Empty, args.ToString());
 }

 [Fact]
 public void FORMOPEN_then_FORMCLOSE_round_trip()
 {
 var plugin = new global::FormCast.Plugin();

 var openArgs = Buf("form,settings,10,20,400,300");
 Assert.Equal(0, plugin.f_FORMOPEN(openArgs));
 string handle = openArgs.ToString();
 Assert.NotEqual(string.Empty, handle);

 var closeArgs = Buf(handle);
 Assert.Equal(0, plugin.f_FORMCLOSE(closeArgs));
 Assert.Equal("0", closeArgs.ToString());
 }

 [Fact]
 public void FORMCLOSE_on_unknown_handle_returns_error_code_in_buffer()
 {
 var plugin = new global::FormCast.Plugin();
 // Looks well-formed but the seq is huge and was never allocated
 var args = Buf("L:99999:99999");
 Assert.Equal(0, plugin.f_FORMCLOSE(args));
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMCLOSE_on_garbage_handle_returns_error_code_in_buffer()
 {
 var plugin = new global::FormCast.Plugin();
 var args = Buf("not-a-handle");
 Assert.Equal(0, plugin.f_FORMCLOSE(args));
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMSTATE_returns_enabled_for_valid_handle()
 {
 var plugin = new global::FormCast.Plugin();

 var openArgs = Buf("form,settings,10,20,400,300");
 plugin.f_FORMOPEN(openArgs);
 string handle = openArgs.ToString();

 var stateArgs = Buf(handle);
 Assert.Equal(0, plugin.f_FORMSTATE(stateArgs));
 Assert.Equal("2", stateArgs.ToString()); // enabled bit only in v0.0.x
 }

 [Fact]
 public void FORMSTATE_returns_minus_one_for_invalid_handle()
 {
 var plugin = new global::FormCast.Plugin();
 var args = Buf("L:99999:99999");
 Assert.Equal(0, plugin.f_FORMSTATE(args));
 Assert.Equal("-1", args.ToString());
 }

 [Fact]
 public void FORMSTATE_returns_minus_one_for_garbage_handle()
 {
 var plugin = new global::FormCast.Plugin();
 var args = Buf("not-a-handle");
 Assert.Equal(0, plugin.f_FORMSTATE(args));
 Assert.Equal("-1", args.ToString());
 }

 [Fact]
 public void FORMOPEN_uses_distinct_handles_for_distinct_calls()
 {
 var plugin = new global::FormCast.Plugin();

 var a = Buf("form,a,0,0,100,100");
 plugin.f_FORMOPEN(a);
 string handleA = a.ToString();

 var b = Buf("form,b,0,0,100,100");
 plugin.f_FORMOPEN(b);
 string handleB = b.ToString();

 Assert.NotEqual(handleA, handleB);
 }

 [Fact]
 public void Open_5_query_each_close_each_leaves_registry_empty()
 {
 // The end-to-end acceptance scenario from the roadmap 
 // "open 5 forms, query each, close each, registry empty".
 var plugin = new global::FormCast.Plugin();

 var handles = new string[5];
 for (int i = 0; i < 5; i++)
 {
 var openArgs = Buf($"form,form{i},0,0,100,100");
 Assert.Equal(0, plugin.f_FORMOPEN(openArgs));
 handles[i] = openArgs.ToString();
 }

 // Query each
 foreach (string h in handles)
 {
 var stateArgs = Buf(h);
 Assert.Equal(0, plugin.f_FORMSTATE(stateArgs));
 Assert.Equal("2", stateArgs.ToString());
 }

 // Close each
 foreach (string h in handles)
 {
 var closeArgs = Buf(h);
 Assert.Equal(0, plugin.f_FORMCLOSE(closeArgs));
 Assert.Equal("0", closeArgs.ToString());
 }

 // Querying any of them now returns the invalid-handle marker
 foreach (string h in handles)
 {
 var stateArgs = Buf(h);
 Assert.Equal(0, plugin.f_FORMSTATE(stateArgs));
 Assert.Equal("-1", stateArgs.ToString());
 }
 }
 }
}
