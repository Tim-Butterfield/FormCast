// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Linq;
using System.Text;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the FORMPIPE streaming command and the
 /// appendtext pseudo-prop. FORMPIPE reads stdin which is not
 /// testable in xUnit (no pipe), so we test the underlying
 /// AppendTextToControl mechanism via the appendtext pseudo-prop
 /// and test FORMPIPE's argument validation directly.
 /// </summary>
 public class FormPipeTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormPipeTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "pipetest")
 {
 var args = Buf($"form,{name},10,20,400,300");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 // -----------------------------------------------------------------
 // appendtext pseudo-prop on MEMO
 // -----------------------------------------------------------------

 [Fact]
 public void Appendtext_on_realized_MEMO_appends_lines()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},memo,MEMO,5,5,390,280,"));
 _plugin.f_FORMSHOW(Buf(h));

 _plugin.f_FORMSET(Buf($"{h},memo,appendtext,line one"));
 _plugin.f_FORMSET(Buf($"{h},memo,appendtext,line two"));
 _plugin.f_FORMSET(Buf($"{h},memo,appendtext,line three"));

 var getArgs = Buf($"{h},memo,text");
 _plugin.f_FORMGET(getArgs);
 string text = getArgs.ToString();
 Assert.Contains("line one", text);
 Assert.Contains("line two", text);
 Assert.Contains("line three", text);
 }

 [Fact]
 public void Appendtext_on_unrealized_MEMO_is_silent_noop()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},memo,MEMO,5,5,390,280,original"));
 // NOT calling FORMSHOW -- the form stays unrealized.

 var args = Buf($"{h},memo,appendtext,should not appear");
 _plugin.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 // Descriptor text unchanged.
 int seq = int.Parse(h.Split(':')[2],
 System.Globalization.CultureInfo.InvariantCulture);
 var form = _plugin.LookupDescriptor(seq)!;
 Assert.Equal("original", form.Controls.First(c => c.Id == "memo").Text);
 }

 [Fact]
 public void Appendtext_on_realized_RICHMEMO_appends_text()
 {
 string h = OpenForm();
 _plugin.f_FORMADD(Buf($"{h},rm,RICHMEMO,5,5,390,280,start"));
 _plugin.f_FORMSHOW(Buf(h));

 _plugin.f_FORMSET(Buf($"{h},rm,appendtext,appended line"));

 var getArgs = Buf($"{h},rm,text");
 _plugin.f_FORMGET(getArgs);
 string text = getArgs.ToString();
 Assert.Contains("start", text);
 Assert.Contains("appended line", text);
 }

 // -----------------------------------------------------------------
 // FORMPIPE argument validation
 // -----------------------------------------------------------------

 [Fact]
 public void FORMPIPE_rejects_too_few_args()
 {
 var args = Buf("L:0:1");
 int rc = _plugin.FORMPIPE(args);
 Assert.Equal(20101, rc);
 }

 [Fact]
 public void FORMPIPE_rejects_too_many_args()
 {
 var args = Buf("L:0:1 memo Blue extra");
 int rc = _plugin.FORMPIPE(args);
 Assert.Equal(20101, rc);
 }

 [Fact]
 public void FORMPIPE_invalid_handle_returns_error()
 {
 var args = Buf("notahandle memo");
 int rc = _plugin.FORMPIPE(args);
 Assert.Equal(20100, rc);
 }

 [Fact]
 public void FORMPIPE_unknown_handle_returns_error()
 {
 var args = Buf("L:0:99999 memo");
 int rc = _plugin.FORMPIPE(args);
 Assert.Equal(20100, rc);
 }

 [Fact]
 public void FORMPIPE_unknown_control_returns_error()
 {
 string h = OpenForm();
 var args = Buf($"{h} nosuchctrl");
 int rc = _plugin.FORMPIPE(args);
 Assert.Equal(20103, rc);
 }
 }
}
