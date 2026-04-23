// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

using FormCast.Forms;
using FormCast.Forms.Controls;
using FormCast.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the RICHMEMO control: WPF RichTextBox embedded
 /// in WindowsFormsIntegration.ElementHost. Covers recognition,
 /// realize as ElementHost-with-RichTextBox, the three highlighting
 /// modes (appendcolor / appendstyle / loadrules), the live text
 /// round-trip via the SetPlainText / GetPlainText helpers, and
 /// the forced-shutdown HandleDestroyed dispose-order safety net.
 /// </summary>
 public class RichMemoTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;
 private readonly GuiHostThread _host;

 public RichMemoTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 _host = new GuiHostThread();
 _host.Start();
 }

 public void Dispose()
 {
 _host.Stop();
 _host.Dispose();
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "rmtest")
 {
 var args = Buf($"form,{name},10,20,500,400");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private static int SeqOf(string handle) =>
 int.Parse(handle.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);

 private void AddRichMemo(string handle, string id = "rm",
 string text = "", int x = 5, int y = 5, int w = 480, int h = 380)
 {
 var args = Buf($"{handle},{id},RICHMEMO,{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 // -----------------------------------------------------------------
 // Recognition + descriptor
 // -----------------------------------------------------------------

 [Fact]
 public void RICHMEMO_is_a_recognized_control_type()
 {
 Assert.True(ControlBuilders.IsRecognizedType("RICHMEMO"));
 Assert.True(ControlBuilders.IsRecognizedType("richmemo"));
 }

 [Fact]
 public void FORMADD_RICHMEMO_succeeds_with_initial_text()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "Hello world");
 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var rm = form.Controls.First(c => c.Id == "rm");
 Assert.Equal("RICHMEMO", rm.Type);
 Assert.Equal("Hello world", rm.Text);
 }

 // -----------------------------------------------------------------
 // Realize: produces an ElementHost with a RichTextBox child
 // -----------------------------------------------------------------

 [Fact]
 public void Realize_RICHMEMO_creates_ElementHost_with_RichTextBox_child()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "initial");
 var form = _plugin.LookupDescriptor(SeqOf(h))!;

 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 Control c = realized.Controls[0];
 var host = Assert.IsType<ElementHost>(c);
 var editor = RichMemoBuilder.GetEditor(host);
 Assert.NotNull(editor);
 Assert.Equal("initial", RichMemoBuilder.GetPlainText(host));
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 [Fact]
 public void Realize_RICHMEMO_with_readonly_flag_sets_IsReadOnly()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "locked");
 _plugin.f_FORMSET(Buf($"{h},rm,readonly,1"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Form realized = FormRealizer.Realize(form, _host);
 try
 {
 _host.Invoke(() =>
 {
 var host = (ElementHost)realized.Controls[0];
 var editor = RichMemoBuilder.GetEditor(host);
 Assert.True(editor!.IsReadOnly);
 });
 }
 finally
 {
 FormRealizer.Destroy(realized, _host);
 }
 }

 // -----------------------------------------------------------------
 // appendcolor / appendstyle live ops
 // -----------------------------------------------------------------

 [Fact]
 public void Appendcolor_appends_text_with_color_run()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "head");
 int seq = SeqOf(h);

 // Realize so the live ops have something to mutate.
 _plugin.f_FORMSHOW(Buf(h));

 _plugin.f_FORMSET(Buf($"{h},rm,appendcolor,red text|Red"));

 // Read back via FORMGET text -- the live document should
 // contain both the initial and the appended text. Note
 // that ArgParser trims comma-separated args, so leading/
 // trailing whitespace inside an arg gets stripped before
 // it reaches the plugin.
 var getArgs = Buf($"{h},rm,text");
 _plugin.f_FORMGET(getArgs);
 string content = getArgs.ToString();
 Assert.Contains("head", content);
 Assert.Contains("red text", content);
 }

 [Fact]
 public void Appendstyle_appends_text_with_style_run()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "");
 _plugin.f_FORMSHOW(Buf(h));

 _plugin.f_FORMSET(Buf($"{h},rm,appendstyle,bold here|bold"));
 _plugin.f_FORMSET(Buf($"{h},rm,appendstyle,italic part|italic"));
 _plugin.f_FORMSET(Buf($"{h},rm,appendstyle,underlined|underline"));

 var getArgs = Buf($"{h},rm,text");
 _plugin.f_FORMGET(getArgs);
 string content = getArgs.ToString();
 Assert.Contains("bold here", content);
 Assert.Contains("italic part", content);
 Assert.Contains("underlined", content);
 }

 [Fact]
 public void Loadrules_applies_regex_highlighting_to_existing_content()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "alpha beta gamma");
 _plugin.f_FORMSHOW(Buf(h));

 // Apply two regex rules. We can't easily inspect WPF
 // foreground brush from a unit test, so we test the
 // contract: loadrules should not crash and the document
 // text should be unchanged after the operation.
 _plugin.f_FORMSET(Buf($"{h},rm,loadrules,beta|Red,gamma|Blue"));

 var getArgs = Buf($"{h},rm,text");
 _plugin.f_FORMGET(getArgs);
 Assert.Equal("alpha beta gamma", getArgs.ToString());
 }

 [Fact]
 public void Settext_replaces_live_document_content()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "old");
 _plugin.f_FORMSHOW(Buf(h));

 _plugin.f_FORMSET(Buf($"{h},rm,text,new content"));

 var getArgs = Buf($"{h},rm,text");
 _plugin.f_FORMGET(getArgs);
 Assert.Equal("new content", getArgs.ToString());
 }

 // -----------------------------------------------------------------
 // Live ops on an unrealized form are silent no-ops (no crash)
 // -----------------------------------------------------------------

 [Fact]
 public void Appendcolor_on_unrealized_form_is_silent_noop()
 {
 string h = OpenForm();
 AddRichMemo(h, "rm", "head");

 // Do NOT call FORMSHOW. The realizer never runs.
 var args = Buf($"{h},rm,appendcolor,never|Red");
 _plugin.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 // Descriptor text is unchanged.
 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 Assert.Equal("head", form.Controls.First(c => c.Id == "rm").Text);
 }

 // -----------------------------------------------------------------
 // RichMemoBuilder unit tests: AppendColor / AppendStyle /
 // LoadRules driven directly against an ElementHost on the
 // GUI thread, bypassing the dispatch surface.
 // -----------------------------------------------------------------

 [Fact]
 public void RichMemoBuilder_AppendColor_returns_false_on_bad_color_name()
 {
 var desc = new ControlDescriptor { Type = "RICHMEMO", Id = "rm", Text = "" };
 ElementHost? host = null;
 try
 {
 _host.Invoke(() => host = RichMemoBuilder.Build(desc));
 bool ok = false;
 _host.Invoke(() =>
 {
 ok = RichMemoBuilder.AppendColor(host!, "x", "NotARealColor");
 });
 Assert.False(ok);
 }
 finally
 {
 if (host is not null) { _host.Invoke(() => host.Dispose()); }
 }
 }

 [Fact]
 public void RichMemoBuilder_LoadRules_skips_invalid_regex_and_color()
 {
 var desc = new ControlDescriptor { Type = "RICHMEMO", Id = "rm", Text = "abc" };
 ElementHost? host = null;
 try
 {
 _host.Invoke(() => host = RichMemoBuilder.Build(desc));
 int applied = -1;
 _host.Invoke(() =>
 {
 // Three rules: invalid regex, invalid color,
 // valid pair. Only the third one counts.
 applied = RichMemoBuilder.LoadRules(host!,
 "([invalid|Red,abc|NotAColor,a|Blue");
 });
 Assert.Equal(1, applied);
 }
 finally
 {
 if (host is not null) { _host.Invoke(() => host.Dispose()); }
 }
 }

 // -----------------------------------------------------------------
 // Forced shutdown: tearing down the realized form does not
 // crash, even though the form holds a WPF visual tree.
 // -----------------------------------------------------------------

 [Fact]
 public void Forced_shutdown_destroys_RICHMEMO_form_cleanly()
 {
 string h = OpenForm("fs");
 AddRichMemo(h, "rm", "content");
 _plugin.f_FORMSHOW(Buf(h));
 int seq = SeqOf(h);
 Assert.True(_plugin.IsRealized(seq));

 // FORMCLOSE drives the destroy path on the GUI thread.
 // The HandleDestroyed handler installed in
 // RichMemoBuilder.Build nulls ElementHost.Child before
 // the host disposes its handle, satisfying the WPF
 // dispose-order contract from PLUGIN_DESIGN.md 4.6.
 var args = Buf(h);
 _plugin.f_FORMCLOSE(args);
 Assert.Equal("0", args.ToString());
 Assert.False(_plugin.IsRealized(seq));
 }
 }
}
